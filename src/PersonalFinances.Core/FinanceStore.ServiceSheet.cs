using Dapper;
using MySqlConnector;

namespace PersonalFinances.Core;
public sealed partial class FinanceStore
{
    private static async Task MigrateServiceSheet(MySqlConnection c)
    {
        if(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM schema_migrations WHERE version=6")>0)return;
        if(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='service_obligations' AND COLUMN_NAME='amount_confirmed'")==0)
            await c.ExecuteAsync("ALTER TABLE service_obligations ADD COLUMN amount_confirmed BOOLEAN NOT NULL DEFAULT 1",commandTimeout:120);
        await c.ExecuteAsync("CREATE TABLE IF NOT EXISTS service_months(period DATE PRIMARY KEY) ENGINE=InnoDB",commandTimeout:120);
        await using var tx=await c.BeginTransactionAsync();
        await c.ExecuteScalarAsync<string>("SELECT `value` FROM settings WHERE `key`='revision' FOR UPDATE",transaction:tx);
        // Previous zero defaults cannot prove that the user explicitly waived a charge.
        await c.ExecuteAsync("UPDATE service_obligations SET amount_confirmed=0 WHERE amount=0; INSERT IGNORE INTO service_months SELECT DISTINCT period FROM service_obligations; UPDATE settings SET `value`=CAST(`value` AS UNSIGNED)+1 WHERE `key`='revision'; INSERT INTO schema_migrations VALUES(6,UTC_TIMESTAMP(6));",transaction:tx);
        await tx.CommitAsync();
    }
    public Task<int> CreateServiceMonthAsync(DateTime month,bool rejectDuplicate=true)=>Write(async(c,t)=>
    {
        month=ServiceSheet.Month(month);ServiceRules.Require(ServiceRules.DateValid(month),"Período inválido.");
        var exists=await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM service_months WHERE period=@month)",new{month},t);
        ServiceRules.Require(!exists||!rejectDuplicate,"Ese período ya existe. Seleccionalo en la lista.");
        await c.ExecuteAsync("INSERT IGNORE INTO service_months VALUES(@month)",new{month},t);
        var ledger=await ReadServices(c,t);var count=0;
        foreach(var s in ledger.Services.Where(s=>s.Active&&(s.Frequency=="Mensual"||s.DueMonth==month.Month)))
        {
            var currency=s.Type=="Tarjeta de crédito"?"ARS":s.Currency;
            if(ledger.Obligations.Any(o=>o.ServiceId==s.Id&&o.Period==month&& (s.Type!="Tarjeta de crédito"||o.Currency=="ARS")))continue;
            await SaveObligation(c,t,new(){ServiceId=s.Id,Period=month,Currency=currency,Amount=0,AmountConfirmed=false});count++;
        }
        return count;
    });
    public Task<long> SaveServiceSheetAsync(ServiceSheetEdit edit)=>Write(async(c,t)=>
    {
        var o=edit.Obligation;
        var s=await c.QuerySingleAsync<RecurringService>(ServiceSql+" WHERE id=@ServiceId",o,t);
        ServiceRules.Require(s.Type!="Tarjeta de crédito"||o.Currency=="ARS","La planilla de tarjetas se registra en ARS. Los USD anteriores se conservan en el historial.");
        var payments=(await c.QueryAsync<ServicePayment>(PaymentSql+" WHERE obligation_id=@Id",o,t)).ToList();
        ServiceRules.Require(ServiceSheet.PaymentStamp(payments)==edit.ExpectedPayments,"Los pagos cambiaron. Actualizá la planilla antes de guardar.");
        var paid=payments.Where(p=>!p.Voided).Sum(p=>p.Amount);
        ServiceRules.Require(ServiceRules.MoneyValid(edit.TotalPaid)&&edit.TotalPaid>=paid,"Para reducir un pago registrado, usá Pagos de esta fila y corregí o anulá el pago original.");
        ServiceRules.Require(o.AmountConfirmed||edit.TotalPaid==0,"Cargá primero el importe del período.");
        ServiceRules.Require(edit.TotalPaid<=o.Amount,"Lo abonado no puede superar el importe del período.");
        ServiceRules.Require(edit.TotalPaid==0||edit.PaymentDate!=null&&ServiceRules.DateValid(edit.PaymentDate.Value),"Completá la fecha real del pago.");
        var id=await SaveObligation(c,t,o);
        await c.ExecuteAsync("INSERT IGNORE INTO service_months VALUES(@Period)",o,t);
        if(edit.TotalPaid>paid)
            await SaveServicePayment(c,t,new(){ObligationId=id,Amount=edit.TotalPaid-paid,Date=edit.PaymentDate!.Value,Token=edit.Token,Notes="Pago desde planilla mensual"});
        else if(paid>0&&edit.PaymentDate!=payments.Where(p=>!p.Voided).Max(p=>p.Date))
        {
            var active=payments.Where(p=>!p.Voided).ToList();
            ServiceRules.Require(active.Count==1,"Hay varios pagos. Cambiá la fecha del pago correspondiente desde Pagos de esta fila.");
            await SaveServicePayment(c,t,active[0] with{Date=edit.PaymentDate!.Value});
        }
        return id;
    });
}
