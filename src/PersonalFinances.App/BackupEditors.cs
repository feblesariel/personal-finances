using PersonalFinances.Core;

namespace PersonalFinances.App;
public sealed class BackupExportEditor : EditorViewModel
{
    private readonly BackupService service;private string status="";
    public string Path {get;}
    public string Password {get;set;}="";
    public string Confirmation {get;set;}="";
    public string Status {get=>status;private set=>Set(ref status,value);}
    public BackupExportEditor(FinanceStore store,string path)
    {
        service=new(store,BackupUi.Version);Path=path;Title="Exportar todos los datos";SaveLabel="Exportar respaldo";
        Subtitle="Una copia completa, cifrada y portable de tu contabilidad.";
    }
    protected override async Task Save()
    {
        if(Password!=Confirmation)throw new RuleException("Las contraseñas no coinciden.");
        await service.ExportAsync(Path,Password,new Progress<string>(s=>Status=s));
        Password="";Confirmation="";Close();
    }
}
public sealed class BackupRestoreEditor : EditorViewModel
{
    private readonly BackupService service;private PreparedRestore? prepared;private string status="";private bool confirmed;
    public string Path {get;}
    public string Password {get;set;}="";
    public string Status {get=>status;private set=>Set(ref status,value);}
    public bool PreviewAvailable=>prepared!=null;
    public bool NeedsPassword=>prepared==null;
    public bool ConfirmReplacement {get=>confirmed;set=>Set(ref confirmed,value);}
    public RestoreResult? Result {get;private set;}
    public string SafetyPath=>prepared?.SafetyBackupPath??"";
    public string Preview
    {
        get
        {
            if(prepared==null)return "";var b=prepared.Incoming;var count=BackupCounts.From(b);var target=prepared.DestinationCounts;
            var dates=b.Movements.Select(m=>m.Date).ToArray();
            var range=dates.Length==0?"Sin movimientos":dates.Min().ToString("dd/MM/yyyy")+" al "+dates.Max().ToString("dd/MM/yyyy");
            return $"Creado: {b.CreatedAt.LocalDateTime:dd/MM/yyyy HH:mm}\nOrigen: {b.Application} {b.ApplicationVersion}\nFormato del respaldo: {b.BackupFormatVersion}\n\nCONTENIDO DEL RESPALDO\n{b.Services.Count} servicios · {b.ServiceObligations.Count} obligaciones · {b.ServicePayments.Count} pagos\n{count.Cards} tarjetas · {count.Categories} categorías\n{count.Periods} períodos · {count.Movements} movimientos\n{count.Deleted} movimientos eliminados conservados\nFechas: {range}\n\nBASE ACTUAL DE ESTE EQUIPO\n{target.Services} servicios · {target.ServiceObligations} obligaciones · {target.ServicePayments} pagos\n{target.Cards} tarjetas · {target.Categories} categorías\n{target.Periods} períodos · {target.Movements} movimientos\n{target.Deleted} movimientos eliminados conservados";
        }
    }
    public BackupRestoreEditor(FinanceStore store,string path)
    {
        service=new(store,BackupUi.Version);Path=path;Title="Importar respaldo";SaveLabel="Validar respaldo";
        Subtitle="Restaurá una copia completa. Primero verificaremos el archivo y te mostraremos su contenido.";
    }
    protected override async Task Save()
    {
        if(prepared==null)
        {
            prepared=await service.PrepareRestoreAsync(Path,Password,new Progress<string>(s=>Status=s));
            Password="";ConfirmReplacement=false;SaveLabel="Restaurar datos";
            foreach(var property in new[]{nameof(PreviewAvailable),nameof(NeedsPassword),nameof(Preview),nameof(SafetyPath),nameof(SaveLabel)})Changed(property);
            Status="Respaldo válido. Copia previa cifrada y verificada. Revisá los datos antes de confirmar.";return;
        }
        try{Result=await service.RestoreAsync(prepared,ConfirmReplacement,new Progress<string>(s=>Status=s));Close();}
        catch{Status="No se pudo completar o confirmar la restauración. No se aplican cambios parciales. La copia previa sigue disponible en la ubicación indicada.";throw;}
    }
}
internal static class BackupUi
{
    internal static string Version=>typeof(BackupUi).Assembly.GetName().Version?.ToString(3)??"1.4.0";
}
