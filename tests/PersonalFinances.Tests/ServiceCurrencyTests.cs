using PersonalFinances.Core;

internal static class ServiceCurrencyTests
{
    internal static async Task Run(FinanceStore store,string temp,Action<bool,string> check)
    {
        async Task Reject(Func<Task> action,string label){try{await action();}catch(RuleException){check(true,label);return;}throw new Exception("Accepted: "+label);}
        async Task<ServiceSheetEntry> Row(int id,DateTime month)=>ServiceSheet.Rows((await store.ReadAsync()).Services,month,new(2026,11,11)).Single(r=>r.ServiceId==id);
        var before=(await store.CaptureBackupAsync("currency-test")).Document;
        var monthly=new RecurringService{Name="Cambio moneda mensual"};monthly.Id=await store.SaveServiceAsync(monthly);
        var old=new ServiceObligation{ServiceId=monthly.Id,Period=new(2026,10,1),Amount=100};old.Id=await store.SaveObligationAsync(old);
        await store.SaveServicePaymentAsync(new(){ObligationId=old.Id,Date=new(2026,10,10),Amount=100});
        var current=old with{Id=0,Period=new(2026,11,1)};current.Id=await store.SaveObligationAsync(current);
        var payment=new ServicePayment{ObligationId=current.Id,Date=new(2026,11,10),Amount=100,Method="VISA",Notes="Pago original"};payment.Id=await store.SaveServicePaymentAsync(payment);
        var future=current with{Id=0,Period=new(2026,12,1),Amount=0,AmountConfirmed=false};future.Id=await store.SaveObligationAsync(future);
        var confirmedFuture=current with{Id=0,Period=new(2027,1,1)};confirmedFuture.Id=await store.SaveObligationAsync(confirmedFuture);
        await store.SaveServiceAsync(monthly with{Currency="USD"});var row=await Row(monthly.Id,current.Period);
        check(row.Service.Currency=="USD"&&row.Currency=="ARS","Moneda: reproduce definición USD con obligación existente ARS sin alterar históricos implícitamente");
        var correction=new ServiceCurrencyCorrection(row.Obligation!,ServiceSheet.PaymentStamp(row.RecordedPayments),row.Month);
        await Reject(()=>store.SaveServiceAsync(row.Service,correction with{ExpectedPayments="outdated"}),"Moneda: pagos concurrentes bloquean la corrección");
        await Reject(()=>store.SaveServiceAsync(row.Service,correction with{Obligation=current with{Version=99}}),"Moneda: período desactualizado bloquea la corrección");
        var foreign=new RecurringService{Name="Otro servicio moneda"};foreign.Id=await store.SaveServiceAsync(foreign);
        await Reject(()=>store.SaveServiceAsync(foreign,correction),"Moneda: no permite corregir obligaciones de otro servicio");
        check((await Row(monthly.Id,current.Period)).Service.Version==row.Service.Version,"Moneda: errores no guardan parcialmente la definición");
        await store.SaveServiceAsync(row.Service,correction);var changed=await Row(monthly.Id,current.Period);
        check(changed.Currency=="USD"&&changed.Paid==100&&changed.PaymentDate==payment.Date&&changed.Status=="Pagado"&&changed.Payments.Single()==payment with{Version=payment.Version+1},"Moneda: corrige cargo y pago conservando ID, token, monto, fecha, notas y medio");
        check((await Row(monthly.Id,old.Period)).Currency=="ARS"&&(await Row(monthly.Id,confirmedFuture.Period)).Currency=="ARS","Moneda: otros períodos con importes reales conservan su moneda");
        check((await Row(monthly.Id,future.Period)) is {Currency:"USD",Confirmed:false,Paid:0},"Moneda: futuros períodos sin cargar adoptan la moneda y conservan NULL");
        await Reject(()=>store.SaveServicePaymentAsync(payment),"Moneda: un editor de pago abierto antes de la corrección no sobrescribe el cambio");
        var annual=new RecurringService{Name="Google One moneda prueba",Frequency="Anual",DueMonth=6};annual.Id=await store.SaveServiceAsync(annual);
        var annualCharge=new ServiceObligation{ServiceId=annual.Id,Period=new(2026,6,1),Amount=19.99m};annualCharge.Id=await store.SaveObligationAsync(annualCharge);
        await store.SaveServicePaymentAsync(new(){ObligationId=annualCharge.Id,Amount=19.99m,Date=new(2026,6,2)});
        var annualRow=await Row(annual.Id,new(2026,11,1));
        await store.SaveServiceAsync(annualRow.Service with{Currency="USD"},new(annualRow.Obligation!,ServiceSheet.PaymentStamp(annualRow.RecordedPayments),annualRow.Month));
        check((await Row(annual.Id,new(2026,11,1))) is {Currency:"USD",Paid:19.99m,CoverageStart:{Year:2026,Month:6,Day:2},CoverageEnd:{Year:2027,Month:6,Day:2}}&&(await Row(annual.Id,new(2027,5,1))).Currency=="USD","Moneda: la cobertura anual hereda la corrección sin cambiar fechas ni duplicar pagos");
        var card=new RecurringService{Name="Tarjeta moneda protegida",Type="Tarjeta de crédito"};card.Id=await store.SaveServiceAsync(card);var cardCharge=current with{Id=0,ServiceId=card.Id};cardCharge.Id=await store.SaveObligationAsync(cardCharge);
        await Reject(()=>store.SaveServiceAsync(card,new(cardCharge,"",cardCharge.Period)),"Moneda: resúmenes de tarjetas conservan el tratamiento independiente en ARS");
        var report=TextReport.Render(await store.ReadAsync(),new(2026,11,11));var block=report[report.IndexOf(annual.Name,StringComparison.Ordinal)..];
        check(block.Contains("US$ 19,99"),"Moneda: TXT muestra USD para la obligación anual corregida");
        var backup=new BackupService(store,"currency-test");var path=Path.Combine(temp,"currency-test.pfinance");const string password="Moneda corregida prueba 2026!";await backup.ExportAsync(path,password);var prepared=await backup.PrepareRestoreAsync(path,password);await backup.RestoreAsync(prepared,true);
        check((await Row(annual.Id,new(2026,11,1))) is {Currency:"USD",Paid:19.99m,Payments.Count:1},"Moneda: respaldo y restauración conservan la corrección y el único pago");
        check(before.Movements.SequenceEqual((await store.CaptureBackupAsync("currency-test")).Document.Movements),"Moneda: ningún consumo ni total de tarjetas se modifica");
    }
}
