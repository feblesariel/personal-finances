using Dapper;
using MySqlConnector;

namespace PersonalFinances.Core;

public sealed record ServiceCurrencyCorrection(ServiceObligation Obligation,string ExpectedPayments,DateTime SelectedMonth);

public sealed partial class FinanceStore
{
    private static async Task CorrectServiceCurrency(MySqlConnection c,MySqlTransaction t,RecurringService old,RecurringService service,ServiceCurrencyCorrection correction)
    {
        ServiceRules.Require(old.Type!="Tarjeta de crédito"&&service.Type!="Tarjeta de crédito","La corrección de moneda no se aplica a resúmenes de tarjetas.");
        ServiceRules.Require(ServiceRules.DateValid(correction.SelectedMonth)&&correction.SelectedMonth.Day==1,"Período de corrección inválido.");
        var target=await c.QuerySingleOrDefaultAsync<ServiceObligation>(ObligationSql+" WHERE id=@Id",correction.Obligation,t);
        ServiceRules.Require(target!=null&&target.ServiceId==service.Id&&target.Version==correction.Obligation.Version&&target.Currency==correction.Obligation.Currency,"El período cambió. Volvé a abrir el servicio antes de corregir su moneda.");
        var payments=(await c.QueryAsync<ServicePayment>(PaymentSql+" WHERE obligation_id=@Id",target,t)).ToList();
        ServiceRules.Require(ServiceSheet.PaymentStamp(payments)==correction.ExpectedPayments,"Los pagos cambiaron. Volvé a abrir el servicio antes de corregir su moneda.");
        var future=(await c.QueryAsync<ServiceObligation>(ObligationSql+" WHERE service_id=@Id AND period>@SelectedMonth AND amount_confirmed=0 AND NOT EXISTS(SELECT 1 FROM service_payments p WHERE p.obligation_id=service_obligations.id)",new{service.Id,correction.SelectedMonth},t)).ToList();
        foreach(var obligation in future.Prepend(target!).DistinctBy(o=>o.Id).Where(o=>o.Currency!=service.Currency))
        {
            await Audit(c,t,"service_obligations",obligation.Id,"currency",obligation);
            await c.ExecuteAsync("UPDATE service_obligations SET currency=@Currency,version=version+1 WHERE id=@Id",new{service.Currency,obligation.Id},t);
            // Payments inherit their currency from the obligation. Invalidate stale editors while preserving all payment values.
            foreach(var payment in payments.Where(p=>p.ObligationId==obligation.Id))
            {
                await Audit(c,t,"service_payments",payment.Id,"currency",payment);
                await c.ExecuteAsync("UPDATE service_payments SET version=version+1 WHERE id=@Id",payment,t);
            }
        }
    }
}
