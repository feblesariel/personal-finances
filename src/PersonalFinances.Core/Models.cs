using System.Globalization;
using System.Text.RegularExpressions;

namespace PersonalFinances.Core;

public static partial class Money
{
    public static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");
    [GeneratedRegex(@"^\d{1,16}(,\d{1,2})?$|^\d{1,3}(\.\d{3})+(,\d{1,2})?$")] private static partial Regex AmountPattern();
    public static decimal Parse(string text)
    {
        text = text.Trim();
        if (!AmountPattern().IsMatch(text) || !decimal.TryParse(text, NumberStyles.Number, Ar, out var n) || n <= 0 || n > 9999999999999999.99m)
            throw new RuleException("Ingresá un importe positivo, con coma decimal y hasta dos decimales. Ejemplo: 10.699,00.");
        return n;
    }
    public static string Format(decimal value, string currency = "ARS") => (currency == "USD" ? "US$ " : "$ ") + value.ToString("N2", Ar);
}
public sealed class RuleException(string message) : Exception(message);
public sealed class Card { public int Id {get;set;} public string Name {get;set;} = ""; public bool Active {get;set;} = true; public override string ToString()=>Name; }
public sealed class Category { public int Id {get;set;} public string Name {get;set;} = ""; public bool Active {get;set;} = true; public override string ToString()=>Name; }
public sealed class Period
{
    public int Id {get;set;} public int CardId {get;set;} public string Name {get;set;}="";
    public DateTime Start {get;set;} public DateTime Closing {get;set;} public DateTime Due {get;set;}
    public override string ToString()=>Name;
}
public sealed class Movement
{
    public long Id {get;set;} public string Token {get;set;}=Guid.NewGuid().ToString("N"); public int CardId {get;set;}
    public int? PeriodId {get;set;} public bool ManualPeriod {get;set;} public int CategoryId {get;set;}
    public DateTime Date {get;set;}=DateTime.Today; public decimal Amount {get;set;} public string Currency {get;set;}="ARS";
    public bool Refund {get;set;} public long? OriginalId {get;set;} public string Description {get;set;}=""; public string Notes {get;set;}="";
    public int Version {get;set;} public string CardName {get;set;}=""; public string CategoryName {get;set;}=""; public string? PeriodName {get;set;}
    public decimal Signed => Refund ? -Amount : Amount;
    public string DisplayAmount => Money.Format(Signed, Currency);
    public string TypeLabel => Refund ? "Reintegro" : "Consumo";
    public string OriginalLabel => $"#{Id} · {Date:dd/MM/yyyy} · {Description} · {DisplayAmount}";
    public string Assignment => PeriodId == null ? "Sin período · requiere atención" : (ManualPeriod ? PeriodName + " · manual" : PeriodName ?? "");
    public Movement Copy() => (Movement)MemberwiseClone();
}
public sealed record Snapshot(List<Card> Cards, List<Category> Categories, List<Period> Periods, List<Movement> Movements, Dictionary<string,string> Settings)
{
    public ServiceLedger Services {get;init;} = ServiceLedger.Empty;
    public long Revision => long.Parse(Settings.GetValueOrDefault("revision","0"));
    public Period? Current(int cardId) => Periods.Where(p=>p.CardId==cardId).OrderBy(p=>p.Start).FirstOrDefault(p=>p.Start<=DateTime.Today && p.Closing>=DateTime.Today)
        ?? Periods.Where(p=>p.CardId==cardId && p.Closing<DateTime.Today).OrderByDescending(p=>p.Closing).FirstOrDefault()
        ?? Periods.Where(p=>p.CardId==cardId).OrderBy(p=>p.Start).FirstOrDefault();
}
public static class PeriodRules
{
    public static void Validate(Period p, IEnumerable<Period> existing)
    {
        if(string.IsNullOrWhiteSpace(p.Name) || p.Name.Length>100) throw new RuleException("Indicá un nombre de período de hasta 100 caracteres.");
        if(p.Start.Date>p.Closing.Date) throw new RuleException("El inicio debe ser anterior o igual al cierre.");
        if(p.Due.Date<p.Closing.Date) throw new RuleException("El vencimiento no puede ser anterior al cierre.");
        if(existing.Any(x=>x.Id!=p.Id && x.CardId==p.CardId && x.Start<=p.Closing.Date && x.Closing>=p.Start.Date))
            throw new RuleException("Estas fechas se superponen con otro período de la tarjeta. Ajustá inicio o cierre.");
    }
    public static int? Assign(IEnumerable<Period> periods,int cardId,DateTime date) => periods.SingleOrDefault(p=>p.CardId==cardId && p.Start<=date.Date && p.Closing>=date.Date)?.Id;
}
