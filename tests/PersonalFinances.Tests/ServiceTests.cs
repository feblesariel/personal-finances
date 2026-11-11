using System.Collections.Immutable;
using PersonalFinances.Core;

internal static class ServiceTests
{
    internal static async Task Run(FinanceStore store,Action<bool,string> check)
    {
        var before=await store.CaptureBackupAsync("test");
        async Task Reject(Func<Task> action,string label){try{await action();}catch(RuleException){check(true,label);return;}throw new Exception("Accepted: "+label);}
        var s=new RecurringService{Name="Internet prueba",UsualAmount=100,DueDay=31};s.Id=await store.SaveServiceAsync(s);
        check(s.Id>0,"Servicios: crear mensual");
        var annual=new RecurringService{Name="Anual USD",Frequency="Anual",Currency="USD",UsualAmount=19.99m,DueMonth=2,DueDay=29};annual.Id=await store.SaveServiceAsync(annual);
        var a=ServiceRules.Propose(annual,[],new(2027,1,1));a.Id=await store.SaveObligationAsync(a);
        check(a.Due==new DateTime(2027,2,28)&&ServiceRules.Propose(annual,[a],DateTime.Today).Period==new DateTime(2028,2,1),"Servicios: anual, bisiesto y sin obligaciones mensuales");
        var o=ServiceRules.Propose(s,[],new(2026,2,1));o.Id=await store.SaveObligationAsync(o);
        check(o.Due==new DateTime(2026,2,28),"Servicios: ajuste de día 31 a febrero");
        var next=ServiceRules.Propose(s,[o],DateTime.Today);next.Id=await store.SaveObligationAsync(next);
        check(next.Due==new DateTime(2026,3,31),"Servicios: varios períodos bajo demanda, conserva día habitual");
        await Reject(()=>store.SaveObligationAsync(o with{Id=0}),"Servicios: rechazo de duplicados");
        var p=new ServicePayment{ObligationId=o.Id,Amount=30,Method="VISA",Notes="Informativo"};p.Id=await store.SaveServicePaymentAsync(p);
        async Task<ServiceRow> Row(long id)=>(await store.ReadAsync()).Services.Rows(new(2026,11,11)).Single(r=>r.Obligation.Id==id);
        check((await Row(o.Id)) is {Paid:30,Balance:70,Overdue:true} r&&r.Status=="Pago parcial · Vencido","Servicios: parcial y vencido coexisten, saldo exacto");
        var p2=new ServicePayment{ObligationId=o.Id,Amount=20};p2.Id=await store.SaveServicePaymentAsync(p2);
        check((await Row(o.Id)).Paid==50,"Servicios: acumula múltiples pagos parciales");
        await Reject(()=>store.SaveServicePaymentAsync(new(){ObligationId=o.Id,Amount=51}),"Servicios: rechazo de excedentes");
        await Reject(()=>store.SaveServicePaymentAsync(new(){ObligationId=o.Id,Amount=-1}),"Servicios: rechazo de negativos");
        await Reject(()=>store.SaveObligationAsync(o with{Amount=49}),"Servicios: no reducir total por debajo de pagos");
        await store.SaveServicePaymentAsync(p2 with{Amount=10});p2=(await store.ReadAsync()).Services.Payments.Single(x=>x.Id==p2.Id);
        check((await Row(o.Id)).Balance==60,"Servicios: corrección de pago recalcula saldo");
        await store.SaveServicePaymentAsync(p2 with{Voided=true});check((await Row(o.Id)).Balance==70,"Servicios: anulación conserva historial y libera saldo");
        await store.SaveServicePaymentAsync(new(){ObligationId=o.Id,Amount=70});check((await Row(o.Id)).BaseStatus=="Pagado","Servicios: pago completo calculado");
        var zero=next with{Id=0,Period=next.Period.AddMonths(1),Amount=0};zero.Id=await store.SaveObligationAsync(zero);check((await Row(zero.Id)).BaseStatus=="Sin cargo","Servicios: importe cero bonificado");
        var card=new RecurringService{Name="VISA obligación",Type="Tarjeta de crédito",CardId=before.Document.Cards[0].Id};card.Id=await store.SaveServiceAsync(card);
        var bill=new ServiceObligation{ServiceId=card.Id,Period=new(2026,11,1),Due=new(2026,11,30),Amount=2504268.44m,Minimum=194340};bill.Id=await store.SaveObligationAsync(bill);
        await store.SaveServicePaymentAsync(new(){ObligationId=bill.Id,Amount=194340,Notes="Pago mínimo"});
        check((await Row(bill.Id)) is {Balance:2309928.44m} br&&br.Status=="Pago parcial · Mínimo cubierto","Servicios: mínimo cubierto mantiene deuda parcial exacta");
        await store.SaveServiceAsync(s with{UsualAmount=200});
        check((await Row(o.Id)).Obligation.Amount==100,"Servicios: cambios habituales conservan históricos");
        var snapshot=await store.ReadAsync();check(snapshot.Services.Obligations.Count(x=>x.ServiceId==s.Id)==3,"Servicios: historial completo del servicio");
        var after=await store.CaptureBackupAsync("test");
        check(before.Document.Movements.SequenceEqual(after.Document.Movements)&&before.Document.Periods.SequenceEqual(after.Document.Periods),"Servicios: VISA informativa no crea consumos ni cambia dashboard, estadísticas o períodos");
        var text=TextReport.Render(snapshot,new(2026,11,11));check(text.IndexOf("SERVICIOS -",StringComparison.Ordinal)>text.IndexOf("TOTAL GENERAL",StringComparison.Ordinal)&&text.Contains("MÍNIMO CUBIERTO")&&text.Contains("Exigible:"),"Servicios: TXT agrega sección después de tarjetas y separa monedas");
        var invalid=after.Document with{ServicePayments=after.Document.ServicePayments.Select(x=>x with{Amount=9999999}).ToList()};
        await Reject(()=>Task.Run(()=>BackupValidation.Validate(invalid)),"Servicios: respaldo con pagos inválidos rechazado antes de modificar destino");
        var flexible=new RecurringService{Name="Sin vencimiento",UsualAmount=80};flexible.Id=await store.SaveServiceAsync(flexible);
        var flexiblePeriod=ServiceRules.Propose(flexible,[],DateTime.Today);flexiblePeriod.Id=await store.SaveObligationAsync(flexiblePeriod);
        check((await Row(flexiblePeriod.Id)) is {Overdue:false,DueLabel:"Sin definir"},"Servicios: vencimiento opcional en alta y período, no inventa mora");
        await Reject(()=>store.SaveObligationAsync(flexiblePeriod with{Id=0,Currency="USD"}),"Servicios: un servicio común no tiene dos monedas en el mismo período");
        var dual=new RecurringService{Name="Resumen dos monedas",Type="Tarjeta de crédito",UsualAmount=100,UsualAmountUsd=20};dual.Id=await store.SaveServiceAsync(dual);
        var dualPeriod=ServiceRules.Propose(dual,[],DateTime.Today);await store.SaveServicePeriodAsync(dualPeriod,20);
        var dualRows=(await store.ReadAsync()).Services.Rows(DateTime.Today).Where(r=>r.Service.Id==dual.Id).ToArray();
        check(dualRows.Length==2&&dual.CardId==null&&dualRows.Select(r=>r.Obligation.Currency).Distinct().Count()==2,"Servicios: una tarjeta sin referencia tiene ARS y USD en el mismo mes");
        await store.SaveServicePaymentAsync(new(){ObligationId=dualRows.Single(r=>r.Obligation.Currency=="ARS").Obligation.Id,Amount=50});
        await store.SaveServicePaymentAsync(new(){ObligationId=dualRows.Single(r=>r.Obligation.Currency=="USD").Obligation.Id,Amount=20});
        dualRows=(await store.ReadAsync()).Services.Rows(DateTime.Today).Where(r=>r.Service.Id==dual.Id).ToArray();
        check(dualRows.Single(r=>r.Obligation.Currency=="ARS") is {Balance:50,BaseStatus:"Pago parcial"}&&dualRows.Single(r=>r.Obligation.Currency=="USD") is {Balance:0,BaseStatus:"Pagado"},"Servicios: pagar USD no cancela ARS y los parciales se calculan por moneda");
        await Reject(()=>store.SaveServicePeriodAsync(dualPeriod with{Period=dualPeriod.Period.AddMonths(10)},-1),"Servicios: ambos cargos se guardan atómicamente");
        check(!(await store.ReadAsync()).Services.Obligations.Any(o=>o.ServiceId==dual.Id&&o.Period==dualPeriod.Period.AddMonths(10)),"Servicios: rollback no deja la mitad ARS del resumen");
        var definitions=(await store.ReadAsync()).Services.Services.Count;
        await store.PrepareServiceMonthAsync(DateTime.Today);var prepared=(await store.ReadAsync()).Services.Obligations.Count;
        check(await store.PrepareServiceMonthAsync(DateTime.Today)==0&&(await store.ReadAsync()).Services.Obligations.Count==prepared,"Servicios: preparar el mes repetidamente no duplica períodos ni monedas");
        await store.PrepareServiceMonthAsync(DateTime.Today.AddMonths(1));
        check((await store.ReadAsync()).Services.Services.Count==definitions,"Servicios: siguiente mes reutiliza servicios fijos sin volver a darlos de alta");
        check((await store.ReadAsync()).Services.Obligations.Count(o=>o.ServiceId==annual.Id)==1,"Servicios: preparar un mes no crea cargos mensuales del anual");
    }
}
