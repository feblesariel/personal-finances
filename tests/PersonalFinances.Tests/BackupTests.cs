using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using PersonalFinances.Core;

internal static class BackupTests
{
    private const string Password="Una contraseña de prueba 2026!";
    internal static async Task Run(MySqlConnection root,ConnectionProfile admin,string temp,Action<bool,string> check)
    {
        var suffix=DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var sourceDb="personal_finances_tests_backup_source_"+suffix;var targetDb="personal_finances_tests_backup_target_"+suffix;
        foreach(var name in new[]{sourceDb,targetDb})await root.ExecuteAsync($"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci");
        try
        {
            var source=new FinanceStore(admin with{Database=sourceDb});var target=new FinanceStore(admin with{Database=targetDb});
            await source.InitializeAsync();await target.InitializeAsync();var s=await source.ReadAsync();var visa=s.Cards[0].Id;var master=s.Cards[1].Id;var cat=s.Categories[0].Id;
            var extra=await source.SaveCardAsync(new(){Name="AMEX inactiva"});var extraCat=await source.SaveCategoryAsync(new(){Name="Categoría archivada"});
            await source.SaveCardAsync(new(){Id=extra,Name="AMEX inactiva",Active=false});await source.SaveCategoryAsync(new(){Id=extraCat,Name="Categoría archivada",Active=false});
            var past=new Period{CardId=visa,Name="Octubre 2026",Start=new(2026,9,21),Closing=new(2026,10,20),Due=new(2026,11,1)};past.Id=await source.SavePeriodAsync(past);
            var current=new Period{CardId=visa,Name="Noviembre 2026",Start=new(2026,10,21),Closing=new(2026,11,20),Due=new(2026,12,1)};current.Id=await source.SavePeriodAsync(current);
            await source.SavePeriodAsync(new(){CardId=master,Name="Master noviembre",Start=current.Start,Closing=current.Closing,Due=current.Due});
            var original=new Movement{CardId=visa,CategoryId=cat,Date=new(2026,11,1),Amount=10000.10m,Currency="ARS",Description="COTO transferencia",Notes="Restan 12.000,00 · áéíóú"};original.Id=await source.SaveMovementAsync(original);
            await source.SaveMovementAsync(new(){CardId=visa,CategoryId=cat,Date=new(2026,11,2),Amount=99.99m,Currency="ARS",Refund=true,OriginalId=original.Id,Description="Devolución real"});
            await source.SaveMovementAsync(new(){CardId=visa,CategoryId=cat,Date=new(2026,11,3),Amount=32.50m,Currency="USD",Notes="REFUND pendiente: no descontar"});
            await source.SaveMovementAsync(new(){CardId=master,CategoryId=cat,Date=new(2026,11,4),Amount=12.99m,Currency="USD"});
            await source.SaveMovementAsync(new(){CardId=visa,CategoryId=cat,Date=new(2026,10,1),Amount=777.77m,Currency="ARS"});
            await source.SaveMovementAsync(new(){CardId=visa,CategoryId=cat,Date=new(2026,8,1),Amount=1.23m,Currency="ARS",ManualPeriod=true,PeriodId=past.Id,Notes="Asignación histórica manual"});
            var deleted=new Movement{CardId=master,CategoryId=cat,Date=new(2026,11,4),Amount=999m,Currency="ARS",Notes="Eliminado con auditoría"};deleted.Id=await source.SaveMovementAsync(deleted);await source.DeleteMovementAsync((await source.ReadAsync()).Movements.Single(m=>m.Id==deleted.Id));
            original=(await source.ReadAsync()).Movements.Single(m=>m.Id==original.Id);await source.SaveMovementAsync(original);
            await source.SettingAsync("import:test",JsonSerializer.Serialize(new{Batch="transferencia-prueba",SourceHash=new string('a',64),Total=10000.10m,Lines=new[]{new BackupImportLine(1,original.Id,original.Token)},ImportedAt=DateTimeOffset.Now}));
            await source.SettingAsync("export_path",@"Q:\ORIGEN-NO-EXPORTAR\RESUMEN_TARJETAS.txt");await source.SettingAsync("private_sentinel","SECRET-NO-EXPORTAR");
            var svc=new RecurringService{Name="Servicio respaldo",Type="Tarjeta de crédito",CardId=visa,UsualAmount=1000,UsualAmountUsd=20};svc.Id=await source.SaveServiceAsync(svc);
            var obligation=new ServiceObligation{ServiceId=svc.Id,Period=new(2026,11,1),Due=new(2026,11,30),Amount=1000,Minimum=100,Installment=5,Installments=24,Notes="Cuota histórica"};obligation.Id=await source.SaveObligationAsync(obligation);
            await source.SaveServicePaymentAsync(new(){ObligationId=obligation.Id,Amount=100,Notes="Mínimo",Method="VISA"});
            await source.SaveServicePaymentAsync(new(){ObligationId=obligation.Id,Amount=50});
            await source.SaveObligationAsync(obligation with{Id=0,Currency="USD",Amount=20,Minimum=null,Due=null});
            var sourceService=new BackupService(source,"1.4.0-test");var targetService=new BackupService(target,"1.4.0-test");
            var path=Path.Combine(temp,"transferencia.pfinance");var exported=await sourceService.ExportAsync(path,Password);
            var decoded=await BackupFile.ReadAsync(path,Password);var fingerprint=BackupValidation.Fingerprint(exported);
            check(fingerprint==BackupValidation.Fingerprint(decoded)&&decoded.Cards.Length==3&&decoded.Periods.Length==3,"Respaldo cifrado de múltiples tarjetas y períodos, incluidos inactivos");
            check(decoded.Movements.Length==7&&decoded.Movements.Count(m=>m.DeletedAt!=null)==1&&decoded.Audit.Length>=4,"Exportación completa: eliminaciones lógicas y auditoría contable");
            var json=JsonSerializer.Serialize(decoded);check(!json.Contains("ORIGEN-NO-EXPORTAR")&&!json.Contains("SECRET-NO-EXPORTAR")&&!json.Contains(admin.Password)&&!json.Contains("ConnectionString"),"Respaldo excluye credenciales, rutas y ajustes locales");
            var raw=await File.ReadAllBytesAsync(path);check(!Encoding.UTF8.GetString(raw).Contains("COTO transferencia")&&!Encoding.UTF8.GetString(raw).Contains(Password),"Archivo sin datos financieros ni contraseña en texto plano");
            var legacy=decoded with{BackupFormatVersion=1,DatabaseSchemaVersion=3,Services=[],ServiceObligations=[],ServicePayments=[]};
            BackupValidation.Validate(legacy);check(legacy.Services.Count==0,"Servicios: respaldo v1 compatible con colecciones vacías");
            var initial=await target.CaptureBackupAsync("test");var initialHash=BackupValidation.Fingerprint(initial.Document);
            async Task Reject(Func<Task> action,string label)
            {try{await action();}catch(RuleException){check(true,label);return;}throw new Exception("FAIL accepted: "+label);}
            await Reject(()=>targetService.PrepareRestoreAsync(path,"contraseña incorrecta"),"Contraseña incorrecta rechazada antes de tocar el destino");
            var corrupt=Path.Combine(temp,"corrupto.pfinance");var altered=raw.ToArray();altered[^25]^=0x50;await File.WriteAllBytesAsync(corrupt,altered);
            await Reject(()=>targetService.PrepareRestoreAsync(corrupt,Password),"Cifrado autenticado detecta archivo alterado");
            altered=raw[..30];await File.WriteAllBytesAsync(corrupt,altered);await Reject(()=>targetService.PrepareRestoreAsync(corrupt,Password),"Respaldo truncado rechazado");
            altered=raw.ToArray();altered[8]=9;await File.WriteAllBytesAsync(corrupt,altered);await Reject(()=>targetService.PrepareRestoreAsync(corrupt,Password),"Versión futura de contenedor rechazada");
            await BackupFile.WriteVerifiedAsync(corrupt,exported with{BackupFormatVersion=9},Password);await Reject(()=>targetService.PrepareRestoreAsync(corrupt,Password),"Versión futura del contenido rechazada antes de restaurar");
            var invalid=exported with{Movements=exported.Movements.SetItem(0,exported.Movements[0] with{CategoryId=int.MaxValue})};await BackupFile.WriteVerifiedAsync(corrupt,invalid,Password);
            await Reject(()=>targetService.PrepareRestoreAsync(corrupt,Password),"Relación inválida detectada antes de generar cambios");
            check(initialHash==BackupValidation.Fingerprint((await target.CaptureBackupAsync("test")).Document),"Fallos de validación conservan íntegro el destino");
            await Reject(()=>sourceService.ExportAsync(path,"corta"),"Contraseña de exportación exige al menos 12 caracteres");
            var unchangedFile=await File.ReadAllBytesAsync(path);check(raw.SequenceEqual(unchangedFile),"Exportación fallida conserva el respaldo anterior");
            var targetFolder=Path.Combine(temp,"destino-resumen");Directory.CreateDirectory(targetFolder);LocalConfig.SetExportFolder(targetFolder);
            var profileBytes=File.ReadAllBytes(LocalConfig.FilePath);var localPath=LocalConfig.Preferences().ExportPath;
            await target.SettingAsync("export_path",@"R:\DESTINO-LOCAL\RESUMEN_TARJETAS.txt");await target.SettingAsync("private_sentinel","CONFIG-DESTINO");
            var prepared=await targetService.PrepareRestoreAsync(path,Password);
            check(prepared.DestinationCounts.Movements==0&&prepared.DestinationCounts.Cards==2,"Vista previa distingue semillas de instalación y movimientos del destino");
            check(BackupValidation.Fingerprint(await BackupFile.ReadAsync(prepared.SafetyBackupPath,Password))==prepared.DestinationFingerprint,"Copia de seguridad previa cifrada, descifrada y verificada");
            await Reject(()=>targetService.RestoreAsync(prepared,false),"Sin confirmación explícita no se restaura");
            var result=await targetService.RestoreAsync(prepared,true);var restored=await target.CaptureBackupAsync("test");
            check(restored.Document.Services.Count==1&&restored.Document.ServiceObligations.Count==2&&restored.Document.ServiceObligations[0].Installment==5&&restored.Document.Services[0].UsualAmountUsd==20&&restored.Document.ServiceObligations.Single(o=>o.Currency=="USD").Due==null&&restored.Document.ServicePayments.Count==2&&restored.Document.ServicePayments.Sum(p=>p.Amount)==150,"Servicios: respaldo y restauración entre bases conserva IDs, cuotas, mínimo y todos los pagos");
            check(fingerprint==BackupValidation.Fingerprint(restored.Document),"Importación en base nueva reconstruye exactamente todas las entidades y relaciones");
            check(restored.Document.Movements.Where(m=>m.DeletedAt==null&&m.Currency=="ARS").Sum(m=>m.Refund?-m.Amount:m.Amount)==10679.11m&&restored.Document.Movements.Where(m=>m.DeletedAt==null&&m.Currency=="USD").Sum(m=>m.Amount)==45.49m,"Totales exactos antes/después, ARS/USD y reintegros separados");
            check(restored.Document.Movements.Single(m=>m.Id==original.Id).Notes==original.Notes&&restored.Document.Movements.Any(m=>m.ManualPeriod&&m.Date<new DateTime(2026,9,1)),"Observaciones, fechas e imputaciones históricas manuales preservadas");
            check(restored.Document.Imports.Length==1&&restored.Document.Movements.Single(m=>m.Id==deleted.Id).DeletedAt!=null,"Manifiestos de importación, tokens y registros eliminados preservados");
            string Totals(BackupDocument b)=>JsonSerializer.Serialize(b.Movements.Where(m=>m.DeletedAt==null).GroupBy(m=>(m.CardId,m.PeriodId,m.Currency)).OrderBy(g=>g.Key.CardId).ThenBy(g=>g.Key.PeriodId).ThenBy(g=>g.Key.Currency).Select(g=>new{g.Key.CardId,g.Key.PeriodId,g.Key.Currency,Count=g.Count(),Net=g.Sum(m=>m.Refund?-m.Amount:m.Amount)}));
            check(Totals(exported)==Totals(restored.Document),"Totales y cantidades exactos por tarjeta, período y moneda tras restaurar");
            var txt=await File.ReadAllTextAsync(Path.Combine(targetFolder,FinanceStore.ExportFileName));check(result.SummaryExported&&txt.Contains("$ 9.900,11")&&txt.Contains("US$ 32,50")&&!txt.Contains("COTO transferencia"),"TXT simplificado regenerado automáticamente desde la base restaurada");
            check(LocalConfig.Preferences().ExportPath==localPath&&profileBytes.SequenceEqual(File.ReadAllBytes(LocalConfig.FilePath)),"Restauración conserva carpeta y credenciales DPAPI del equipo destino");
            var targetSettings=(await target.ReadAsync()).Settings;check(targetSettings["private_sentinel"]=="CONFIG-DESTINO"&&targetSettings["export_path"].Contains("DESTINO-LOCAL"),"Ajustes locales legados del destino no se reemplazan por los del origen");
            var extraTarget=new Movement{CardId=visa,CategoryId=cat,Date=new(2026,11,5),Amount=123.45m,Currency="ARS",Description="Solo destino"};await target.SaveMovementAsync(extraTarget);
            var previous=await target.CaptureBackupAsync("test");prepared=await targetService.PrepareRestoreAsync(path,Password);await targetService.RestoreAsync(prepared,true);
            check(BackupValidation.Fingerprint((await target.CaptureBackupAsync("test")).Document)==fingerprint&&BackupValidation.Fingerprint(await BackupFile.ReadAsync(prepared.SafetyBackupPath,Password))==BackupValidation.Fingerprint(previous.Document),"Destino con datos: reemplazo completo y copia recuperable de su estado anterior");
            prepared=await targetService.PrepareRestoreAsync(path,Password);await targetService.RestoreAsync(prepared,true);check((await target.CaptureBackupAsync("test")).Document.Cards.Length==3&&(await target.ReadAsync()).Movements.Count==6,"Restauración repetida no duplica tarjetas, categorías ni movimientos");
            prepared=await targetService.PrepareRestoreAsync(path,Password);await target.SaveMovementAsync(new(){CardId=visa,CategoryId=cat,Date=new(2026,11,5),Amount=1.11m,Currency="ARS"});
            var changedHash=BackupValidation.Fingerprint((await target.CaptureBackupAsync("test")).Document);await Reject(()=>targetService.RestoreAsync(prepared,true),"Cambio en destino desde vista previa requiere nueva revisión");
            check(changedHash==BackupValidation.Fingerprint((await target.CaptureBackupAsync("test")).Document),"Rechazo por concurrencia no altera el estado más reciente");
            prepared=await targetService.PrepareRestoreAsync(path,Password);
            await using(var connection=new MySqlConnection(target.Profile.ConnectionString))
            {
                await connection.OpenAsync();await connection.ExecuteAsync("CREATE TRIGGER fail_backup_test BEFORE INSERT ON categories FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Intentional isolated restore failure'");
                try{await targetService.RestoreAsync(prepared,true);throw new Exception("Expected restore failure");}catch(MySqlException e)when(e.SqlState=="45000"){check(true,"Fallo SQL inyectado después de eliminar filas dentro de la transacción");}
                finally{await connection.ExecuteAsync("DROP TRIGGER fail_backup_test");}
            }
            check(changedHash==BackupValidation.Fingerprint((await target.CaptureBackupAsync("test")).Document)&&File.Exists(prepared.SafetyBackupPath),"Rollback integral ante fallo de restauración; copia previa conservada");
            var safety=await File.ReadAllBytesAsync(prepared.SafetyBackupPath);safety[^1]^=1;await File.WriteAllBytesAsync(prepared.SafetyBackupPath,safety);
            await Reject(()=>targetService.RestoreAsync(prepared,true),"Copia previa alterada impide cualquier reemplazo de datos");
            LocalConfig.SetExportFolder(Path.Combine(temp,"unidad-no-disponible"));prepared=await targetService.PrepareRestoreAsync(path,Password);result=await targetService.RestoreAsync(prepared,true);
            check(!result.SummaryExported&&fingerprint==BackupValidation.Fingerprint((await target.CaptureBackupAsync("test")).Document),"Drive no disponible: restauración válida confirmada y exportación pendiente");
            LocalConfig.SetExportFolder(targetFolder);check(await new ExportService(target).TryAsync(),"Resumen pendiente se recupera al restablecer el destino");
            var recover=await targetService.PrepareRestoreAsync(prepared.SafetyBackupPath,Password);await targetService.RestoreAsync(recover,true);
            check(BackupValidation.Fingerprint((await target.CaptureBackupAsync("test")).Document)==changedHash,"Recuperación completa del estado anterior usando la copia automática");
            var called=false;Task? concurrentWrite=null;
            var snapshot=await source.CaptureBackupAsync("test",new ImmediateProgress(message=>
            {
                if(!called&&message.StartsWith("Leyendo tarjetas")){called=true;concurrentWrite=source.SaveCardAsync(new(){Name="Creada durante respaldo"});concurrentWrite.GetAwaiter().GetResult();}
            }));
            check(called&&snapshot.Document.Cards.Length==3&&(await source.ReadAsync()).Cards.Count==4,"Vista MVCC consistente mientras otra conexión modifica la base");
            var oldPath=Path.Combine(temp,"legacy.pfinance");
            await BackupFile.WriteVerifiedAsync(oldPath,legacy,Password);
            var oldPrepared=await targetService.PrepareRestoreAsync(oldPath,Password);await targetService.RestoreAsync(oldPrepared,true);
            var oldRestored=(await target.CaptureBackupAsync("test")).Document;
            check(oldRestored.Services.Count==0&&oldRestored.ServicePayments.Count==0&&oldRestored.Movements.SequenceEqual(legacy.Movements),"Servicios: restauración efectiva de v1 conserva consumos y deja servicios vacíos");
            var previousFormat=decoded with{BackupFormatVersion=2,DatabaseSchemaVersion=4,Services=decoded.Services.Select(s=>s with{UsualAmountUsd=null}).ToList(),ServiceObligations=decoded.ServiceObligations.Where(o=>o.Currency=="ARS").ToList()};
            var v2Path=Path.Combine(temp,"v2.pfinance");await BackupFile.WriteVerifiedAsync(v2Path,previousFormat,Password);
            var v2Prepared=await targetService.PrepareRestoreAsync(v2Path,Password);await targetService.RestoreAsync(v2Prepared,true);
            check((await target.ReadAsync()).Services.Obligations.Count==1&&(await target.ReadAsync()).Services.Payments.Count==2,"Servicios: respaldo v2 anterior conserva sus servicios y pagos al restaurar");
            var unknown="unrecognized_business_data";await using(var c=new MySqlConnection(source.Profile.ConnectionString)){await c.OpenAsync();await c.ExecuteAsync($"CREATE TABLE {unknown}(id INT) ENGINE=InnoDB");}
            await Reject(()=>sourceService.ExportAsync(Path.Combine(temp,"unknown.pfinance"),Password),"Esquema desconocido bloquea respaldo parcial en vez de omitir entidades");
            check(!Directory.GetFiles(temp,".pfinance-*.tmp",SearchOption.AllDirectories).Any(),"Escritura segura sin archivos temporales residuales");
        }
        finally
        {
            MySqlConnection.ClearAllPools();foreach(var name in new[]{sourceDb,targetDb})
            {if(!name.StartsWith("personal_finances_tests_backup_")||!name.EndsWith(suffix))throw new Exception("Unsafe cleanup");await root.ExecuteAsync($"DROP DATABASE `{name}`");}
        }
    }
    private sealed class ImmediateProgress(Action<string> action):IProgress<string>{public void Report(string value)=>action(value);}
}
