using PersonalFinances.Core;
namespace PersonalFinances.App;
public sealed class ServiceSheetRow(SimpleServiceEntry entry)
{
    public SimpleServiceEntry Entry {get;}=entry;
    public int ServiceId=>Entry.ServiceId;
    public string Name=>Entry.Name;
    public string Currency=>Entry.Currency;
    public string Frequency=>Entry.Frequency;
    public string PaidDisplay=>Entry.Paid is {} paid?Money.Format(paid,Currency):"—";
    public decimal? PaidSort=>Entry.Paid;
    public DateTime? PaymentDate=>Entry.PaymentDate;
    public string Notes=>Entry.Notes;
    public string Status=>Entry.Status.ToUpper(Money.Ar);
    public string BaseStatus=>Entry.Status;
}
