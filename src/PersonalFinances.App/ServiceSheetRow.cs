using PersonalFinances.Core;
namespace PersonalFinances.App;
public sealed class ServiceSheetRow(ServiceSheetEntry entry)
{
    public ServiceSheetEntry Entry {get;}=entry;
    public int ServiceId=>Entry.ServiceId;
    public string Name=>Entry.Name;
    public string Currency=>Entry.Currency;
    public string AmountDisplay=>Entry.Confirmed?Money.Format(Entry.Obligation!.Amount,Currency):Entry.Applicable?(Currency=="USD"?"US$ —":"$ —"):"No corresponde";
    public string PaidDisplay=>Entry.Paid>0?Money.Format(Entry.Paid,Currency):"—";
    public decimal? AmountSort=>Entry.Confirmed?Entry.Obligation?.Amount:null;
    public decimal PaidSort=>Entry.Paid;
    public DateTime? PaymentDate=>Entry.PaymentDate;
    public DateTime? Due=>Entry.Obligation?.Due;
    public string Notes=>Entry.Obligation?.Notes??"";
    public string Status=>Entry.Status.ToUpper(Money.Ar);
    public string BaseStatus=>Entry.Status;
    public string Indicator=>Entry.Indicator;
}