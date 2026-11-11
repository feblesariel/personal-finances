using PersonalFinances.Core;
using Dapper;
using MySqlConnector;
using System.Security.Cryptography;
internal static class ServiceOperationTests
{
    internal static async Task Run(FinanceStore store,string temp,Action<bool,string> check)
    {
        async Task Reject(Func<Task> action,string label){try{await action();}catch(RuleException){check(true,label);return;}throw new Exception("Accepted: "+label);}
        var svc=new RecurringService{Name="Modal pagos prueba"};svc.Id=await store.SaveServiceAsync(svc);
        await store.PrepareServiceMonthAsync(DateTime.Today);
        async Task<ServiceSheetEntry> Entry(int id,DateTime? month=null,DateTime? now=null)=>ServiceSheet.Rows((await store.ReadAsync()).Services,month??DateTime.Today,now??DateTime.Today).Single(r=>r.ServiceId==id);
        var entry=await Entry(svc.Id);
        await using var c=new MySqlConnection(store.Profile.ConnectionString);await c.OpenAsync();
        check(await c.ExecuteScalarAsync<bool>("SELECT amount IS NULL FROM service_obligations WHERE id=@id",new{id=entry.Obligation!.Id}),"Modal: sin importe se conserva NULL real en MySQL");
        var o=entry.Obligation! with{Amount=0,AmountConfirmed=true};await store.SaveServiceChangeAsync(new(o,null,null,"",Guid.NewGuid().ToString("N")));entry=await Entry(svc.Id);
        check(entry.Status=="Sin cargo"&&await c.ExecuteScalarAsync<bool>("SELECT amount=0 AND amount IS NOT NULL FROM service_obligations WHERE id=@id",new{id=o.Id}),"Modal: cero explícito es SIN CARGO, distinto de NULL");
        o=entry.Obligation! with{Amount=100,AmountConfirmed=true};await store.SaveServiceChangeAsync(new(o,null,null,"",Guid.NewGuid().ToString("N")));entry=await Entry(svc.Id);
        check(entry.Status=="Pendiente"&&entry.Payments.Count==0,"Modal: importe/vencimiento sin nuevo abono no crea un pago");
        var request=new ServicePaymentChange(entry.Obligation!,30,DateTime.Today.AddDays(-1),"",Guid.NewGuid().ToString("N"));await Task.WhenAll(store.SaveServiceChangeAsync(request),store.SaveServiceChangeAsync(request));entry=await Entry(svc.Id);
        check(entry.Status=="Parcial"&&entry.Paid==30&&entry.Payments.Count==1,"Modal: doble envío y reintento del mismo token no duplican el pago");
        await Reject(()=>store.SaveServiceChangeAsync(new(entry.Obligation!,71,DateTime.Today,ServiceSheet.PaymentStamp(entry.Payments),Guid.NewGuid().ToString("N"))),"Modal: rechaza excedente del saldo");
        await Reject(()=>store.SaveServiceChangeAsync(new(entry.Obligation!,-1,DateTime.Today,ServiceSheet.PaymentStamp(entry.Payments),Guid.NewGuid().ToString("N"))),"Modal: rechaza pagos negativos");
        await store.SaveServiceChangeAsync(new(entry.Obligation!,70,DateTime.Today,ServiceSheet.PaymentStamp(entry.Payments),Guid.NewGuid().ToString("N")));entry=await Entry(svc.Id);
        check(entry.Status=="Pagado"&&entry.Payments.Count==2,"Modal: segundo pago se acumula y completa el total");
        var previousPayments=entry.Payments.Select(p=>p with{}).ToList();var last=previousPayments.OrderByDescending(p=>p.Date).ThenByDescending(p=>p.Id).First();
        var dateChange=new ServicePaymentChange(entry.Obligation!,null,DateTime.Today.AddDays(-5),ServiceSheet.PaymentStamp(entry.Payments),Guid.NewGuid().ToString("N"),last.Id);
        await store.SaveServiceChangeAsync(dateChange);entry=await Entry(svc.Id);
        var expectedPayments=previousPayments.Select(p=>p.Id==last.Id?p with{Date=dateChange.PaymentDate!.Value,Version=p.Version+1}:p);
        check(entry.Paid==100&&entry.Status=="Pagado"&&entry.Payments.OrderBy(p=>p.Id).SequenceEqual(expectedPayments.OrderBy(p=>p.Id)),"Modal: corregir fecha conserva IDs, importes, medios, notas y demás pagos, sin duplicados");
        await Reject(()=>store.SaveServiceChangeAsync(dateChange),"Modal: rechaza corrección de fecha con una versión desactualizada");
        var freshChange=dateChange with{Obligation=entry.Obligation!,ExpectedPayments=ServiceSheet.PaymentStamp(entry.Payments)};
        await Reject(()=>store.SaveServiceChangeAsync(freshChange with{PaymentDate=null}),"Modal: corregir fecha exige una fecha válida");
        await Reject(()=>store.SaveServiceChangeAsync(freshChange with{CorrectPaymentId=long.MaxValue}),"Modal: no permite corregir un pago ajeno al período");
        await Reject(()=>store.SaveServiceChangeAsync(freshChange with{NewPayment=1}),"Modal: no mezcla corrección de fecha y un nuevo pago");
        var annual=new RecurringService{Name="Anual cobertura prueba",Frequency="Anual",Currency="USD",DueMonth=1};annual.Id=await store.SaveServiceAsync(annual);
        var annualO=new ServiceObligation{ServiceId=annual.Id,Period=new(2026,1,1),Amount=19.99m,Currency="USD"};annualO.Id=await store.SaveObligationAsync(annualO);await store.SaveServicePaymentAsync(new(){ObligationId=annualO.Id,Amount=19.99m,Date=new(2026,6,2)});
        var covered=true;for(var i=0;i<12;i++){var r=await Entry(annual.Id,new DateTime(2026,6,1).AddMonths(i),new(2026,11,11));covered&=r.Status=="Pagado"&&r.PaymentDate==new DateTime(2026,6,2)&&r.Paid==19.99m;}
        check(covered,"Anual: doce meses cubiertos muestran PAGADO y la fecha original, sin duplicar pagos");
        check((await Entry(annual.Id,new(2027,6,1),new(2027,6,1))).Status=="Pagado"&&(await Entry(annual.Id,new(2027,6,1),new(2027,6,2))).Status=="Sin cargar","Anual: cobertura termina exactamente en el aniversario");
        check((await Entry(annual.Id,new(2026,6,1),new(2026,6,1))).Status!="Pagado","Anual: no muestra pagado antes de la fecha real");
        check((await Entry(annual.Id,new(2026,1,1),new(2026,1,31))).Status=="Pendiente","Anual: el período histórico no adelanta un pago futuro");
        var leap=new RecurringService{Name="Bisiesto cobertura",Frequency="Anual",Currency="USD"};leap.Id=await store.SaveServiceAsync(leap);var leapO=annualO with{Id=0,ServiceId=leap.Id,Period=new(2024,2,1)};leapO.Id=await store.SaveObligationAsync(leapO);await store.SaveServicePaymentAsync(new(){ObligationId=leapO.Id,Amount=19.99m,Date=new(2024,2,29)});
        check((await Entry(leap.Id,new(2025,2,1),new(2025,2,27))).Status=="Pagado"&&(await Entry(leap.Id,new(2025,2,1),new(2025,2,28))).Status=="Sin cargar","Anual: aniversario bisiesto se resuelve correctamente");
        var renewal=annualO with{Id=0,Period=new DateTime(2027,6,1)};renewal.Id=await store.SaveObligationAsync(renewal);await store.SaveServicePaymentAsync(new(){ObligationId=renewal.Id,Amount=19.99m,Date=new(2027,6,2)});
        check((await Entry(annual.Id,new(2027,6,1),new(2027,6,2))).CoverageEnd==new DateTime(2028,6,2),"Anual: una renovación extiende vigencia y conserva el pago anterior");
        var report=TextReport.Render(await store.ReadAsync(),new(2026,11,11));check(report.Contains("02/06/2026")&&report.Contains("VIGENTE HASTA"),"Anual: vigencia y pago original se reflejan en el TXT");
        var before=(await store.CaptureBackupAsync("test")).Document;var backupPath=Path.Combine(temp,"before-service-delete.pfinance");const string password="Recuperación de servicio 2026!";var bs=new BackupService(store,"test");var document=await bs.ExportAsync(backupPath,password);var hash=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(backupPath)));var proof=new ServiceRecovery(backupPath,hash,BackupValidation.Fingerprint(document),svc.Id,svc.Version);
        await Reject(()=>store.DeleteServiceWithRecoveryAsync(proof,false),"Eliminar: exige confirmación explícita");
        await Reject(()=>store.DeleteServiceWithRecoveryAsync(proof with{FileHash="invalid"},true),"Eliminar: respaldo alterado bloquea la eliminación");
        await Reject(()=>store.DeleteServiceWithRecoveryAsync(proof with{LedgerFingerprint="stale"},true),"Eliminar: exige respaldo del estado actual");
        await store.DeleteServiceWithRecoveryAsync(proof,true);var deleted=(await store.CaptureBackupAsync("test")).Document;
        check(!deleted.Services.Any(s=>s.Id==svc.Id)&&!deleted.ServiceObligations.Any(x=>x.ServiceId==svc.Id)&&!deleted.ServicePayments.Any(p=>entry.Payments.Any(old=>old.Id==p.Id)),"Eliminar: borra definición, períodos, pagos y auditoría propia en una transacción");
        check(before.Movements.SequenceEqual(deleted.Movements)&&deleted.Services.Any(s=>s.Id==annual.Id),"Eliminar: conserva consumos y todos los demás servicios");
        var prepared=await bs.PrepareRestoreAsync(backupPath,password);await bs.RestoreAsync(prepared,true);
        check(BackupValidation.Fingerprint((await store.CaptureBackupAsync("test")).Document)==BackupValidation.Fingerprint(before),"Eliminar: respaldo recupera exactamente el servicio eliminado y toda la contabilidad");
        check((await Entry(svc.Id)).Payments.Single(p=>p.Id==last.Id).Date==dateChange.PaymentDate,"Modal: exportar y restaurar conserva la fecha de pago corregida");
        var missing=before.ServiceObligations.First(o=>!o.AmountConfirmed);check(await c.ExecuteScalarAsync<bool>("SELECT amount IS NULL FROM service_obligations WHERE id=@id",new{id=missing.Id}),"Restaurar: los importes sin cargar vuelven como NULL real");
        check((await Entry(annual.Id,new(2026,11,1),new(2026,11,11))).Status=="Pagado","Anual: restauración conserva pagos y cobertura calculada");
    }
}
