using Dapper;
using MySqlConnector;
using System.Security.Cryptography;

namespace PersonalFinances.Core;
public sealed record ServicePaymentChange(ServiceObligation Obligation,decimal? NewPayment,DateTime? PaymentDate,string ExpectedPayments,string Token);
public sealed record ServiceRecovery(string Path,string FileHash,string LedgerFingerprint,int ServiceId,int Version);
public sealed partial class FinanceStore
{
    private static async Task MigrateNullableServiceAmount(MySqlConnection c)
    {
        if(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM schema_migrations WHERE version=7")>0)return;
        await c.ExecuteAsync("ALTER TABLE service_obligations MODIFY amount DECIMAL(18,2) NULL",commandTimeout:120);
        await using var t=await c.BeginTransactionAsync();
        await c.ExecuteScalarAsync<string>("SELECT `value` FROM settings WHERE `key`='revision' FOR UPDATE",transaction:t);
        await c.ExecuteAsync("UPDATE service_obligations SET amount=NULL WHERE amount_confirmed=0; UPDATE settings SET `value`=CAST(`value` AS UNSIGNED)+1 WHERE `key`='revision'; INSERT INTO schema_migrations VALUES(7,UTC_TIMESTAMP(6));",transaction:t);
        await t.CommitAsync();
    }
    public Task<long> SaveServiceChangeAsync(ServicePaymentChange change)=>Write(async(c,t)=>
    {
        using var timing=OperationLog.Measure("service.save");
        ServiceRules.Require(Guid.TryParseExact(change.Token,"N",out _),"Identificador de operación inválido.");
        var o=change.Obligation;
        if(change.NewPayment!=null)
        {
            ServiceRules.Require(change.NewPayment>0&&ServiceRules.MoneyValid(change.NewPayment.Value),"El nuevo pago debe ser mayor a cero y tener hasta dos decimales.");
            // An exact retry after a lost response must not insert the payment twice.
            var prior=await c.QuerySingleOrDefaultAsync<ServicePayment>(PaymentSql+" WHERE token=@Token",change,t);
            if(prior!=null)
            {
                var linked=await c.QuerySingleAsync<ServiceObligation>(ObligationSql+" WHERE id=@id",new{id=prior.ObligationId},t);
                ServiceRules.Require(linked.ServiceId==o.ServiceId&&linked.Period==o.Period&&linked.Currency==o.Currency&&linked.Amount==o.Amount&&linked.AmountConfirmed==o.AmountConfirmed&&linked.Due==o.Due&&prior.Amount==change.NewPayment&&prior.Date==change.PaymentDate,"La operación ya fue utilizada para otro pago.");
                return prior.ObligationId;
            }
        }
        var payments=(await c.QueryAsync<ServicePayment>(PaymentSql+" WHERE obligation_id=@Id",o,t)).ToList();
        ServiceRules.Require(ServiceSheet.PaymentStamp(payments)==change.ExpectedPayments,"Los pagos cambiaron. Cerrá y volvé a abrir el servicio antes de guardar.");
        var s=await c.QuerySingleAsync<RecurringService>(ServiceSql+" WHERE id=@ServiceId",o,t);
        ServiceRules.Require(s.Type!="Tarjeta de crédito"||o.Currency=="ARS","El resumen del servicio se registra en ARS.");
        var paid=payments.Where(p=>!p.Voided).Sum(p=>p.Amount);
        ServiceRules.Require(change.NewPayment==null||o.AmountConfirmed&&change.NewPayment<=o.Amount-paid,"El nuevo pago supera el saldo restante o falta el importe del período.");
        ServiceRules.Require(change.NewPayment==null||change.PaymentDate!=null&&ServiceRules.DateValid(change.PaymentDate.Value),"Ingresá una fecha válida para el pago.");
        var id=await SaveObligation(c,t,o);
        if(change.NewPayment is {} amount)await SaveServicePayment(c,t,new(){ObligationId=id,Amount=amount,Date=change.PaymentDate!.Value,Token=change.Token});
        return id;
    });
    public async Task DeleteServiceWithRecoveryAsync(ServiceRecovery recovery,bool confirmed)
    {
        ServiceRules.Require(confirmed,"Confirmá expresamente la eliminación del servicio y de todo su historial.");
        await Task.Run(async()=>{
        ServiceRules.Require(File.Exists(recovery.Path),"No está disponible el respaldo de recuperación. Generá uno antes de eliminar.");
        await using(var file=File.OpenRead(recovery.Path))
            ServiceRules.Require(Convert.ToHexString(await SHA256.HashDataAsync(file))==recovery.FileHash,"El respaldo cambió. Generá otro antes de eliminar.");
        });
        await Write(async(c,t)=>
        {
            using var timing=OperationLog.Measure("service.delete");
            var before=await CaptureBackup(c,t,"deletion-check");
            ServiceRules.Require(BackupValidation.Fingerprint(before.Document)==recovery.LedgerFingerprint,"Los datos cambiaron después del respaldo. Generá una copia actualizada antes de eliminar.");
            var service=before.Document.Services.SingleOrDefault(s=>s.Id==recovery.ServiceId);
            ServiceRules.Require(service!=null&&service.Version==recovery.Version,"El servicio cambió o ya fue eliminado.");
            var ids=before.Document.ServiceObligations.Where(o=>o.ServiceId==recovery.ServiceId).Select(o=>o.Id).ToArray();
            var payIds=before.Document.ServicePayments.Where(p=>ids.Contains(p.ObligationId)).Select(p=>p.Id).ToArray();
            if(payIds.Length>0)await c.ExecuteAsync("DELETE FROM audit_log WHERE entity='service_payments' AND entity_id IN @payIds",new{payIds},t);
            if(ids.Length>0)
            {
                await c.ExecuteAsync("DELETE FROM audit_log WHERE entity='service_obligations' AND entity_id IN @ids; DELETE FROM service_payments WHERE obligation_id IN @ids; DELETE FROM service_obligations WHERE id IN @ids",new{ids},t);
            }
            await c.ExecuteAsync("DELETE FROM audit_log WHERE entity='recurring_services' AND entity_id=@id; DELETE FROM recurring_services WHERE id=@id",new{id=recovery.ServiceId},t);
            return true;
        });
    }
}
public static class OperationLog
{
    public static IDisposable Measure(string operation)=>new Timing(operation);
    private sealed class Timing:IDisposable
    {
        private readonly string operation;
        public Timing(string operation){this.operation=operation;LocalConfig.Trace(operation,"started",0);}
        private readonly System.Diagnostics.Stopwatch watch=System.Diagnostics.Stopwatch.StartNew();
        public void Dispose(){LocalConfig.Trace(operation,"finished",watch.ElapsedMilliseconds);}
    }
}
