using PersonalFinances.Core;

internal static class ExportFileNameTests
{
    internal static async Task Run(FinanceStore store,string temp,Action<bool,string> check)
    {
        var previous=LocalConfig.Preferences();var credentials=File.ReadAllBytes(LocalConfig.FilePath);var original=(await store.CaptureBackupAsync("filename-test")).Document;
        async Task Reject(Func<Task> action,string label){try{await action();}catch(RuleException){check(true,label);return;}throw new Exception("Accepted: "+label);}
        try
        {
            LocalConfig.SetExportDestination(temp,"Mi resumen mensual");var expected=Path.Combine(temp,"Mi resumen mensual.txt");
            check(LocalConfig.Preferences().ExportPath==expected&&LocalConfig.Preferences().ExportedRevision==null,"Archivo: nombre configurable agrega .txt e invalida el estado de exportación");
            check(ExportDestination.NormalizeFileName("  Mi resumen  ")=="Mi resumen.txt"&&ExportDestination.NormalizeFileName("Resumen.TXT")=="Resumen.TXT","Archivo: admite espacios y extensión TXT sin duplicarla");
            check(await new ExportService(store).TryAsync(true)&&File.ReadAllText(expected).Contains("SERVICIOS -"),"Archivo: exporta el resumen completo al nombre configurado");
            var originalPreferences=File.ReadAllBytes(LocalConfig.PreferencesPath);
            foreach(var invalid in new[]{"","../fuera.txt","sub\\resumen.txt","C:\\resumen.txt","invalido?.txt","CON.txt","LPT1.txt","COM¹.txt","archivo.csv",".txt","punto.txt.",new string('a',201)+".txt"})
                await Reject(()=>Task.Run(()=>LocalConfig.SetExportDestination(temp,invalid)),"Archivo: rechaza nombre inválido "+(invalid.Length>30?"demasiado largo":invalid));
            check(originalPreferences.SequenceEqual(File.ReadAllBytes(LocalConfig.PreferencesPath)),"Archivo: nombres inválidos conservan el destino y todas las preferencias");
            var nextFolder=Path.Combine(temp,"filename-folder");Directory.CreateDirectory(nextFolder);LocalConfig.SetExportFolder(nextFolder);
            check(LocalConfig.Preferences().ExportPath==Path.Combine(nextFolder,"Mi resumen mensual.txt"),"Archivo: cambiar carpeta conserva el nombre elegido");
            var backup=new BackupService(store,"filename-test");var backupPath=Path.Combine(temp,"filename-test.pfinance");const string password="Nombre de resumen prueba 2026!";await backup.ExportAsync(backupPath,password);var prepared=await backup.PrepareRestoreAsync(backupPath,password);await backup.RestoreAsync(prepared,true);
            check(LocalConfig.Preferences().ExportPath==Path.Combine(nextFolder,"Mi resumen mensual.txt")&&File.Exists(LocalConfig.Preferences().ExportPath),"Archivo: restaurar un respaldo conserva el nombre y carpeta del equipo destino");
            check(credentials.SequenceEqual(File.ReadAllBytes(LocalConfig.FilePath))&&BackupValidation.Fingerprint(original)==BackupValidation.Fingerprint((await store.CaptureBackupAsync("filename-test")).Document),"Archivo: configuración del TXT no altera credenciales ni datos financieros");
        }
        finally{LocalConfig.UpdatePreferences(_=>previous);}
    }
}
