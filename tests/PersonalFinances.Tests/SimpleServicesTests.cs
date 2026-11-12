using PersonalFinances.Core;
using Dapper;
using MySqlConnector;

internal static class SimpleServicesTests
{
    internal static async Task Run(FinanceStore store,string temp,Action<bool,string> check)
    {
        var month=new DateTime(2026,11,1);var original=(await store.CaptureBackupAsync("simple-test")).Document;
        await using(var migrationConnection=new MySqlConnection(store.Profile.ConnectionString))
        {
            await migrationConnection.OpenAsync();var revision=(await store.ReadAsync()).Revision;await migrationConnection.ExecuteAsync("DELETE FROM schema_migrations WHERE version=8");
            await store.InitializeAsync();var migratedRevision=(await store.ReadAsync()).Revision;await store.InitializeAsync();
            check(migratedRevision==revision+1&&(await store.ReadAsync()).Revision==migratedRevision,"Simple: migración invalida TXT una sola vez y es idempotente");
        }
        async Task<SimpleServiceEntry> Row(int id,DateTime? m=null)=>SimpleServices.Rows((await store.ReadAsync()).Services,m??month).Single(r=>r.ServiceId==id);
        async Task Save(SimpleServiceEntry row,decimal? paid,DateTime? date,string notes="")=>await store.SaveSimpleServiceAsync(new(row.Service,row.Month,paid,date,notes,row.Currency,row.Stamp));
        async Task Reject(Func<Task> action,string label){try{await action();}catch(RuleException){check(true,label);return;}throw new Exception("Accepted: "+label);}
        var id=await store.SaveSimpleServiceAsync(new(new(){Name="Planilla simple mensual"},month,null,null,"","ARS",""));var row=await Row(id);
        check(row is {Paid:null,PaymentDate:null,Status:"Pendiente"},"Simple: alta sin monto queda PENDIENTE");
        await Save(row,22554.99m,new(2026,11,11),"Factura");row=await Row(id);
        check(row is {Paid:22554.99m,PaymentDate:{Year:2026,Month:11,Day:11},Status:"Pagado",Notes:"Factura"},"Simple: monto y fecha quedan PAGADO");
        var stale=row;await Save(row,20000,new(2026,11,10),"Corregido");row=await Row(id);
        check(row.Paid==20000&&row.PaymentDate==new DateTime(2026,11,10),"Simple: corregir reemplaza monto y fecha, sin acumular");
        await Reject(()=>Save(stale,10,month),"Simple: versiones desactualizadas no pisan cambios");
        await Reject(()=>Save(row,-1,month),"Simple: rechaza monto negativo");await Reject(()=>Save(row,1,null),"Simple: monto informado requiere fecha");
        check(!(await store.ReadAsync()).Services.Payments.Any(p=>p.ObligationId==row.Current!.Id),"Simple: guardar no genera pagos sucesivos ni historial financiero nuevo");
        await Save(row,null,null);row=await Row(id);check(row.Status=="Pendiente"&&row.Paid==null&&row.PaymentDate==null,"Simple: limpiar monto y fecha vuelve a PENDIENTE");
        await Save(row,0,month);check((await Row(id)).Status=="Pagado","Simple: cero explícito con fecha se considera PAGADO, sin otro estado");
        var annual=await store.SaveSimpleServiceAsync(new(new(){Name="Anual simple Google",Frequency="Anual",Currency="USD"},month,19.99m,new(2026,6,2),"Renovación","USD",""));
        await store.CreateSimpleServiceMonthAsync(month.AddMonths(1),false);
        check((await Row(id,month.AddMonths(1))) is {Status:"Pendiente",Paid:null,PaymentDate:null,Notes:""},"Simple: nuevo mes limpia monto, fecha y notas de mensuales");
        check((await Row(annual,month.AddMonths(1))) is {Status:"Pagado",Paid:19.99m,PaymentDate:{Year:2026,Month:6,Day:2},Notes:"Renovación",Currency:"USD"},"Simple: nuevo mes conserva monto, fecha original y notas del anual");
        check((await store.ReadAsync()).Services.Obligations.Count(o=>o.ServiceId==annual)==1,"Simple: anual no duplica registros al crear meses");
        await Reject(()=>store.CreateSimpleServiceMonthAsync(month.AddMonths(1)),"Simple: evita períodos duplicados");
        check((await Row(annual,new(2028,7,1))).Paid==19.99m,"Simple: anual continúa abonado sin lógica de vencimiento ni aniversario");
        var annualDecember=await Row(annual,month.AddMonths(1));await Save(annualDecember,25,new(2026,12,5),"Nuevo dato");
        check((await Row(annual)).Paid==19.99m&&(await Row(annual,month.AddMonths(1))).Paid==25,"Simple: editar otro mes conserva el dato del mes anterior");
        var cleared=await Row(annual,month.AddMonths(1));await Save(cleared,null,null);
        check((await Row(annual,new(2027,1,1))).Status=="Pendiente","Simple: limpiar un anual no recupera un pago anterior por error");
        var legacy=new RecurringService{Name="Anual previo simple",Frequency="Anual",Currency="USD",Notes="Nota de la definición"};legacy.Id=await store.SaveServiceAsync(legacy);
        var o=new ServiceObligation{ServiceId=legacy.Id,Period=new(2026,6,1),Amount=100,Currency="USD",Notes="Nota previa"};o.Id=await store.SaveObligationAsync(o);await store.SaveServicePaymentAsync(new(){ObligationId=o.Id,Amount=20,Date=new(2026,6,2),Notes="Observación original"});
        var migrated=await Row(legacy.Id);check(migrated is {Status:"Pagado",Paid:20,PaymentDate:{Year:2026,Month:6,Day:2}}&&migrated.Notes.Contains("Observación original")&&migrated.Notes.Contains("Nota de la definición"),"Simple: pagos previos se conservan y se muestran sin parciales ni pérdida de notas");
        var isolatedLedger=new ServiceLedger([legacy],[o],(await store.ReadAsync()).Services.Payments.Where(p=>p.ObligationId==o.Id).ToList()){Months=[o.Period,month]};
        check(SimpleServices.VisibleMonths(isolatedLedger,month).SequenceEqual(new[]{month}),"Simple: un pago anual no introduce junio en el selector de trabajo");
        var legacyBefore=(await store.CaptureBackupAsync("simple-test")).Document;await Save(migrated,15,new(2026,6,3),"Editado");var legacyAfter=(await store.CaptureBackupAsync("simple-test")).Document;
        check(legacyBefore.ServicePayments.SequenceEqual(legacyAfter.ServicePayments)&&legacyAfter.ServiceObligations.Single(x=>x.Id==o.Id).Amount==100,"Simple: editar conserva pagos y cargos previos internamente");
        var changed=await Row(id);await store.SaveSimpleServiceAsync(new(changed.Service with{Name="Mensual renombrado"},month,12,month,"USD corregido","USD",changed.Stamp));check((await Row(id)) is {Name:"Mensual renombrado",Currency:"USD",Paid:12},"Simple: nombre y moneda se editan junto con el pago");
        var report=TextReport.Render(await store.ReadAsync(),month);var services=report[report.IndexOf("SERVICIOS -",StringComparison.Ordinal)..];
        check(services.Contains("PAGADO")&&services.Contains("PENDIENTE")&&services.Contains("US$ 12,00")&&!new[]{"Exigible","VIGENTE","Saldo:","PARCIAL","Vencimiento","SIN CARGO"}.Any(services.Contains),"Simple: TXT compacto sólo muestra monto, fecha y estado, separando monedas");
        var backup=new BackupService(store,"1.8.0-test");var path=Path.Combine(temp,"simple-service.pfinance");const string password="Servicios simples prueba 2026!";var exported=await backup.ExportAsync(path,password);
        check(exported.BackupFormatVersion==6&&exported.DatabaseSchemaVersion==8,"Simple: respaldo versionado v6 con esquema 8");
        var invalid=exported with{ServiceObligations=exported.ServiceObligations.Select(x=>x.Id==changed.Current!.Id?x with{SheetPaid=-1}:x).ToList()};await Reject(()=>Task.Run(()=>BackupValidation.Validate(invalid)),"Simple: rechaza datos simplificados inválidos antes de restaurar");
        var prepared=await backup.PrepareRestoreAsync(path,password);await backup.RestoreAsync(prepared,true);
        check((await Row(id)) is {Currency:"USD",Paid:12,Notes:"USD corregido"}&&(await Row(legacy.Id)).Paid==15,"Simple: respaldo y restauración recuperan exactamente la pantalla");
        var oldFormat=original with{BackupFormatVersion=5,DatabaseSchemaVersion=7};var oldPath=Path.Combine(temp,"legacy-v5-services.pfinance");await BackupFile.WriteVerifiedAsync(oldPath,oldFormat,password);var oldPrepared=await backup.PrepareRestoreAsync(oldPath,password);await backup.RestoreAsync(oldPrepared,true);
        check(BackupValidation.Fingerprint((await store.CaptureBackupAsync("simple-test")).Document)==BackupValidation.Fingerprint(original),"Simple: respaldo anterior v5 sigue restaurando sin inventar datos");
        check(original.Movements.SequenceEqual((await store.CaptureBackupAsync("simple-test")).Document.Movements),"Simple: consumos, dashboard y estadísticas de tarjetas intactos");
    }
}
