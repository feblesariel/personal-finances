using System.Collections.Immutable;
using System.Data;
using System.Text.Json;
using Dapper;
using MySqlConnector;

namespace PersonalFinances.Core;
public sealed partial class FinanceStore
{
    public async Task<BackupSnapshot> CaptureBackupAsync(string applicationVersion,IProgress<string>? progress=null)
    {
        await using var c=await Open();
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var result=await CaptureBackup(c,tx,applicationVersion,progress);
        await tx.CommitAsync();
        return result;
    }
    private static async Task<BackupSnapshot> CaptureBackup(MySqlConnection c,MySqlTransaction tx,string version,IProgress<string>? progress=null)
    {
        var tables=(await c.QueryAsync<(string Name,string Engine)>("SELECT TABLE_NAME,ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()",transaction:tx)).ToList();
        string[] known=["cards","categories","periods","movements","audit_log","settings","schema_migrations"];
        if(tables.Count!=known.Length||tables.Any(t=>!known.Contains(t.Name,StringComparer.OrdinalIgnoreCase)||t.Engine!="InnoDB"))
            throw new RuleException("El esquema contiene tablas desconocidas o no transaccionales. No se puede garantizar un respaldo completo; actualizá la aplicación.");
        // The first consistent read establishes the MVCC snapshot for ALL accounting tables.
        var migrations=(await c.QueryAsync<int>("SELECT version FROM schema_migrations ORDER BY version",transaction:tx)).ToArray();
        if(!migrations.SequenceEqual(new[]{1,2,3}))throw new RuleException("La versión de esta base no es compatible con los respaldos de esta aplicación.");
        var cards=(await c.QueryAsync<BackupNamed>("SELECT id,seed_key SeedKey,name,active FROM cards ORDER BY id",transaction:tx)).ToImmutableArray();
        progress?.Report("Leyendo tarjetas y categorías…");
        var categories=(await c.QueryAsync<BackupNamed>("SELECT id,seed_key SeedKey,name,active FROM categories ORDER BY id",transaction:tx)).ToImmutableArray();
        var periods=(await c.QueryAsync<BackupPeriod>(PeriodSql+" ORDER BY id",transaction:tx)).ToImmutableArray();
        progress?.Report("Leyendo todo el historial…");
        var movements=(await c.QueryAsync<BackupMovement>("""
            SELECT id,token,card_id CardId,period_id PeriodId,manual_period ManualPeriod,category_id CategoryId,
            movement_date Date,amount,currency,refund,original_id OriginalId,description,notes,version,
            created_at CreatedAt,updated_at UpdatedAt,deleted_at DeletedAt FROM movements ORDER BY id
            """,transaction:tx)).ToImmutableArray();
        var audit=(await c.QueryAsync<BackupAudit>("SELECT id,entity,entity_id EntityId,action,before_json BeforeJson,changed_at ChangedAt FROM audit_log ORDER BY id",transaction:tx)).ToImmutableArray();
        // Explicit allowlist: never serialize connection/device settings or arbitrary future secrets.
        var settings=(await c.QueryAsync<(string Key,string Value)>("SELECT `key`,`value` FROM settings WHERE `key` IN ('ledger_id','revision') OR `key` LIKE 'import:%'",transaction:tx)).ToDictionary(x=>x.Key,x=>x.Value);
        var imports=ImmutableArray.CreateBuilder<BackupImportManifest>();
        foreach(var setting in settings.Where(s=>s.Key.StartsWith("import:",StringComparison.Ordinal)))
        {
            using var json=JsonDocument.Parse(setting.Value);var e=json.RootElement;
            // Only the business manifest fields cross computers; extra local metadata is excluded.
            imports.Add(new(setting.Key,e.GetProperty("Batch").GetString()!,e.GetProperty("SourceHash").GetString()!,e.GetProperty("Total").GetDecimal(),
                e.GetProperty("Lines").Deserialize<ImmutableArray<BackupImportLine>>(BackupValidation.Json),e.GetProperty("ImportedAt").GetDateTimeOffset()));
        }
        var document=new BackupDocument{ApplicationVersion=version,LedgerId=settings.GetValueOrDefault("ledger_id")??"",Cards=cards,Categories=categories,Periods=periods,Movements=movements,Audit=audit,Imports=imports.ToImmutable()};
        await Task.Run(()=>BackupValidation.Validate(document));
        return new(document,long.Parse(settings["revision"],System.Globalization.CultureInfo.InvariantCulture));
    }

