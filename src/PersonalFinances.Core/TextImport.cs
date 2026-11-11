using System.Globalization;
using System.Text.RegularExpressions;

namespace PersonalFinances.Core;
public sealed class ImportRow
{
    public bool Include {get;set;} public int Line {get;set;} public string Raw {get;set;}=""; public Movement? Movement {get;set;}
    public string Problem {get;set;}=""; public bool Duplicate {get;set;} public bool Reviewed {get;set;}
    public string Date => Movement?.Date.ToString("dd/MM/yyyy")??"—";
    public string Card=>Movement?.CardName??"—";public string Description=>Movement?.Description??Raw;
    public string Category=>Movement?.CategoryName??"—";public string Amount=>Movement?.DisplayAmount??"—";
}
public static partial class TextImport
{
    [GeneratedRegex(@"^\s*(?<date>\d{2}/\d{2}/\d{4})\s*---\s*(?<desc>.*?)\s*---\s*(?<amount>.*?)\s*<--\s*(?<tail>.+?)\s*$",RegexOptions.IgnoreCase)] private static partial Regex LinePattern();
    public static List<ImportRow> Parse(IEnumerable<string> lines,Snapshot s)
    {
        var result=new List<ImportRow>();var lineNo=0;var seen=new HashSet<string>();
        foreach(var raw in lines)
        {
            lineNo++; if(string.IsNullOrWhiteSpace(raw)||raw.TrimStart().StartsWith("---")||raw.TrimStart().StartsWith("==="))continue;
            var row=new ImportRow{Line=lineNo,Raw=raw}; result.Add(row);var match=LinePattern().Match(raw);
            if(!match.Success) { row.Problem="Formato no reconocido · revisar o excluir";continue; }
            if(!DateTime.TryParseExact(match.Groups["date"].Value,"dd/MM/yyyy",Money.Ar,DateTimeStyles.None,out var date)){row.Problem="Fecha inválida";continue;}
            var tail=match.Groups["tail"].Value;var card=s.Cards.OrderByDescending(c=>c.Name.Length).FirstOrDefault(c=>StartsCard(tail,c.Name))
                ?? s.Cards.FirstOrDefault(c=>c.Name.Equals("MASTERCARD",StringComparison.OrdinalIgnoreCase)&&StartsCard(tail,"MASTER"));
            if(card==null){row.Problem="Tarjeta no reconocida · revisar";continue;}
            var token=StartsCard(tail,card.Name)?card.Name:"MASTER"; var notes=tail[token.Length..].Trim().TrimStart('-','>',' ');
            var refund=Regex.IsMatch(notes,@"\b(REFUND|REINTEGRO|DEVOLUCI[ÓO]N)\b",RegexOptions.IgnoreCase);
            var amountText=match.Groups["amount"].Value.Trim();var currency=amountText.Contains("US$",StringComparison.OrdinalIgnoreCase)||amountText.Contains("USD",StringComparison.OrdinalIgnoreCase)?"USD":"ARS";
            amountText=Regex.Replace(amountText,@"(?i)US\$|USD|ARS|\$","").Trim();
            decimal amount;try{amount=Money.Parse(amountText);}catch(RuleException){row.Problem="Importe ambiguo o inválido · revisar";continue;}
            var desc=match.Groups["desc"].Value.Trim();var category=Suggest(desc,s.Categories);
            var m=new Movement{Date=date,Amount=amount,Currency=currency,CardId=card.Id,CardName=card.Name,CategoryId=category.Id,CategoryName=category.Name,Description=desc,Notes=notes,Refund=refund,PeriodId=PeriodRules.Assign(s.Periods,card.Id,date)};row.Movement=m;
            var signature=$"{date:yyyyMMdd}|{card.Id}|{currency}|{amount}|{refund}|{desc.ToUpperInvariant()}";
            row.Duplicate=!seen.Add(signature)||s.Movements.Any(x=>x.Date==date&&x.CardId==card.Id&&x.Currency==currency&&x.Amount==amount&&x.Refund==refund&&x.Description.Equals(desc,StringComparison.OrdinalIgnoreCase));
            row.Problem=row.Duplicate?"Posible duplicado · requiere revisión":m.PeriodId==null?"Sin período · se conservará pendiente":"Categoría sugerida · revisar antes de importar";
            row.Include=!row.Duplicate && card.Active;
        }
        return result;
    }
    private static bool StartsCard(string text,string name)=>text.StartsWith(name,StringComparison.OrdinalIgnoreCase)&&(text.Length==name.Length||!char.IsLetterOrDigit(text[name.Length]));
    private static Category Suggest(string text,List<Category> categories)
    {
        var t=text.ToUpperInvariant();var key=t.Contains("COTO")||t.Contains("SUPER")||t.Contains("CARREFOUR")?"Supermercado":t.Contains("NAFTA")||t.Contains("SHELL")||t.Contains("YPF")?"Combustible":t.Contains("DELIVERY")||t.Contains("PEDIDOS")||t.Contains("RAPPI")?"Delivery":t.Contains("REGALO")?"Regalos":t.Contains("FARMAC")||t.Contains("SALUD")?"Salud":t.Contains("NETFLIX")||t.Contains("SPOTIFY")?"Suscripciones":t.Contains("ESTACIONAMIENTO")?"Estacionamiento":t.Contains("RESTAUR")||t.Contains("MCDONALD")?"Salidas":"Otros";
        return categories.FirstOrDefault(c=>c.Active&&c.Name.StartsWith(key,StringComparison.OrdinalIgnoreCase))??categories.First(c=>c.Active);
    }
}
