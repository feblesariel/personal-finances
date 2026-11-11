using System.Data;
using System.Text.Json;
using Dapper;
using MySqlConnector;

namespace PersonalFinances.Core;
public sealed class FinanceStore(ConnectionProfile profile)
{
    public const string ExportFileName = "RESUMEN_TARJETAS.txt";
    public ConnectionProfile Profile => profile;
    private async Task<MySqlConnection> Open() { var c=new MySqlConnection(profile.ConnectionString); try { await c.OpenAsync(); return c; } catch { await c.DisposeAsync(); throw; } }
    private const string PeriodSql="SELECT id,card_id CardId,name,start_date Start,closing_date Closing,due_date Due FROM periods";
    private const string MovementSql="""
        SELECT m.id,m.token,m.card_id CardId,m.period_id PeriodId,m.manual_period ManualPeriod,m.category_id CategoryId,
        m.movement_date Date,m.amount,m.currency,m.refund,m.original_id OriginalId,m.description,m.notes,m.version,
        c.name CardName,k.name CategoryName,p.name PeriodName
        FROM movements m JOIN cards c ON c.id=m.card_id JOIN categories k ON k.id=m.category_id
        LEFT JOIN periods p ON p.id=m.period_id WHERE m.deleted_at IS NULL
        """;
    public async Task InitializeAsync()
    {
        await using var c=await Open();
        var migrationLock="pf_migrations_"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(profile.Database.ToLowerInvariant())))[..40];
        var locked=await c.ExecuteScalarAsync<int>("SELECT GET_LOCK(@name,15)",new {name=migrationLock});
        if(locked!=1) throw new RuleException("Otra instancia está actualizando la base. Intentá nuevamente.");
        try
        {
            var tables=(await c.QueryAsync<string>("SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()")).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(tables.Count>0&&!tables.Contains("schema_migrations"))throw new RuleException("La base contiene tablas ajenas a Personal Finances. Elegí una base vacía; no se modificó su contenido.");
            await c.ExecuteAsync("CREATE TABLE IF NOT EXISTS schema_migrations(version INT PRIMARY KEY, applied_at DATETIME(6) NOT NULL) ENGINE=InnoDB");
            if(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM schema_migrations WHERE version=1")==0)
            {
                // DDL is idempotent because MySQL DDL commits implicitly; interrupted migrations can be resumed.
                await c.ExecuteAsync("""
                  CREATE TABLE IF NOT EXISTS cards(id INT AUTO_INCREMENT PRIMARY KEY,seed_key VARCHAR(40) UNIQUE NULL,name VARCHAR(80) NOT NULL UNIQUE,active BOOLEAN NOT NULL DEFAULT 1) ENGINE=InnoDB;
                  CREATE TABLE IF NOT EXISTS categories(id INT AUTO_INCREMENT PRIMARY KEY,seed_key VARCHAR(40) UNIQUE NULL,name VARCHAR(80) NOT NULL UNIQUE,active BOOLEAN NOT NULL DEFAULT 1) ENGINE=InnoDB;
                  CREATE TABLE IF NOT EXISTS periods(id INT AUTO_INCREMENT PRIMARY KEY,card_id INT NOT NULL,name VARCHAR(100) NOT NULL,start_date DATE NOT NULL,closing_date DATE NOT NULL,due_date DATE NOT NULL,
                    FOREIGN KEY(card_id) REFERENCES cards(id),INDEX ix_period_dates(card_id,start_date,closing_date),CHECK(start_date<=closing_date),CHECK(due_date>=closing_date)) ENGINE=InnoDB;
                  CREATE TABLE IF NOT EXISTS settings(`key` VARCHAR(80) PRIMARY KEY,`value` TEXT NOT NULL) ENGINE=InnoDB;
                  CREATE TABLE IF NOT EXISTS movements(id BIGINT AUTO_INCREMENT PRIMARY KEY,token CHAR(32) NOT NULL UNIQUE,card_id INT NOT NULL,period_id INT NULL,manual_period BOOLEAN NOT NULL DEFAULT 0,category_id INT NOT NULL,
                    movement_date DATE NOT NULL,amount DECIMAL(18,2) NOT NULL,currency CHAR(3) NOT NULL,refund BOOLEAN NOT NULL DEFAULT 0,original_id BIGINT NULL,description VARCHAR(200) NOT NULL DEFAULT '',notes TEXT NOT NULL,
                    version INT NOT NULL DEFAULT 1,created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),deleted_at DATETIME(6) NULL,
                    FOREIGN KEY(card_id) REFERENCES cards(id),FOREIGN KEY(period_id) REFERENCES periods(id),FOREIGN KEY(category_id) REFERENCES categories(id),FOREIGN KEY(original_id) REFERENCES movements(id),
                    INDEX ix_movement_period(period_id,deleted_at,currency),INDEX ix_movement_date(card_id,movement_date),INDEX ix_movement_category(category_id,currency),CHECK(amount>0),CHECK(currency IN ('ARS','USD'))) ENGINE=InnoDB;
                  CREATE TABLE IF NOT EXISTS audit_log(id BIGINT AUTO_INCREMENT PRIMARY KEY,entity VARCHAR(40) NOT NULL,entity_id BIGINT NOT NULL,action VARCHAR(20) NOT NULL,before_json JSON NULL,changed_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)) ENGINE=InnoDB;
                  INSERT IGNORE INTO settings SELECT 'bootstrap_pending','1' WHERE NOT EXISTS(SELECT 1 FROM cards) AND NOT EXISTS(SELECT 1 FROM categories) AND NOT EXISTS(SELECT 1 FROM movements);
                  INSERT INTO schema_migrations VALUES(1,UTC_TIMESTAMP(6));
                  """);
            }
            if(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM schema_migrations WHERE version=2")==0)
            {
                await using var tx=await c.BeginTransactionAsync();
                var initializeDefaults=await c.ExecuteScalarAsync<string?>("SELECT `value` FROM settings WHERE `key`='bootstrap_pending'",transaction:tx)=="1";
                if(initializeDefaults)
                {
                foreach(var (key,name) in new[]{("visa","VISA"),("mastercard","MASTERCARD")})
                    await c.ExecuteAsync("INSERT IGNORE INTO cards(seed_key,name) VALUES(@key,@name)",new {key,name},tx);
                string[] names=["Compras generales","Supermercado","Delivery","Combustible / Nafta","Estacionamiento","Salidas / Restaurantes","Regalos","Servicios","Suscripciones","Salud / Farmacia","Transporte","Otros"];
                for(var i=0;i<names.Length;i++) await c.ExecuteAsync("INSERT IGNORE INTO categories(seed_key,name) VALUES(@key,@name)",new {key="category_"+i,name=names[i]},tx);
                }
                await c.ExecuteAsync("INSERT IGNORE INTO settings VALUES('revision','1'),('export_revision','0'); DELETE FROM settings WHERE `key`='bootstrap_pending';",transaction:tx);
                await c.ExecuteAsync("INSERT INTO schema_migrations VALUES(2,UTC_TIMESTAMP(6))",transaction:tx);
                await tx.CommitAsync();
            }
        }
        finally { await c.ExecuteAsync("SELECT RELEASE_LOCK(@name)",new {name=migrationLock}); }
    }
    public async Task<Snapshot> ReadAsync()
    {
        await using var c=await Open(); await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var cards=(await c.QueryAsync<Card>("SELECT id,name,active FROM cards ORDER BY id",transaction:tx)).ToList();
        var cats=(await c.QueryAsync<Category>("SELECT id,name,active FROM categories ORDER BY id",transaction:tx)).ToList();
        var periods=(await c.QueryAsync<Period>(PeriodSql+" ORDER BY start_date DESC",transaction:tx)).ToList();
        var movements=(await c.QueryAsync<Movement>(MovementSql+" ORDER BY m.movement_date DESC,m.id DESC",transaction:tx)).ToList();
        var settings=(await c.QueryAsync<(string Key,string Value)>("SELECT `key`,`value` FROM settings",transaction:tx)).ToDictionary(x=>x.Key,x=>x.Value);
        await tx.CommitAsync(); return new(cards,cats,periods,movements,settings);
    }
    private async Task<T> Write<T>(Func<MySqlConnection,MySqlTransaction,Task<T>> work)
    {
        await using var c=await Open(); await using var tx=await c.BeginTransactionAsync();
        // Every mutation acquires this row, making assignment, overlap checks and multi-step edits serializable.
        await c.ExecuteScalarAsync<string>("SELECT `value` FROM settings WHERE `key`='revision' FOR UPDATE",transaction:tx);
        var result=await work(c,tx);
        await c.ExecuteAsync("UPDATE settings SET `value`=CAST(`value` AS UNSIGNED)+1 WHERE `key`='revision'",transaction:tx);
        await tx.CommitAsync(); return result;
    }
    private static Task Audit(MySqlConnection c,MySqlTransaction t,string entity,long id,string action,object? before) => c.ExecuteAsync("INSERT INTO audit_log(entity,entity_id,action,before_json) VALUES(@entity,@id,@action,@json)",new {entity,id,action,json=before==null?null:JsonSerializer.Serialize(before)},t);
    public Task<int> SaveCardAsync(Card card) => SaveNamed("cards",card.Id,card.Name,card.Active);
    public Task<int> SaveCategoryAsync(Category category) => SaveNamed("categories",category.Id,category.Name,category.Active);
    private Task<int> SaveNamed(string table,int id,string name,bool active) => Write(async(c,t)=>
    {
        name=name.Trim(); if(name.Length is 0 or >80) throw new RuleException("Ingresá un nombre de 1 a 80 caracteres.");
        if(await c.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {table} WHERE name=@name AND id<>@id",new {name,id},t)>0) throw new RuleException("Ya existe un registro con ese nombre.");
        if(id==0) return await c.ExecuteScalarAsync<int>($"INSERT INTO {table}(name,active) VALUES(@name,@active); SELECT LAST_INSERT_ID();",new {name,active},t);
        var before=await c.QuerySingleAsync($"SELECT * FROM {table} WHERE id=@id",new {id},t); await Audit(c,t,table,id,"edit",before);
        await c.ExecuteAsync($"UPDATE {table} SET name=@name,active=@active WHERE id=@id",new {id,name,active},t); return id;
    });
    public Task<int> SavePeriodAsync(Period p) => Write(async(c,t)=>
    {
        p.Start=p.Start.Date;p.Closing=p.Closing.Date;p.Due=p.Due.Date;p.Name=p.Name.Trim();
        var periods=(await c.QueryAsync<Period>(PeriodSql,new {},t)).ToList(); PeriodRules.Validate(p,periods);
        if(!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM cards WHERE id=@CardId)",p,t)) throw new RuleException("La tarjeta no existe.");
        var old=periods.SingleOrDefault(x=>x.Id==p.Id);
        if(old!=null && old.CardId!=p.CardId) throw new RuleException("No se puede cambiar la tarjeta de un período existente.");
        if(old!=null) { await Audit(c,t,"periods",p.Id,"edit",old); await c.ExecuteAsync("UPDATE periods SET name=@Name,start_date=@Start,closing_date=@Closing,due_date=@Due WHERE id=@Id",p,t); }
        else p.Id=await c.ExecuteScalarAsync<int>("INSERT INTO periods(card_id,name,start_date,closing_date,due_date) VALUES(@CardId,@Name,@Start,@Closing,@Due); SELECT LAST_INSERT_ID();",p,t);
        periods.RemoveAll(x=>x.Id==p.Id);periods.Add(p);
        var moves=await c.QueryAsync<Movement>(MovementSql+" AND m.card_id=@CardId AND m.manual_period=0",p,t);
        foreach(var m in moves)
        {
            var assigned=PeriodRules.Assign(periods,m.CardId,m.Date);
            if(assigned!=m.PeriodId) { await Audit(c,t,"movements",m.Id,"reassign",m); await c.ExecuteAsync("UPDATE movements SET period_id=@assigned,version=version+1,updated_at=UTC_TIMESTAMP(6) WHERE id=@Id",new {assigned,m.Id},t); }
        }
        return p.Id;
    });
    public Task<long> SaveMovementAsync(Movement m) => Write((c,t)=>SaveMovement(c,t,m));
    private static async Task<long> SaveMovement(MySqlConnection c,MySqlTransaction t,Movement m)
    {
        if(m.Amount<=0 || m.Amount>9999999999999999.99m || decimal.Round(m.Amount,2)!=m.Amount) throw new RuleException("El importe debe ser positivo y tener hasta dos decimales.");
        if(m.Currency is not ("ARS" or "USD")) throw new RuleException("Seleccioná ARS o USD.");
        if(m.Description.Length>200 || m.Notes.Length>4000) throw new RuleException("Descripción: máximo 200 caracteres. Observaciones: máximo 4.000.");
        if(m.Token.Length!=32 || !m.Token.All(Uri.IsHexDigit)) throw new RuleException("Identificador de movimiento inválido.");
        var before=m.Id>0?await c.QuerySingleOrDefaultAsync<Movement>(MovementSql+" AND m.id=@Id",m,t):null;
        if(m.Id>0 && (before==null || before.Version!=m.Version)) throw new RuleException("El movimiento cambió desde que lo abriste. Actualizá el historial y volvé a editarlo.");
        if(!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM cards WHERE id=@CardId AND (active=1 OR id=@oldCard))",new {m.CardId,oldCard=before?.CardId??0},t)) throw new RuleException("Seleccioná una tarjeta activa.");
        if(!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM categories WHERE id=@CategoryId AND (active=1 OR id=@oldCategory))",new {m.CategoryId,oldCategory=before?.CategoryId??0},t)) throw new RuleException("Seleccioná una categoría activa.");
        var periods=(await c.QueryAsync<Period>(PeriodSql+" WHERE card_id=@CardId",m,t)).ToList();
        if(m.ManualPeriod) { if(!periods.Any(p=>p.Id==m.PeriodId)) throw new RuleException("Seleccioná un período de esta tarjeta o usá asignación automática."); }
        else m.PeriodId=PeriodRules.Assign(periods,m.CardId,m.Date);
        if(m.OriginalId!=null)
        {
            var original=await c.QuerySingleOrDefaultAsync<Movement>(MovementSql+" AND m.id=@OriginalId",m,t);
            if(!m.Refund || original==null || original.Refund || original.CardId!=m.CardId || original.Currency!=m.Currency || original.Id==m.Id) throw new RuleException("El reintegro debe vincularse con un consumo de la misma tarjeta y moneda.");
        }
        if(before?.Refund==false && await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM movements WHERE original_id=@Id AND deleted_at IS NULL AND (@Refund=1 OR card_id<>@CardId OR currency<>@Currency)",m,t)>0)
            throw new RuleException("Hay reintegros vinculados: conservá el tipo, la tarjeta y la moneda del consumo original.");
        m.Date=m.Date.Date;
        if(m.Id==0)
        {
            var duplicate=await c.ExecuteScalarAsync<long?>("SELECT id FROM movements WHERE token=@Token",m,t);
            if(duplicate!=null) return duplicate.Value; // Exactly-once retry token, also prevents repeated Enter.
            return await c.ExecuteScalarAsync<long>("""
                INSERT INTO movements(token,card_id,period_id,manual_period,category_id,movement_date,amount,currency,refund,original_id,description,notes)
                VALUES(@Token,@CardId,@PeriodId,@ManualPeriod,@CategoryId,@Date,@Amount,@Currency,@Refund,@OriginalId,@Description,@Notes); SELECT LAST_INSERT_ID();
                """,m,t);
        }
        await Audit(c,t,"movements",m.Id,"edit",before);
        await c.ExecuteAsync("""
            UPDATE movements SET card_id=@CardId,period_id=@PeriodId,manual_period=@ManualPeriod,category_id=@CategoryId,movement_date=@Date,amount=@Amount,currency=@Currency,
            refund=@Refund,original_id=@OriginalId,description=@Description,notes=@Notes,version=version+1,updated_at=UTC_TIMESTAMP(6) WHERE id=@Id
            """,m,t);return m.Id;
    }
    public Task<int> DeleteMovementAsync(Movement m) => Write(async(c,t)=>
    {
        var before=await c.QuerySingleOrDefaultAsync<Movement>(MovementSql+" AND m.id=@Id",m,t);
        if(before==null || before.Version!=m.Version) throw new RuleException("El movimiento ya cambió. Actualizá el historial.");
        if(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM movements WHERE original_id=@Id AND deleted_at IS NULL",m,t)>0) throw new RuleException("Primero desvinculá o eliminá los reintegros asociados. El consumo original debe conservarse mientras tenga reintegros.");
        await Audit(c,t,"movements",m.Id,"delete",before);
        return await c.ExecuteAsync("UPDATE movements SET deleted_at=UTC_TIMESTAMP(6),version=version+1 WHERE id=@Id",m,t);
    });
    public Task<int> ImportAsync(IEnumerable<Movement> rows) => Write(async(c,t)=> { var count=0;foreach(var row in rows){await SaveMovement(c,t,row);count++;}return count; });
    public Task<bool> SettingAsync(string key,string value) => Write(async(c,t)=> {await c.ExecuteAsync("INSERT INTO settings(`key`,`value`) VALUES(@key,@value) ON DUPLICATE KEY UPDATE `value`=@value",new {key,value},t);return true;});
    public async Task MarkExportedAsync(long revision)
    {
        await using var c=await Open();await c.ExecuteAsync("UPDATE settings SET `value`=@revision WHERE `key`='export_revision'",new {revision=revision.ToString()});
    }
}