    internal Task<bool> RestoreBackupAsync(PreparedRestore prepared,IProgress<string>? progress) => Write(async(c,tx)=>
    {
        await Task.Run(()=>BackupValidation.Validate(prepared.Incoming));
        var before=await CaptureBackup(c,tx,prepared.Incoming.ApplicationVersion);
        if(before.Revision!=prepared.DestinationRevision||await Task.Run(()=>BackupValidation.Fingerprint(before.Document))!=prepared.DestinationFingerprint)
            throw new RuleException("La base cambió desde la vista previa. Volvé a seleccionar el respaldo para revisar los datos actuales antes de restaurar.");
        progress?.Report("Restaurando dentro de una transacción segura…");
        // DML only: no TRUNCATE, DDL, disabled foreign keys or implicit commits.
        await c.ExecuteAsync("UPDATE movements SET original_id=NULL; DELETE FROM audit_log; DELETE FROM movements; DELETE FROM periods; DELETE FROM categories; DELETE FROM cards; DELETE FROM settings WHERE `key`='ledger_id' OR `key` LIKE 'import:%';",transaction:tx);
        var b=prepared.Incoming;
        if(b.Cards.Length>0)await c.ExecuteAsync("INSERT INTO cards(id,seed_key,name,active) VALUES(@Id,@SeedKey,@Name,@Active)",b.Cards,tx);
        if(b.Categories.Length>0)await c.ExecuteAsync("INSERT INTO categories(id,seed_key,name,active) VALUES(@Id,@SeedKey,@Name,@Active)",b.Categories,tx);
        if(b.Periods.Length>0)await c.ExecuteAsync("INSERT INTO periods(id,card_id,name,start_date,closing_date,due_date) VALUES(@Id,@CardId,@Name,@Start,@Closing,@Due)",b.Periods,tx);
        if(b.Movements.Length>0)await c.ExecuteAsync("""
            INSERT INTO movements(id,token,card_id,period_id,manual_period,category_id,movement_date,amount,currency,refund,original_id,description,notes,version,created_at,updated_at,deleted_at)
            VALUES(@Id,@Token,@CardId,@PeriodId,@ManualPeriod,@CategoryId,@Date,@Amount,@Currency,@Refund,NULL,@Description,@Notes,@Version,@CreatedAt,@UpdatedAt,@DeletedAt)
            """,b.Movements,tx);
        foreach(var m in b.Movements.Where(m=>m.OriginalId!=null))await c.ExecuteAsync("UPDATE movements SET original_id=@OriginalId,updated_at=@UpdatedAt WHERE id=@Id",m,tx);
        if(b.Audit.Length>0)await c.ExecuteAsync("INSERT INTO audit_log(id,entity,entity_id,action,before_json,changed_at) VALUES(@Id,@Entity,@EntityId,@Action,@BeforeJson,@ChangedAt)",b.Audit,tx);
        await c.ExecuteAsync("INSERT INTO settings VALUES('ledger_id',@LedgerId)",b,tx);
        foreach(var manifest in b.Imports)
        {
            var value=JsonSerializer.Serialize(new{manifest.Batch,manifest.SourceHash,manifest.Total,manifest.Lines,manifest.ImportedAt},BackupValidation.Json);
            await c.ExecuteAsync("INSERT INTO settings VALUES(@key,@value)",new{key=manifest.Key,value},tx);
        }
        progress?.Report("Verificando relaciones, importes e historial…");
        var restored=await CaptureBackup(c,tx,b.ApplicationVersion);
        if(!await Task.Run(()=>BackupValidation.Fingerprint(restored.Document)==BackupValidation.Fingerprint(b)))
            throw new RuleException("La verificación de la restauración no coincidió. Se canceló la operación y se conservan los datos anteriores.");
        return true;
    });
}
