using PersonalFinances.Core;
internal static class SheetTests
{
    internal static async Task Run(FinanceStore store,Action<bool,string> check)
    {
        var original=(await store.CaptureBackupAsync("test")).Document.Movements;
        var monthly=new RecurringService{Name="Planilla Internet",UsualAmount=22554.99m};monthly.Id=await store.SaveServiceAsync(monthly);
        var card=new RecurringService{Name="Planilla VISA",Type="Tarjeta de crédito"};card.Id=await store.SaveServiceAsync(card);
        var annual=new RecurringService{Name="Planilla anual",Frequency="Anual",Currency="USD",DueMonth=2};annual.Id=await store.SaveServiceAsync(annual);
        var month=new DateTime(2035,11,1);await store.CreateServiceMonthAsync(month);
        async Task<ServiceSheetEntry> Row(int id,DateTime? m=null)=>ServiceSheet.Rows((await store.ReadAsync()).Services,m??month,DateTime.Today).Single(r=>r.ServiceId==id);
        async Task Reject(Func<Task> action,string label){try{await action();}catch(RuleException){check(true,label);return;}throw new Exception("Accepted: "+label);}
        check((await Row(monthly.Id)) is {Status:"Sin cargar",Confirmed:false,Paid:0},"Planilla: nuevo mes no confirma el importe habitual ni bonifica importes vacíos");
        check((await Row(card.Id)) is {Currency:"ARS"}&& (await store.ReadAsync()).Services.Obligations.Count(o=>o.ServiceId==card.Id&&o.Period==month)==1,"Planilla: una sola fila de tarjeta en ARS");
        check((await Row(annual.Id)) is {Applicable:false,Status:"No corresponde"},"Planilla: anual no genera deuda mensual");
        async Task Save(int id,decimal amount,bool confirmed,decimal paid,DateTime? date)
        {var r=await Row(id);await store.SaveServiceSheetAsync(new(r.Obligation! with{Amount=amount,AmountConfirmed=confirmed},paid,date,ServiceSheet.PaymentStamp(r.Payments),Guid.NewGuid().ToString("N")));}
        await Save(monthly.Id,22554.99m,true,0,null);check((await Row(monthly.Id)).Status=="Pendiente","Planilla: importe confirmado sin pago queda pendiente");
        await Save(monthly.Id,22554.99m,true,10000,new(2035,11,12));check((await Row(monthly.Id)) is {Status:"Parcial",Paid:10000,Balance:12554.99m},"Planilla: pago rápido parcial calcula saldo");
        await Save(monthly.Id,22554.99m,true,22554.99m,new(2035,11,30));var full=await Row(monthly.Id);
        check(full.Status=="Pagado"&&full.Payments.Count==2&&full.PaymentDate==new DateTime(2035,11,30),"Planilla: aumentar abonado acumula pagos independientes y fecha");
        await Reject(()=>Save(monthly.Id,22554.99m,true,5,DateTime.Today),"Planilla: una reducción no borra pagos previos");
        await Save(card.Id,0,true,0,null);check((await Row(card.Id)).Status=="Sin cargo","Planilla: cero confirmado bonifica explícitamente");
        await store.CreateServiceMonthAsync(month.AddMonths(1));check((await Row(monthly.Id,month.AddMonths(1))) is {Confirmed:false,Paid:0,PaymentDate:null},"Planilla: nuevo mes no copia pagos, fechas ni estados");
        check((await Row(monthly.Id)).Paid==22554.99m,"Planilla: noviembre conserva todos sus pagos al crear diciembre");
        await Reject(()=>store.CreateServiceMonthAsync(month),"Planilla: no duplica meses existentes");
        await store.CreateServiceMonthAsync(new(2036,2,1));check((await Row(annual.Id,new(2036,2,1))).Applicable,"Planilla: anual aparece en su renovación");
        var legacy=new ServiceObligation{ServiceId=card.Id,Period=month,Currency="USD",Amount=20};legacy.Id=await store.SaveObligationAsync(legacy);
        await store.SaveServicePaymentAsync(new(){ObligationId=legacy.Id,Amount=5});var legacyRow=await Row(card.Id);
        check(legacyRow.LegacyWarning&&legacyRow.Currency=="ARS"&&legacyRow.Paid==0&&legacyRow.Legacy.Single().Paid==5,"Planilla: USD previo se conserva con advertencia y no se suma a ARS");
        check(original.SequenceEqual((await store.CaptureBackupAsync("test")).Document.Movements),"Planilla: ninguna edición modifica consumos de tarjetas");
        var report=TextReport.Render(await store.ReadAsync(),month);
        check(report.Contains("PAGADO")&&!report.Contains("Exigible:")&&!report.Contains("Pago desde planilla mensual"),"Planilla: TXT compacto muestra el pago sin detalle financiero");
        await store.SaveServiceAsync(monthly with{Frequency="Anual",DueMonth=2});
        check((await Row(monthly.Id)).Paid==22554.99m,"Planilla: cambiar periodicidad conserva importes y pagos históricos");
        var added=await store.SaveServiceAsync(new(){Name="Servicio incorporado después"});
        check(!ServiceSheet.Rows((await store.ReadAsync()).Services,month,DateTime.Today).Any(r=>r.ServiceId==added),"Planilla: agregar un servicio después no inventa pendientes en meses anteriores");
    }
}
