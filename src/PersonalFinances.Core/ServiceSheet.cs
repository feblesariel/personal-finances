namespace PersonalFinances.Core;

public sealed record ServiceSheetEntry(RecurringService Service,ServiceObligation? Obligation,List<ServiceRow> Legacy,DateTime Month,DateTime Today)
{
    public int ServiceId=>Service.Id;
    public string Name=>Service.Name;
    public string Currency=>Service.Type=="Tarjeta de crédito"?"ARS":Obligation?.Currency??Service.Currency;
    public bool RenewalRequired {get;init;}
    public DateTime? CoverageStart {get;init;}
    public DateTime? CoverageEnd {get;init;}
    public List<ServiceRow> History {get;init;}=[];
    public bool Applicable=>RenewalRequired||Obligation!=null||Service.Frequency=="Mensual"||Service.DueMonth==Month.Month;
    public bool Confirmed=>Obligation?.AmountConfirmed==true;
    public List<ServicePayment> Payments {get;init;}=[];
    public List<ServicePayment> RecordedPayments=>History.FirstOrDefault(r=>r.Obligation.Id==Obligation?.Id)?.Payments??Payments;
    public decimal RecordedPaid=>RecordedPayments.Where(p=>!p.Voided).Sum(p=>p.Amount);
    public decimal RecordedBalance=>Confirmed?(Obligation?.Amount??0)-RecordedPaid:0;
    public decimal Paid=>Payments.Where(p=>!p.Voided).Sum(p=>p.Amount);
    public decimal Balance=>Confirmed?(Obligation?.Amount??0)-Paid:0;
    public DateTime? PaymentDate=>Payments.Where(p=>!p.Voided).OrderByDescending(p=>p.Date).Select(p=>(DateTime?)p.Date).FirstOrDefault();
    public bool LegacyWarning=>Legacy.Any(r=>r.Obligation.Amount>0||r.Payments.Count>0||r.Obligation.Notes.Length>0||r.Obligation.Minimum is >0);
    public string Status=>!Applicable?"No corresponde":!Confirmed?"Sin cargar":Obligation!.Amount==0?"Sin cargo":Paid==0?"Pendiente":Balance==0?"Pagado":"Parcial";
    public bool Overdue=>Confirmed&&Balance>0&&Obligation?.Due is {} due&&due<Today.Date;
    public bool MinimumCovered=>Obligation?.Minimum is >0&&Paid>=Obligation.Minimum;
    public string Indicator=>string.Join(" · ",new[]{Overdue?"Vencido":"",MinimumCovered?"Mínimo cubierto":"",LegacyWarning?"Revisar USD anteriores":"",CoverageEnd is {} end?"Vigente hasta "+end.AddDays(-1).ToString("dd/MM/yyyy"):""}.Where(s=>s.Length>0));
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
            var history=ledger.Obligations.Where(o=>o.ServiceId==s.Id).Select(o=>new ServiceRow(s,o,ledger.Payments.Where(p=>p.ObligationId==o.Id).ToList(),today)).ToList();
            var matches=history.Where(r=>r.Obligation.Period==month).Select(r=>r.Obligation).ToList();
            var asOf=month==Month(today)?today.Date:month.AddMonths(1).AddDays(-1);
            var coverages=s.Frequency=="Anual"?history.Where(r=>(s.Type!="Tarjeta de crédito"||r.Obligation.Currency=="ARS")&&r.Obligation.AmountConfirmed&&r.Obligation.Amount>0&&r.Balance==0&&r.Paid>0).Select(r=>new{Row=r,Start=r.Payments.Where(p=>!p.Voided).Max(p=>p.Date)}).Where(c=>c.Start<=asOf).ToList():[];
            var covering=coverages.Where(c=>c.Start.AddYears(1)>asOf).OrderByDescending(c=>c.Start).FirstOrDefault();
            var outstanding=s.Frequency=="Anual"?history.Where(r=>r.Obligation.Period<=month&&r.Obligation.AmountConfirmed&&r.Obligation.Amount>r.Payments.Where(p=>!p.Voided&&p.Date<=asOf).Sum(p=>p.Amount)&&(s.Type!="Tarjeta de crédito"||r.Obligation.Currency=="ARS")).OrderByDescending(r=>r.Obligation.Period).FirstOrDefault():null;
            var renewal=s.Frequency=="Anual"&&covering==null&&(coverages.Count>0||outstanding!=null);
            if(!s.Active&&matches.Count==0&&covering==null)continue;
            if(matches.Count==0&&covering==null&&!renewal&&(s.Frequency=="Mensual"||s.DueMonth==month.Month))continue;
            var primary=covering?.Row.Obligation??(s.Type=="Tarjeta de crédito"?matches.FirstOrDefault(o=>o.Currency=="ARS"):matches.FirstOrDefault());
            if(covering==null&&outstanding!=null&&(primary==null||!primary.AmountConfirmed))primary=outstanding.Obligation;
            var legacy=history.Where(r=>r.Obligation.Period==month&&r.Obligation!=primary&&r.Obligation.Currency!=(s.Type=="Tarjeta de crédito"?"ARS":s.Currency)).ToList();
            rows.Add(new(s,primary,legacy,month,today){Payments=ledger.Payments.Where(p=>primary!=null&&p.ObligationId==primary.Id&&(s.Frequency!="Anual"||p.Date<=asOf)).ToList(),RenewalRequired=renewal,CoverageStart=covering?.Start,CoverageEnd=covering?.Start.AddYears(1),History=history});
        }        return rows;
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
