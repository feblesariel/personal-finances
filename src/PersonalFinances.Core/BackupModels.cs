using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonalFinances.Core;

// IDs are stable inside LedgerId. Movement tokens survive transfers as well.
// A future merge must use these identities, never names or amounts as identities.
public sealed record BackupNamed
{
    public int Id { get; init; }
    public string? SeedKey { get; init; }
    public string Name { get; init; } = "";
    public bool Active { get; init; }
}
public sealed record BackupPeriod
{
    public int Id { get; init; }
    public int CardId { get; init; }
    public string Name { get; init; } = "";
    public DateTime Start { get; init; }
    public DateTime Closing { get; init; }
    public DateTime Due { get; init; }
}
public sealed record BackupMovement
{
    public long Id { get; init; }
    public string Token { get; init; } = "";
    public int CardId { get; init; }
    public int? PeriodId { get; init; }
    public bool ManualPeriod { get; init; }
    public int CategoryId { get; init; }
    public DateTime Date { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "";
    public bool Refund { get; init; }
    public long? OriginalId { get; init; }
    public string Description { get; init; } = "";
    public string Notes { get; init; } = "";
    public int Version { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? DeletedAt { get; init; }
}
public sealed record BackupAudit
{
    public long Id { get; init; }
    public string Entity { get; init; } = "";
    public long EntityId { get; init; }
    public string Action { get; init; } = "";
    public string? BeforeJson { get; init; }
    public DateTime ChangedAt { get; init; }
}
public sealed record BackupImportLine(int Line, long MovementId, string Token);
public sealed record BackupImportManifest(string Key, string Batch, string SourceHash, decimal Total,
    ImmutableArray<BackupImportLine> Lines, DateTimeOffset ImportedAt);
public sealed record BackupDocument
{
    [JsonRequired] public int BackupFormatVersion { get; init; } = 4;
    [JsonRequired] public int DatabaseSchemaVersion { get; init; } = 6;
    [JsonRequired] public string Application { get; init; } = "Personal Finances";
    [JsonRequired] public string ApplicationVersion { get; init; } = "";
    [JsonRequired] public Guid BackupId { get; init; } = Guid.NewGuid();
    [JsonRequired] public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    [JsonRequired] public string LedgerId { get; init; } = "";
    public List<DateTime> ServiceMonths {get;init;}=[];
    public List<RecurringService> Services {get;init;} = [];
    public List<ServiceObligation> ServiceObligations {get;init;} = [];
    public List<ServicePayment> ServicePayments {get;init;} = [];
    [JsonRequired] public ImmutableArray<BackupNamed> Cards { get; init; } = [];
    [JsonRequired] public ImmutableArray<BackupNamed> Categories { get; init; } = [];
    [JsonRequired] public ImmutableArray<BackupPeriod> Periods { get; init; } = [];
    [JsonRequired] public ImmutableArray<BackupMovement> Movements { get; init; } = [];
    [JsonRequired] public ImmutableArray<BackupAudit> Audit { get; init; } = [];
    [JsonRequired] public ImmutableArray<BackupImportManifest> Imports { get; init; } = [];
}
public sealed record BackupSnapshot(BackupDocument Document, long Revision);
public sealed record BackupCounts(int Cards, int Categories, int Periods, int Movements, int Deleted, int Services=0, int ServiceObligations=0, int ServicePayments=0)
{
    public static BackupCounts From(BackupDocument b) => new(b.Cards.Length,b.Categories.Length,b.Periods.Length,
        b.Movements.Count(m=>m.DeletedAt==null),b.Movements.Count(m=>m.DeletedAt!=null),b.Services.Count,b.ServiceObligations.Count,b.ServicePayments.Count);
}
public sealed record PreparedRestore(BackupDocument Incoming, string DestinationFingerprint, long DestinationRevision,
    BackupCounts DestinationCounts, string SafetyBackupPath, string SafetyBackupHash);
public sealed record RestoreResult(string SafetyBackupPath, bool SummaryExported, string ExportStatus);

public static class BackupValidation
{
    internal static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = false, MaxDepth = 40 };
    internal static void Require(bool ok, string reason)
    { if(!ok) throw new RuleException("El respaldo no es válido: "+reason); }

    public static void Validate(BackupDocument b)
    {
        if(b.BackupFormatVersion is not (1 or 2 or 3 or 4))throw new RuleException("Esta versión del respaldo no es compatible. Actualizá Personal Finances para abrirlo.");
        Require((b.BackupFormatVersion==1 && b.DatabaseSchemaVersion==3 || b.BackupFormatVersion==2 && b.DatabaseSchemaVersion==4 || b.BackupFormatVersion==3 && b.DatabaseSchemaVersion==5 || b.BackupFormatVersion==4 && b.DatabaseSchemaVersion==6),"versión de base de datos incompatible.");
        Require(b.Application=="Personal Finances"&&!string.IsNullOrWhiteSpace(b.ApplicationVersion)&&b.ApplicationVersion.Length<=160,"aplicación de origen desconocida.");
        Require(b.BackupId!=Guid.Empty&&b.CreatedAt.Year>=2020&&Guid.TryParseExact(b.LedgerId,"N",out _),"identificación incompleta.");
        Require(!b.Cards.IsDefault&&!b.Categories.IsDefault&&!b.Periods.IsDefault&&!b.Movements.IsDefault&&!b.Audit.IsDefault&&!b.Imports.IsDefault,"faltan colecciones.");
        if(b.ServiceMonths==null)throw new RuleException("Faltan meses de servicios.");
        Require(Months(b).All(d=>Calendar(d)&&d.Day==1)&&Months(b).Distinct().Count()==Months(b).Count,"meses de servicios inválidos.");
        Require(b.BackupFormatVersion<4||b.ServiceObligations.All(o=>o!=null&&b.ServiceMonths.Contains(o.Period)),"faltan meses de servicios.");
        ValidateServices(b);
        Named(b.Cards);Named(b.Categories);
        var cards=b.Cards.Select(x=>x.Id).ToHashSet();var cats=b.Categories.Select(x=>x.Id).ToHashSet();
        Require(b.Periods.All(p=>p!=null&&p.Id>0)&&b.Periods.Select(p=>p.Id).Distinct().Count()==b.Periods.Length,"períodos duplicados o inválidos.");
        foreach(var p in b.Periods)
        {
            Require(cards.Contains(p.CardId)&&Text(p.Name,100)&&Calendar(p.Start)&&Calendar(p.Closing)&&Calendar(p.Due)&&p.Start<=p.Closing&&p.Closing<=p.Due,"fechas o tarjeta de un período inválidas.");
        }
        foreach(var g in b.Periods.GroupBy(p=>p.CardId))
        { var rows=g.OrderBy(p=>p.Start).ToArray();for(var i=1;i<rows.Length;i++)Require(rows[i].Start>rows[i-1].Closing,"hay períodos superpuestos."); }
        Require(b.Movements.All(m=>m!=null&&m.Id>0)&&b.Movements.Select(m=>m.Id).Distinct().Count()==b.Movements.Length,"movimientos duplicados o inválidos.");
        Require(b.Movements.Select(m=>m.Token).Distinct(StringComparer.OrdinalIgnoreCase).Count()==b.Movements.Length,"identificadores de movimiento duplicados.");
        var periods=b.Periods.ToDictionary(p=>p.Id);var moves=b.Movements.ToDictionary(m=>m.Id);
        foreach(var m in b.Movements)
        {
            Require(Guid.TryParseExact(m.Token,"N",out _)&&cards.Contains(m.CardId)&&cats.Contains(m.CategoryId),"relaciones de un movimiento incompletas.");
            Require(m.PeriodId==null||periods.TryGetValue(m.PeriodId.Value,out var p)&&p.CardId==m.CardId,"período de otra tarjeta.");
            Require(!m.ManualPeriod||m.PeriodId!=null,"asignación manual sin período.");
            Require(Calendar(m.Date)&&m.Amount>0&&m.Amount<=9999999999999999.99m&&decimal.Round(m.Amount,2)==m.Amount&&m.Currency is "ARS" or "USD","fecha, importe o moneda inválidos.");
            Require(m.Description!=null&&m.Description.Length<=200&&m.Notes!=null&&System.Text.Encoding.UTF8.GetByteCount(m.Notes)<=65535&&m.Version>0&&Timestamp(m.CreatedAt)&&Timestamp(m.UpdatedAt)&&(m.DeletedAt==null||Timestamp(m.DeletedAt.Value)),"texto o metadatos de movimiento inválidos.");
            if(m.OriginalId is {} id)Require(m.Refund&&id!=m.Id&&moves.TryGetValue(id,out var original)&&!original.Refund&&original.CardId==m.CardId&&original.Currency==m.Currency,"reintegro vinculado incorrectamente.");
        }
        Require(b.Audit.All(a=>a!=null&&a.Id>0)&&b.Audit.Select(a=>a.Id).Distinct().Count()==b.Audit.Length,"auditoría duplicada o inválida.");
        foreach(var a in b.Audit)
        {
            Require(a.Entity is "cards" or "categories" or "periods" or "movements" or "recurring_services" or "service_obligations" or "service_payments"&&a.EntityId>0&&Text(a.Action,20)&&Timestamp(a.ChangedAt),"auditoría inválida.");
            if(a.BeforeJson!=null){using var json=JsonDocument.Parse(a.BeforeJson);Require(json.RootElement.ValueKind==JsonValueKind.Object,"auditoría ilegible.");}
        }
        Require(b.Imports.All(i=>i!=null)&&b.Imports.Select(i=>i.Key).Distinct().Count()==b.Imports.Length,"lotes de importación duplicados.");
        foreach(var i in b.Imports)
        {
            Require(i.Key!=null&&i.Key.StartsWith("import:",StringComparison.Ordinal)&&i.Key.Length<=80&&Text(i.Batch,160)&&Text(i.SourceHash,128)&&!i.Lines.IsDefault&&i.ImportedAt.Year>=1000,"lote de importación inválido.");
            foreach(var l in i.Lines)Require(l!=null&&l.Line>0&&moves.TryGetValue(l.MovementId,out var m)&&m.Token==l.Token,"lote con movimientos inexistentes.");
        }
    }
    private static void ValidateServices(BackupDocument b)
    {
        if(b.Services==null||b.ServiceObligations==null||b.ServicePayments==null)throw new RuleException("Faltan colecciones de servicios.");
        Require(b.BackupFormatVersion!=1||(b.Services!.Count+b.ServiceObligations!.Count+b.ServicePayments!.Count==0),"servicios en formato antiguo.");
        Require(b.Services!.All(x=>x!=null&&x.Id>0)&&b.Services.Select(x=>x.Id).Distinct().Count()==b.Services.Count,"servicios duplicados.");
        Require(b.ServiceObligations!.All(x=>x!=null&&x.Id>0)&&b.ServiceObligations.Select(x=>x.Id).Distinct().Count()==b.ServiceObligations.Count,"obligaciones duplicadas.");
        Require(b.ServicePayments!.All(x=>x!=null&&x.Id>0)&&b.ServicePayments.Select(x=>x.Id).Distinct().Count()==b.ServicePayments.Count,"pagos duplicados.");
        Require(b.ServicePayments.Select(x=>x.Token).Distinct().Count()==b.ServicePayments.Count,"tokens de pagos duplicados.");
        var services=b.Services.ToDictionary(x=>x.Id);var obligations=b.ServiceObligations.ToDictionary(x=>x.Id);
        foreach(var s in b.Services){ServiceRules.Validate(s);Require(s.CardId==null||b.Cards.Any(c=>c.Id==s.CardId),"tarjeta de servicio inexistente.");}
        foreach(var p in b.ServicePayments)Require(obligations.ContainsKey(p.ObligationId)&&Guid.TryParseExact(p.Token,"N",out _)&&ServiceRules.DateValid(p.Date)&&ServiceRules.MoneyValid(p.Amount)&&p.Amount>0&&ServiceRules.Methods.Contains(p.Method)&&p.Notes!=null&&p.Notes.Length<=4000&&p.Version>0,"pago de servicio inválido.");
        foreach(var o in b.ServiceObligations){Require(services.ContainsKey(o.ServiceId),"servicio inexistente.");ServiceRules.Validate(o,services[o.ServiceId],b.ServicePayments.Where(p=>p.ObligationId==o.Id&&!p.Voided).Sum(p=>p.Amount));}
        Require(b.ServiceObligations.Select(o=>(o.ServiceId,Key:o.Period.Year*100+o.Period.Month,Currency:services[o.ServiceId].Type=="Tarjeta de crédito"?o.Currency:"single")).Distinct().Count()==b.ServiceObligations.Count,"períodos de servicios duplicados.");
    }
    public static List<DateTime> Months(BackupDocument b)=>b.BackupFormatVersion<4?b.ServiceObligations.Select(o=>o.Period).Distinct().Order().ToList():b.ServiceMonths;
    public static List<ServiceObligation> Obligations(BackupDocument b)=>b.ServiceObligations.Select(o=>b.BackupFormatVersion<4&&o.Amount==0?o with{AmountConfirmed=false}:o).ToList();
    private static bool Text(string? s,int max)=>!string.IsNullOrWhiteSpace(s)&&s.Length<=max;
    private static bool Calendar(DateTime d)=>d.Year>=1000&&d.TimeOfDay==TimeSpan.Zero;
    private static bool Timestamp(DateTime d)=>d.Year>=1000&&d.Ticks%10==0;
    private static void Named(ImmutableArray<BackupNamed> rows)
    {
        Require(rows.All(x=>x!=null&&x.Id>0&&Text(x.Name,80)&&(x.SeedKey==null||Text(x.SeedKey,40)))&&rows.Select(x=>x.Id).Distinct().Count()==rows.Length,"tarjetas o categorías inválidas.");
        Require(rows.Select(x=>x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()==rows.Length&&rows.Where(x=>x.SeedKey!=null).Select(x=>x.SeedKey).Distinct().Count()==rows.Count(x=>x.SeedKey!=null),"nombres o claves iniciales duplicados.");
    }
    // MySQL normalizes JSON whitespace/key order. Canonicalize before comparing snapshots.
    internal static string CanonicalJson(string json)
    {
        using var doc=JsonDocument.Parse(json);using var bytes=new MemoryStream();using(var w=new Utf8JsonWriter(bytes))Write(w,doc.RootElement);
        return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
    }
    private static void Write(Utf8JsonWriter w,JsonElement e)
    {
        switch(e.ValueKind)
        {
            case JsonValueKind.Object:w.WriteStartObject();foreach(var p in e.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal)){w.WritePropertyName(p.Name);Write(w,p.Value);}w.WriteEndObject();break;
            case JsonValueKind.Array:w.WriteStartArray();foreach(var x in e.EnumerateArray())Write(w,x);w.WriteEndArray();break;
            case JsonValueKind.Number:if(e.TryGetDecimal(out var n))w.WriteRawValue(n.ToString("G29",System.Globalization.CultureInfo.InvariantCulture));else e.WriteTo(w);break;
            default:e.WriteTo(w);break;
        }
    }
    public static string Fingerprint(BackupDocument b)
    {
        var data=new {b.LedgerId,Services=b.Services.OrderBy(x=>x.Id),ServiceMonths=Months(b).Order(),ServiceObligations=Obligations(b).OrderBy(x=>x.Id),ServicePayments=b.ServicePayments.OrderBy(x=>x.Id),Cards=b.Cards.OrderBy(x=>x.Id),Categories=b.Categories.OrderBy(x=>x.Id),Periods=b.Periods.OrderBy(x=>x.Id),Movements=b.Movements.OrderBy(x=>x.Id),Audit=b.Audit.OrderBy(x=>x.Id).Select(a=>a with{BeforeJson=a.BeforeJson==null?null:CanonicalJson(a.BeforeJson)}),Imports=b.Imports.OrderBy(x=>x.Key).Select(i=>i with{Lines=i.Lines.OrderBy(l=>l.Line).ToImmutableArray()})};
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(CanonicalJson(JsonSerializer.Serialize(data,Json)))));
    }
}
