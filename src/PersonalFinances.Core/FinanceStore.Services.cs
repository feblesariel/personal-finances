using Dapper;
using MySqlConnector;

namespace PersonalFinances.Core;
public sealed partial class FinanceStore
{
    private const string ServiceSql="SELECT id,name,type,frequency,currency,usual_amount UsualAmount,usual_amount_usd UsualAmountUsd,due_day DueDay,due_month DueMonth,notes,active,card_id CardId,version FROM recurring_services";
    private const string ObligationSql="SELECT id,service_id ServiceId,period,due,COALESCE(amount,0) Amount,amount_confirmed AmountConfirmed,currency,minimum,installment,installments,notes,version FROM service_obligations";
    private const string PaymentSql="SELECT id,token,obligation_id ObligationId,payment_date Date,amount,method,notes,voided,version FROM service_payments";
    private static async Task<ServiceLedger> ReadServices(MySqlConnection c,MySqlTransaction t) => new ServiceLedger((await c.QueryAsync<RecurringService>(ServiceSql+" ORDER BY id",transaction:t)).ToList(),(await c.QueryAsync<ServiceObligation>(ObligationSql+" ORDER BY id",transaction:t)).ToList(),(await c.QueryAsync<ServicePayment>(PaymentSql+" ORDER BY id",transaction:t)).ToList()){Months=(await c.QueryAsync<DateTime>("SELECT period FROM service_months ORDER BY period",transaction:t)).ToList()};
    private static async Task MigrateServices(MySqlConnection c)
    {
        if(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM schema_migrations WHERE version=4")>0)return;
        await c.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS recurring_services(id INT AUTO_INCREMENT PRIMARY KEY,name VARCHAR(100) NOT NULL,type VARCHAR(40) NOT NULL,frequency VARCHAR(10) NOT NULL,currency CHAR(3) NOT NULL,usual_amount DECIMAL(18,2) NULL,due_day INT NOT NULL,due_month INT NOT NULL,notes TEXT NOT NULL,active BOOLEAN NOT NULL,card_id INT NULL,version INT NOT NULL DEFAULT 1,FOREIGN KEY(card_id) REFERENCES cards(id),CHECK(currency IN ('ARS','USD')),CHECK(usual_amount>=0),CHECK(due_day BETWEEN 1 AND 31),CHECK(due_month BETWEEN 1 AND 12)) ENGINE=InnoDB;
            CREATE TABLE IF NOT EXISTS service_obligations(id BIGINT AUTO_INCREMENT PRIMARY KEY,service_id INT NOT NULL,period DATE NOT NULL,due DATE NOT NULL,amount DECIMAL(18,2) NOT NULL,currency CHAR(3) NOT NULL,minimum DECIMAL(18,2) NULL,installment INT NULL,installments INT NULL,notes TEXT NOT NULL,version INT NOT NULL DEFAULT 1,FOREIGN KEY(service_id) REFERENCES recurring_services(id),UNIQUE KEY ux_service_period(service_id,period),INDEX ix_service_due(due),CHECK(amount>=0),CHECK(currency IN ('ARS','USD')),CHECK(minimum>=0 AND minimum<=amount)) ENGINE=InnoDB;
            CREATE TABLE IF NOT EXISTS service_payments(id BIGINT AUTO_INCREMENT PRIMARY KEY,token CHAR(32) NOT NULL UNIQUE,obligation_id BIGINT NOT NULL,payment_date DATE NOT NULL,amount DECIMAL(18,2) NOT NULL,method VARCHAR(30) NOT NULL,notes TEXT NOT NULL,voided BOOLEAN NOT NULL DEFAULT 0,version INT NOT NULL DEFAULT 1,FOREIGN KEY(obligation_id) REFERENCES service_obligations(id),INDEX ix_service_payment(obligation_id,voided),CHECK(amount>0)) ENGINE=InnoDB;
            INSERT INTO schema_migrations VALUES(4,UTC_TIMESTAMP(6));
            """,commandTimeout:120);
    }
    public Task<int> SaveServiceAsync(RecurringService s,ServiceCurrencyCorrection? currencyCorrection=null)=>Write(async(c,t)=>
    {
        ServiceRules.Validate(s);
        if(s.CardId!=null)ServiceRules.Require(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cards WHERE id=@CardId",s,t)==1,"Tarjeta inexistente.");
        if(s.Id==0){ServiceRules.Require(currencyCorrection==null,"Una corrección de moneda requiere un servicio existente.");return await c.ExecuteScalarAsync<int>("INSERT INTO recurring_services(name,type,frequency,currency,usual_amount,usual_amount_usd,due_day,due_month,notes,active,card_id) VALUES(@Name,@Type,@Frequency,@Currency,@UsualAmount,@UsualAmountUsd,@DueDay,@DueMonth,@Notes,@Active,@CardId); SELECT LAST_INSERT_ID();",s,t);}
        var old=await c.QuerySingleAsync<RecurringService>(ServiceSql+" WHERE id=@Id",s,t);
        ServiceRules.Require(old.Version==s.Version,"El servicio cambió. Actualizá antes de editar.");
        if(s.Type!="Tarjeta de crédito")ServiceRules.Require(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM (SELECT period FROM service_obligations WHERE service_id=@Id GROUP BY period HAVING COUNT(*)>1) pairs",s,t)==0,"Este servicio tiene períodos en ambas monedas; conservá el tipo Tarjeta de crédito.");
        if(s.Type!="Tarjeta de crédito")ServiceRules.Require(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM service_obligations WHERE service_id=@Id AND minimum IS NOT NULL",s,t)==0,"Con mínimos registrados, conservá el tipo Tarjeta de crédito.");
        if(currencyCorrection!=null)await CorrectServiceCurrency(c,t,old,s,currencyCorrection);
        await Audit(c,t,"recurring_services",s.Id,"edit",old);
        await c.ExecuteAsync("UPDATE recurring_services SET name=@Name,type=@Type,frequency=@Frequency,currency=@Currency,usual_amount=@UsualAmount,usual_amount_usd=@UsualAmountUsd,due_day=@DueDay,due_month=@DueMonth,notes=@Notes,active=@Active,card_id=@CardId,version=version+1 WHERE id=@Id",s,t);return s.Id;
    });
    private static async Task MigrateFlexibleServices(MySqlConnection c)
    {
        if(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM schema_migrations WHERE version=5")>0)return;
        if(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='recurring_services' AND COLUMN_NAME='usual_amount_usd'")==0)
            await c.ExecuteAsync("ALTER TABLE recurring_services ADD COLUMN usual_amount_usd DECIMAL(18,2) NULL CHECK(usual_amount_usd>=0)",commandTimeout:120);
        await c.ExecuteAsync("ALTER TABLE recurring_services MODIFY due_day INT NULL; ALTER TABLE service_obligations MODIFY due DATE NULL;",commandTimeout:120);
        if(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='service_obligations' AND INDEX_NAME='ux_service_period_currency'")==0)
            await c.ExecuteAsync("ALTER TABLE service_obligations ADD UNIQUE KEY ux_service_period_currency(service_id,period,currency)",commandTimeout:120);
        if(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='service_obligations' AND INDEX_NAME='ux_service_period'")>0)
            await c.ExecuteAsync("ALTER TABLE service_obligations DROP INDEX ux_service_period",commandTimeout:120);
        await c.ExecuteAsync("INSERT INTO schema_migrations VALUES(5,UTC_TIMESTAMP(6))");
    }
    public Task<long> SaveServicePeriodAsync(ServiceObligation ars,decimal? usdAmount)=>Write(async(c,t)=>
    {
        var id=await SaveObligation(c,t,ars);
        if(usdAmount!=null)
        {
            var service=await c.QuerySingleAsync<RecurringService>(ServiceSql+" WHERE id=@ServiceId",ars,t);
            ServiceRules.Require(service.Type=="Tarjeta de crédito"&&ars.Currency=="ARS"&&ars.Id==0,"Los dos cargos corresponden únicamente a un nuevo período de tarjeta.");
            await SaveObligation(c,t,ars with{Id=0,Currency="USD",Amount=usdAmount.Value,Minimum=null});
        }
        return id;
    });
    public Task<int> PrepareServiceMonthAsync(DateTime month)=>CreateServiceMonthAsync(month,false);
    public Task<long> SaveObligationAsync(ServiceObligation o)=>Write((c,t)=>SaveObligation(c,t,o));
    private static async Task<long> SaveObligation(MySqlConnection c,MySqlTransaction t,ServiceObligation o)
    {
        var s=await c.QuerySingleAsync<RecurringService>(ServiceSql+" WHERE id=@ServiceId",o,t);
        var old=await c.QuerySingleOrDefaultAsync<ServiceObligation>(ObligationSql+" WHERE id=@Id",o,t);
        var paid=await c.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(amount),0) FROM service_payments WHERE obligation_id=@Id AND voided=0",o,t);
        ServiceRules.Validate(o,s,paid);
        ServiceRules.Require(old!=null||s.Active,"El servicio está inactivo.");
        ServiceRules.Require(old==null||old.ServiceId==o.ServiceId&&old.Period==o.Period&&old.Version==o.Version,"El período cambió o su identidad no coincide. Actualizá los datos.");
        ServiceRules.Require(old==null||old.Currency==o.Currency||await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM service_payments WHERE obligation_id=@Id",o,t)==0,"No se puede cambiar la moneda con pagos registrados.");
        ServiceRules.Require(!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM service_obligations WHERE service_id=@ServiceId AND id<>@Id AND (@card=0 OR currency=@Currency) AND period=@Period)",new{o.ServiceId,o.Id,o.Period,annual=s.Frequency=="Anual",o.Currency,card=s.Type=="Tarjeta de crédito"},t),"Ya existe ese período del servicio.");
        await c.ExecuteAsync("INSERT IGNORE INTO service_months VALUES(@Period)",o,t);
        var values=new DynamicParameters(o);values.Add("AmountDb",o.AmountConfirmed?(decimal?)o.Amount:null);
        if(o.Id==0)return await c.ExecuteScalarAsync<long>("INSERT INTO service_obligations(service_id,period,due,amount,amount_confirmed,currency,minimum,installment,installments,notes) VALUES(@ServiceId,@Period,@Due,@AmountDb,@AmountConfirmed,@Currency,@Minimum,@Installment,@Installments,@Notes); SELECT LAST_INSERT_ID();",values,t);
        await Audit(c,t,"service_obligations",o.Id,"edit",old);
        await c.ExecuteAsync("UPDATE service_obligations SET due=@Due,amount=@AmountDb,amount_confirmed=@AmountConfirmed,currency=@Currency,minimum=@Minimum,installment=@Installment,installments=@Installments,notes=@Notes,version=version+1 WHERE id=@Id",values,t);return o.Id;
    }
    public Task<long> SaveServicePaymentAsync(ServicePayment p)=>Write((c,t)=>SaveServicePayment(c,t,p));
    private static async Task<long> SaveServicePayment(MySqlConnection c,MySqlTransaction t,ServicePayment p)
    {
        ServiceRules.Require(ServiceRules.DateValid(p.Date)&&ServiceRules.MoneyValid(p.Amount)&&p.Amount>0&&ServiceRules.Methods.Contains(p.Method)&&p.Notes!=null&&p.Notes.Length<=4000&&Guid.TryParseExact(p.Token,"N",out _),"Fecha, importe o medio de pago inválido.");
        var o=await c.QuerySingleAsync<ServiceObligation>(ObligationSql+" WHERE id=@ObligationId",p,t);
        ServiceRules.Require(o.AmountConfirmed||p.Voided,"Confirmá el importe del período antes de registrar un pago.");
        if(p.Id==0){var duplicate=await c.ExecuteScalarAsync<long?>("SELECT id FROM service_payments WHERE token=@Token",p,t);if(duplicate!=null)return duplicate.Value;}
        var old=await c.QuerySingleOrDefaultAsync<ServicePayment>(PaymentSql+" WHERE id=@Id",p,t);
        ServiceRules.Require(p.Id==0||old!=null&&!old.Voided&&old.Version==p.Version&&old.ObligationId==p.ObligationId&&old.Token==p.Token,"El pago cambió o fue anulado. Actualizá los datos.");
        var other=await c.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(amount),0) FROM service_payments WHERE obligation_id=@ObligationId AND id<>@Id AND voided=0",p,t);
        ServiceRules.Require(p.Voided||other+p.Amount<=o.Amount,"El pago supera el saldo pendiente.");
        if(p.Id==0){ServiceRules.Require(!p.Voided,"Un nuevo pago no puede estar anulado.");return await c.ExecuteScalarAsync<long>("INSERT INTO service_payments(token,obligation_id,payment_date,amount,method,notes) VALUES(@Token,@ObligationId,@Date,@Amount,@Method,@Notes); SELECT LAST_INSERT_ID();",p,t);}
        await Audit(c,t,"service_payments",p.Id,p.Voided?"void":"edit",old);
        await c.ExecuteAsync("UPDATE service_payments SET payment_date=@Date,amount=@Amount,method=@Method,notes=@Notes,voided=@Voided,version=version+1 WHERE id=@Id",p,t);return p.Id;
    }
}
