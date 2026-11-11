using System.Text.Json.Serialization;

namespace PersonalFinances.Core;

public sealed record RecurringService
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Servicio";
    public string Frequency { get; set; } = "Mensual";
    public string Currency { get; set; } = "ARS";
    public decimal? UsualAmount { get; set; }
    public int? DueDay { get; set; }
    public decimal? UsualAmountUsd {get;set;}
    public int DueMonth { get; set; } = 1;
    public string Notes { get; set; } = "";
    public bool Active { get; set; } = true;
    public int? CardId { get; set; }
    public int Version { get; set; } = 1;
    public override string ToString() => Name + (Active ? "" : " · Inactivo");
}
public sealed record ServiceObligation
{
    public long Id { get; set; }
    public int ServiceId { get; set; }
    public DateTime Period { get; set; }
    public DateTime? Due { get; set; }
    public bool AmountConfirmed {get;set;}=true;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ARS";
    public decimal? Minimum { get; set; }
    public int? Installment { get; set; }
    public int? Installments { get; set; }
    public string Notes { get; set; } = "";
    public int Version { get; set; } = 1;
}
public sealed record ServicePayment
{
    public long Id { get; set; }
    public string Token { get; set; } = Guid.NewGuid().ToString("N");
    public long ObligationId { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public decimal Amount { get; set; }
    public string Method { get; set; } = "Transferencia";
    public string Notes { get; set; } = "";
    public bool Voided { get; set; }
    public int Version { get; set; } = 1;
}
public sealed record ServiceLedger(List<RecurringService> Services,List<ServiceObligation> Obligations,List<ServicePayment> Payments)
{
    public List<DateTime> Months {get;init;}=[];
    public static ServiceLedger Empty => new([],[],[]);
    public IEnumerable<ServiceRow> Rows(DateTime today) => Obligations.Select(o => new ServiceRow(Services.Single(s=>s.Id==o.ServiceId),o,Payments.Where(p=>p.ObligationId==o.Id).ToList(),today));
}
public sealed record ServiceRow(RecurringService Service,ServiceObligation Obligation,List<ServicePayment> Payments,DateTime Today)
{
    public string Name => Service.Name+(Service.Type=="Tarjeta de crédito"?" · "+Obligation.Currency:"");
    public string DueLabel=>Obligation.Due?.ToString("dd/MM/yyyy")??"Sin definir";
    public string PeriodLabel => Obligation.Period.ToString(Service.Frequency=="Anual"?"yyyy":"MMMM yyyy",Money.Ar);
    public decimal Paid => Payments.Where(p=>!p.Voided).Sum(p=>p.Amount);
    public decimal Balance => Obligation.Amount-Paid;
    public bool Overdue => Obligation.AmountConfirmed&&Balance>0 && Obligation.Due is {} due && due.Date<Today.Date;
    public string BaseStatus => !Obligation.AmountConfirmed?"Sin cargar":Obligation.Amount==0?"Sin cargo":Balance==0?"Pagado":Paid>0?"Pago parcial":"Pendiente";
    public string Status => BaseStatus+(Overdue?" · Vencido":"")+(Obligation.Minimum is >0 && Paid>=Obligation.Minimum?" · Mínimo cubierto":"");
    public string AmountLabel => Money.Format(Obligation.Amount,Obligation.Currency);
    public string PaidLabel => Money.Format(Paid,Obligation.Currency);
    public string BalanceLabel => Money.Format(Balance,Obligation.Currency);
    public string Detail => $"{Name} · {PeriodLabel} · {Status}\n{Obligation.Notes}\n"+(Obligation.Installment.HasValue?$"Cuota {Obligation.Installment} de {Obligation.Installments}":"");
}
public static class ServiceRules
{
    public static readonly string[] Types=["Servicio","Impuesto","Suscripción","Educación","Seguro","Tarjeta de crédito","Otro"];
    public static readonly string[] Methods=["Efectivo","Transferencia","VISA","MASTERCARD","Otro"];
    public static void Require(bool ok,string message) { if(!ok)throw new RuleException(message); }
    public static bool MoneyValid(decimal n)=>n>=0&&n<=9999999999999999.99m&&decimal.Round(n,2)==n;
    public static bool DateValid(DateTime d)=>d.Year>=1000&&d.TimeOfDay==TimeSpan.Zero;
    public static void Validate(RecurringService s)
    {
        Require(!string.IsNullOrWhiteSpace(s.Name)&&s.Name.Length<=100&&Types.Contains(s.Type),"Nombre o tipo de servicio inválido.");
        Require(s.Frequency is "Mensual" or "Anual"&&s.Currency is "ARS" or "USD"&&(s.DueDay==null||s.DueDay is >=1 and <=31)&&s.DueMonth is >=1 and <=12,"Periodicidad, moneda o vencimiento inválido.");
        Require((s.UsualAmountUsd==null||s.Type=="Tarjeta de crédito"&&MoneyValid(s.UsualAmountUsd.Value))&&(s.UsualAmount==null||MoneyValid(s.UsualAmount.Value))&&s.Notes!=null&&s.Notes.Length<=4000&&s.Version>0,"Importe habitual u observaciones inválidos.");
    }
    public static void Validate(ServiceObligation o,RecurringService s,decimal paid)
    {
        Require(DateValid(o.Period)&&o.Period.Day==1&&(o.Due==null||DateValid(o.Due.Value))&&MoneyValid(o.Amount)&&o.Amount>=paid&&o.Currency is "ARS" or "USD","Fecha o importe inválido. El total debe cubrir los pagos existentes.");
        Require(o.AmountConfirmed||o.Amount==0&&paid==0,"Un importe sin cargar no puede tener cargos ni pagos.");
        Require(o.Minimum==null||s.Type=="Tarjeta de crédito"&&MoneyValid(o.Minimum.Value)&&o.Minimum<=o.Amount,"El mínimo debe corresponder a una tarjeta y no superar el total.");
        Require((o.Installment==null&&o.Installments==null)||(o.Installment is >0&&o.Installments>=o.Installment),"Completá número y total de cuotas válidos.");
        Require(o.Notes!=null&&o.Notes.Length<=4000&&o.Version>0,"Observaciones inválidas.");
    }
    public static ServiceObligation Propose(RecurringService s,IEnumerable<ServiceObligation> existing,DateTime today)
    {
        Require(s.Active,"El servicio está inactivo.");
        var last=existing.Where(o=>o.ServiceId==s.Id).OrderByDescending(o=>o.Period).FirstOrDefault();
        var period=last==null?new DateTime(today.Year,s.Frequency=="Anual"?s.DueMonth:today.Month,1):s.Frequency=="Anual"?last.Period.AddYears(1):last.Period.AddMonths(1);
        if(s.Frequency=="Anual")period=new(period.Year,s.DueMonth,1);
        var month=s.Frequency=="Anual"?s.DueMonth:period.Month;
        return new(){ServiceId=s.Id,Period=period,Due=s.DueDay is {} day?new DateTime(period.Year,month,Math.Min(day,DateTime.DaysInMonth(period.Year,month))):null,Amount=s.UsualAmount??0,Currency=s.Currency};
    }
}
