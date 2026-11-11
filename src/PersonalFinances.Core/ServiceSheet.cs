namespace PersonalFinances.Core;

public sealed record ServiceSheetEntry(RecurringService Service,ServiceObligation? Obligation,List<ServiceRow> Legacy,DateTime Month,DateTime Today)
{
    public int ServiceId=>Service.Id;
    public string Name=>Service.Name;
    public string Currency=>Service.Type=="Tarjeta de crédito"?"ARS":Obligation?.Currency??Service.Currency;
    public bool Applicable=>Obligation!=null||Service.Frequency=="Mensual"||Service.DueMonth==Month.Month;
    public bool Confirmed=>Obligation?.AmountConfirmed==true;
    public List<ServicePayment> Payments {get;init;}=[];
    public decimal Paid=>Payments.Where(p=>!p.Voided).Sum(p=>p.Amount);
    public decimal Balance=>Confirmed?(Obligation?.Amount??0)-Paid:0;
    public DateTime? PaymentDate=>Payments.Where(p=>!p.Voided).OrderByDescending(p=>p.Date).Select(p=>(DateTime?)p.Date).FirstOrDefault();
    public bool LegacyWarning=>Legacy.Any(r=>r.Obligation.Amount>0||r.Payments.Count>0||r.Obligation.Notes.Length>0||r.Obligation.Minimum is >0);
    public string Status=>!Applicable?"No corresponde":!Confirmed?"Sin cargar":Obligation!.Amount==0?"Bonificado":Paid==0?"Pendiente":Balance==0?"Pagado":"Parcial";
    public bool Overdue=>Confirmed&&Balance>0&&Obligation?.Due is {} due&&due<Today.Date;
    public bool MinimumCovered=>Obligation?.Minimum is >0&&Paid>=Obligation.Minimum;
    public string Indicator=>string.Join(" · ",new[]{Overdue?"Vencido":"",MinimumCovered?"Mínimo cubierto":"",LegacyWarning?"Revisar USD anteriores":""}.Where(s=>s.Length>0));
}
public static class ServiceSheet
{
    public static DateTime Month(DateTime d)=>new(d.Year,d.Month,1);
    public static string PaymentStamp(IEnumerable<ServicePayment> payments)=>string.Join(";",payments.OrderBy(p=>p.Id).Select(p=>$"{p.Id}:{p.Version}:{p.Voided}"));
    public static List<ServiceSheetEntry> Rows(ServiceLedger ledger,DateTime month,DateTime today)
    {
        month=Month(month);
        var rows=new List<ServiceSheetEntry>();
        foreach(var s in ledger.Services.OrderBy(s=>s.Id))
        {
            var matches=ledger.Obligations.Where(o=>o.ServiceId==s.Id&&o.Period==month).ToList();
            if(!s.Active&&matches.Count==0)continue;
            if(matches.Count==0&&(s.Frequency=="Mensual"||s.DueMonth==month.Month))continue;
            var primary=s.Type=="Tarjeta de crédito"?matches.FirstOrDefault(o=>o.Currency=="ARS"):matches.FirstOrDefault();
            var legacy=matches.Where(o=>o!=primary).Select(o=>new ServiceRow(s,o,ledger.Payments.Where(p=>p.ObligationId==o.Id).ToList(),today)).ToList();
            rows.Add(new(s,primary,legacy,month,today){Payments=ledger.Payments.Where(p=>primary!=null&&p.ObligationId==primary.Id).ToList()});
        }
        return rows;
    }
    public static IEnumerable<ServiceSheetEntry> Order(IEnumerable<ServiceSheetEntry> rows,string sort)
    {
        IOrderedEnumerable<ServiceSheetEntry>? result=null;
        foreach(var part in sort.Split(';',StringSplitOptions.RemoveEmptyEntries))
        {
            var pair=part.Split(':');Func<ServiceSheetEntry,IComparable?> key=pair[0] switch
            {
                "Name"=>r=>r.Name,"AmountSort"=>r=>r.Confirmed?r.Obligation?.Amount:null,"PaidSort"=>r=>r.Paid,
                "PaymentDate"=>r=>r.PaymentDate,"Due"=>r=>r.Obligation?.Due,"Status"=>r=>r.Status,"Notes"=>r=>r.Obligation?.Notes,
                _=>r=>r.ServiceId
            };
            var desc=pair.ElementAtOrDefault(1)=="Descending";
            result=result==null?(desc?rows.OrderByDescending(key):rows.OrderBy(key)):(desc?result.ThenByDescending(key):result.ThenBy(key));
        }
        return result??rows.OrderBy(r=>r.ServiceId);
    }
}
public sealed record ServiceSheetEdit(ServiceObligation Obligation,decimal TotalPaid,DateTime? PaymentDate,string ExpectedPayments,string Token);
