using System.ComponentModel;
using PersonalFinances.Core;

namespace PersonalFinances.App;
public sealed class ServiceSheetRow : Observable,IEditableObject
{
    public ServiceSheetEntry Entry {get;private set;}
    private string amount="",paid="",notes="";private DateTime? date,due;private bool loading,editing,dateTouched;
    private (string Amount,string Paid,DateTime? Date,DateTime? Due,string Notes) original;
    public string Token {get;private set;}=Guid.NewGuid().ToString("N");
    public int ServiceId=>Entry.ServiceId;
    public string Name=>Entry.Name;
    public string Currency=>Entry.Currency;
    public bool Applicable=>Entry.Applicable;
    public string AmountText {get=>amount;set{Set(ref amount,value);}}
    public string PaidText {get=>paid;set{if(Set(ref paid,value)&&editing&&!loading&&!dateTouched&&TryAmount(value)>Entry.Paid){Set(ref date,DateTime.Today,nameof(PaymentDate));}}}
    public DateTime? PaymentDate {get=>date;set{if(Set(ref date,value)&&!loading)dateTouched=true;}}
    public DateTime? Due {get=>due;set=>Set(ref due,value);}
    public string Notes {get=>notes;set=>Set(ref notes,value);}
    public string AmountDisplay=>Entry.Confirmed?Money.Format(Entry.Obligation!.Amount,Currency):Applicable?(Currency=="USD"?"US$ —":"$ —"):"No corresponde";
    public string PaidDisplay=>Entry.Paid>0?Money.Format(Entry.Paid,Currency):"—";
    public decimal? AmountSort=>Entry.Confirmed?Entry.Obligation?.Amount:null;
    public decimal PaidSort=>Entry.Paid;
    public string Status=>Entry.Status.ToUpper(Money.Ar);
    public string BaseStatus=>Entry.Status;
    public bool Overdue=>Entry.Overdue;
    public string Indicator=>Entry.Indicator;
    public string Suggestion=>Entry.Service.UsualAmount is {} a?"Referencia habitual: "+Money.Format(a,Currency)+". Confirmá el importe de este mes.":"Dejá vacío si todavía no llegó el importe. Escribir 0 confirma la bonificación.";
    public bool Dirty=>editing&&original!=(amount,paid,date,due,notes);
    public ServiceSheetRow(ServiceSheetEntry entry){Entry=entry;Apply(entry);}
    public void Apply(ServiceSheetEntry entry)
    {
        loading=true;Entry=entry;amount=entry.Confirmed?entry.Obligation!.Amount.ToString("N2",Money.Ar):"";paid=entry.Paid>0?entry.Paid.ToString("N2",Money.Ar):"";date=entry.PaymentDate;due=entry.Obligation?.Due;notes=entry.Obligation?.Notes??"";editing=false;loading=false;
        foreach(var name in new[]{nameof(AmountText),nameof(PaidText),nameof(PaymentDate),nameof(Due),nameof(Notes),nameof(AmountDisplay),nameof(PaidDisplay),nameof(Status),nameof(BaseStatus),nameof(Indicator),nameof(Overdue),nameof(AmountSort),nameof(PaidSort)})Changed(name);
    }
    public void BeginEdit(){if(editing)return;original=(amount,paid,date,due,notes);editing=true;dateTouched=false;Token=Guid.NewGuid().ToString("N");}
    public void EndEdit(){editing=false;}
    public void CancelEdit(){if(!editing)return;loading=true;(amount,paid,date,due,notes)=original;loading=false;editing=false;foreach(var n in new[]{nameof(AmountText),nameof(PaidText),nameof(PaymentDate),nameof(Due),nameof(Notes)})Changed(n);}
    public ServiceSheetEdit BuildEdit()
    {
        if(!Applicable)throw new RuleException("Este servicio anual no corresponde a este mes.");
        var confirmed=!string.IsNullOrWhiteSpace(AmountText);
        var model=Entry.Obligation is {} o?o with{}:new ServiceObligation{ServiceId=ServiceId,Period=Entry.Month,Currency=Currency};
        model.Amount=confirmed?ServiceEditor.ParseZero(AmountText):0;model.AmountConfirmed=confirmed;model.Due=Due?.Date;model.Notes=Notes;
        return new(model,string.IsNullOrWhiteSpace(PaidText)?0:ServiceEditor.ParseZero(PaidText),PaymentDate?.Date,ServiceSheet.PaymentStamp(Entry.Payments),Token);
    }
    private static decimal TryAmount(string s)=>decimal.TryParse(s,System.Globalization.NumberStyles.Number,Money.Ar,out var n)?n:0;
}
