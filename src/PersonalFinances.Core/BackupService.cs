namespace PersonalFinances.Core;

public sealed class BackupService(FinanceStore store,string applicationVersion)
{
    public async Task<BackupDocument> ExportAsync(string path,string password,IProgress<string>? progress=null)
    {
        BackupFile.Password(password,true);
        progress?.Report("Obteniendo una copia consistente de MySQL…");
        var snapshot=await store.CaptureBackupAsync(applicationVersion,progress);
        progress?.Report("Cifrando y verificando el archivo…");
        try{await BackupFile.WriteVerifiedAsync(path,snapshot.Document,password);}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){LocalConfig.Log(e);throw new RuleException("No se pudo guardar y verificar el respaldo. Revisá el espacio libre y los permisos de la carpeta elegida. Se conservó cualquier archivo anterior.");}
        return snapshot.Document;
    }
    public async Task<PreparedRestore> PrepareRestoreAsync(string path,string password,IProgress<string>? progress=null)
    {
        progress?.Report("Comprobando contraseña, integridad y versión…");
        BackupDocument incoming;
        try{incoming=await BackupFile.ReadAsync(path,password);}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){LocalConfig.Log(e);throw new RuleException("No se pudo leer el respaldo. Comprobá que el archivo esté disponible y que tengas permiso para abrirlo.");}
        try{await Task.Run(()=>BackupValidation.Validate(incoming));}catch(System.Text.Json.JsonException){throw new RuleException("El respaldo contiene una auditoría dañada.");}
        progress?.Report("Preparando y verificando una copia de seguridad del destino…");
        var before=await store.CaptureBackupAsync(applicationVersion);
        var folder=Path.Combine(LocalConfig.Folder,"Backups");
        var safety=Path.Combine(folder,"Antes-de-restaurar-"+DateTime.Now.ToString("yyyy-MM-dd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]+".pfinance");
        try{Directory.CreateDirectory(folder);await BackupFile.WriteVerifiedAsync(safety,before.Document,password);}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){LocalConfig.Log(e);throw new RuleException("No se pudo guardar y verificar la copia de seguridad previa. Revisá espacio y permisos en la configuración local. No se modificó la base.");}
        return new(incoming,await Task.Run(()=>BackupValidation.Fingerprint(before.Document)),before.Revision,BackupCounts.From(before.Document),safety,await HashFile(safety));
    }
    public async Task<RestoreResult> RestoreAsync(PreparedRestore prepared,bool confirmed,IProgress<string>? progress=null)
    {
        if(!confirmed)throw new RuleException("Confirmá explícitamente el reemplazo de los datos antes de restaurar.");
        if(!File.Exists(prepared.SafetyBackupPath))throw new RuleException("No está disponible la copia de seguridad previa. Volvé a validar el respaldo antes de restaurar.");
        if(await HashFile(prepared.SafetyBackupPath)!=prepared.SafetyBackupHash)throw new RuleException("La copia de seguridad previa fue alterada. Volvé a validar el respaldo; no se modificaron los datos.");
        await store.RestoreBackupAsync(prepared,progress);
        progress?.Report("Actualizando el resumen en la carpeta de este equipo…");
        var exporter=new ExportService(store);var exported=await exporter.TryAsync(true);
        return new(prepared.SafetyBackupPath,exported,exporter.Status);
    }
    private static async Task<string> HashFile(string path)
    {
        await using var file=File.OpenRead(path);return Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(file));
    }
}
