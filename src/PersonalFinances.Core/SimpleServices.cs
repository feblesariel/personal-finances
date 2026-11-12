using Dapper;
using MySqlConnector;

namespace PersonalFinances.Core;

public sealed record SimpleServiceEntry(RecurringService Service,ServiceObligation? Current,ServiceObligation? Source,DateTime Month,decimal? Paid,DateTime? PaymentDate,string Notes,string Currency,string Stamp)
{
    public int ServiceId=>Service.Id;
    public string Name=>Service.Name;
    public string Frequency=>Service.Frequency;
    public string Status=>Paid!=null&&PaymentDate!=null?"Pagado":"Pendiente";
}
public sealed record SimpleServiceEdit(RecurringService Service,DateTime Month,decimal? Paid,DateTime? PaymentDate,string Notes,string Currency,string ExpectedStamp);

public static class SimpleServices
{
    public static List<DateTime> VisibleMonths(ServiceLedger ledger,DateTime today)=>ledger.Months.Where(m=>!ledger.Obligations.Any(o=>o.Period==m)||ledger.Obligations.Any(o=>o.Period==m&&(o.SheetRecorded||ledger.Services.Single(s=>s.Id==o.ServiceId).Frequency=="Mensual"))).Append(ServiceSheet.Month(today)).Distinct().OrderByDescending(m=>m).ToList();
    public static List<SimpleServiceEntry> Rows(ServiceLedger ledger,DateTime month)
    {
        month=ServiceSheet.Month(month);var rows=new List<SimpleServiceEntry>();
        var paymentGroups=ledger.Payments.GroupBy(p=>p.ObligationId).ToDictionary(g=>g.Key,g=>g.ToList());
        foreach(var service in ledger.Services.OrderBy(s=>s.Id))
        {
            var history=ledger.Obligations.Where(o=>o.ServiceId==service.Id&&(service.Type!="Tarjeta de crédito"||o.Currency=="ARS")).ToList();
            List<ServicePayment> Payments(ServiceObligation o)=>paymentGroups.GetValueOrDefault(o.Id,[]).Where(p=>!p.Voided).ToList();
            DateTime? Date(ServiceObligation o)=>o.SheetRecorded?o.SheetDate:Payments(o).Select(p=>(DateTime?)p.Date).Max();
            var current=history.FirstOrDefault(o=>o.Period==month);
            if(!service.Active&&current==null)continue;
            ServiceObligation? source=current;
            if(service.Frequency=="Anual")
            {
                DateTime Effective(ServiceObligation o)=>o.SheetRecorded?o.Period:Date(o) is {} d&&ServiceSheet.Month(d)<o.Period?ServiceSheet.Month(d):o.Period;
                source=history.Where(o=>(o.SheetRecorded||Payments(o).Count>0)&&Effective(o)<=month).OrderByDescending(Effective).ThenByDescending(o=>Date(o)).ThenByDescending(o=>o.Id).FirstOrDefault()??current;
            }
            var payments=source==null?[]:Payments(source);
            decimal? paid=source?.SheetRecorded==true?source.SheetPaid:payments.Count>0?payments.Sum(p=>p.Amount):null;
            var date=source==null?null:Date(source);
            var notes=source?.SheetRecorded==true?source.SheetNotes??"":source==null?"":string.Join("\n",new[]{service.Notes,source.Notes}.Concat(payments.Select(p=>p.Notes)).Where(n=>!string.IsNullOrWhiteSpace(n)&&n!="Pago desde planilla mensual").Distinct());
            var currency=service.Type=="Tarjeta de crédito"?"ARS":source?.SheetRecorded==true?source.SheetCurrency!:source?.Currency??service.Currency;
            var stamp=$"{service.Version}|{current?.Id}:{current?.Version}|{source?.Id}:{source?.Version}|{ServiceSheet.PaymentStamp(source==null?[]:paymentGroups.GetValueOrDefault(source.Id,[]))}";
            rows.Add(new(service,current,source,month,paid,date,notes,currency,stamp));
        }
        return rows;
    }
    public static IEnumerable<SimpleServiceEntry> Order(IEnumerable<SimpleServiceEntry> rows,string sort)
    {
        IOrderedEnumerable<SimpleServiceEntry>? ordered=null;
        foreach(var part in sort.Split(';',StringSplitOptions.RemoveEmptyEntries))
        {
            var pair=part.Split(':');Func<SimpleServiceEntry,IComparable?> key=pair[0] switch{"Name"=>r=>r.Name,"Frequency"=>r=>r.Frequency,"PaidSort"=>r=>r.Paid,"PaymentDate"=>r=>r.PaymentDate,"Status"=>r=>r.Status,"Notes"=>r=>r.Notes,_=>r=>r.ServiceId};var desc=pair.ElementAtOrDefault(1)=="Descending";
            ordered=ordered==null?(desc?rows.OrderByDescending(key):rows.OrderBy(key)):(desc?ordered.ThenByDescending(key):ordered.ThenBy(key));
        }
        return ordered??rows.OrderBy(r=>r.ServiceId);
    }
}
public sealed partial class FinanceStore
{
    private static async Task MigrateSimpleServices(MySqlConnection c)
    {
        if(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM schema_migrations WHERE version=8")>0)return;
        if(!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='service_obligations' AND COLUMN_NAME='sheet_recorded')"))
            await c.ExecuteAsync("ALTER TABLE service_obligations ADD COLUMN sheet_recorded BOOLEAN NOT NULL DEFAULT 0, ADD COLUMN sheet_paid DECIMAL(18,2) NULL CHECK(sheet_paid>=0), ADD COLUMN sheet_date DATE NULL, ADD COLUMN sheet_notes TEXT NULL, ADD COLUMN sheet_currency CHAR(3) NULL CHECK(sheet_currency IN ('ARS','USD'))",commandTimeout:120);
        await using var t=await c.BeginTransactionAsync();
        await c.ExecuteScalarAsync<string>("SELECT `value` FROM settings WHERE `key`='revision' FOR UPDATE",transaction:t);
        await c.ExecuteAsync("UPDATE settings SET `value`=CAST(`value` AS UNSIGNED)+1 WHERE `key`='revision'; INSERT INTO schema_migrations VALUES(8,UTC_TIMESTAMP(6));",transaction:t);
        await t.CommitAsync();
    }
    public Task<int> CreateSimpleServiceMonthAsync(DateTime month,bool rejectDuplicate=true)=>Write(async(c,t)=>
    {
        month=ServiceSheet.Month(month);ServiceRules.Require(ServiceRules.DateValid(month),"Período inválido.");var ledger=await ReadServices(c,t);
        ServiceRules.Require(!rejectDuplicate||!ledger.Months.Contains(month),"Ese período ya existe. Seleccionalo en la lista.");
        await c.ExecuteAsync("INSERT IGNORE INTO service_months VALUES(@month)",new{month},t);var count=0;
        foreach(var service in ledger.Services.Where(s=>s.Active&&s.Frequency=="Mensual"))
        {
            if(ledger.Obligations.Any(o=>o.ServiceId==service.Id&&o.Period==month&&(service.Type!="Tarjeta de crédito"||o.Currency=="ARS")))continue;
            var currency=service.Type=="Tarjeta de crédito"?"ARS":service.Currency;
            var id=await SaveObligation(c,t,new(){ServiceId=service.Id,Period=month,Currency=currency,AmountConfirmed=false});
            await c.ExecuteAsync("UPDATE service_obligations SET sheet_recorded=1,sheet_notes='',sheet_currency=@currency WHERE id=@id",new{id,currency},t);count++;
        }
        return count;
    });
    public Task<int> SaveSimpleServiceAsync(SimpleServiceEdit edit)=>Write(async(c,t)=>
    {
        using var timing=OperationLog.Measure("service.simple.save");
        var month=ServiceSheet.Month(edit.Month);ServiceRules.Require(ServiceRules.DateValid(month),"Período inválido.");
        ServiceRules.Require(edit.Currency is "ARS" or "USD"&&edit.Notes!=null&&edit.Notes.Length<=4000,"Revisá la moneda y las observaciones.");
        ServiceRules.Require(edit.Paid==null||ServiceRules.MoneyValid(edit.Paid.Value)&&edit.PaymentDate is {} date&&ServiceRules.DateValid(date),"Completá un monto no negativo y una fecha de pago válida, o dejá el monto vacío.");
        var ledger=await ReadServices(c,t);var existing=edit.Service.Id==0?null:SimpleServices.Rows(ledger,month).SingleOrDefault(r=>r.ServiceId==edit.Service.Id);
        ServiceRules.Require(edit.Service.Id==0||existing!=null&&existing.Stamp==edit.ExpectedStamp,"El servicio cambió. Volvé a abrirlo antes de guardar.");
        ServiceRules.Require(edit.Service.Type!="Tarjeta de crédito"||edit.Currency=="ARS","Los resúmenes de tarjetas se registran en ARS.");
        var service=edit.Service with{Currency=edit.Currency};var id=await SaveService(c,t,service);
        var obligation=existing?.Current;
        if(obligation==null){var created=new ServiceObligation{ServiceId=id,Period=month,Currency=edit.Currency,AmountConfirmed=false};created.Id=await SaveObligation(c,t,created);obligation=created;}
        await Audit(c,t,"service_obligations",obligation.Id,"sheet",obligation);
        await c.ExecuteAsync("UPDATE service_obligations SET sheet_recorded=1,sheet_paid=@Paid,sheet_date=@PaymentDate,sheet_notes=@Notes,sheet_currency=@Currency,version=version+1 WHERE id=@Id",new{obligation.Id,edit.Paid,PaymentDate=edit.Paid==null?null:edit.PaymentDate,edit.Notes,edit.Currency},t);
        return id;
    });
}
