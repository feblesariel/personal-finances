using PersonalFinances.Core;

namespace PersonalFinances.App;
public sealed class ServiceEditor : EditorViewModel
{
    private readonly FinanceStore store;
    public RecurringService Model {get;}
    public string Amount {get;set;}
    public string AmountUsd {get;set;}="";
    public bool IsCard=>Type=="Tarjeta de crédito";
    public bool SingleCurrency=>!IsCard;
    public bool IsAnnual=>Frequency=="Anual";
    public string Frequency {get=>Model.Frequency;set{Model.Frequency=value;Changed();Changed(nameof(IsAnnual));}}
    public string AmountLabel=>IsCard?"Importe habitual en ARS (opcional)":"Importe habitual (opcional)";
    public string Type {get=>Model.Type;set{Model.Type=value;Changed();Changed(nameof(IsCard));Changed(nameof(SingleCurrency));Changed(nameof(AmountLabel));}}
    public string[] Types=>ServiceRules.Types;
    public string[] Frequencies {get;}=["Mensual","Anual"];
    public string[] Currencies {get;}=["ARS","USD"];

    public ServiceEditor(FinanceStore store,Snapshot snapshot,RecurringService? model=null)
    {
        this.store=store;Model=model is null?new():model with{};
        AmountUsd=Model.UsualAmountUsd?.ToString("N2",Money.Ar)??"";Amount=Model.UsualAmount?.ToString("N2",Money.Ar)??"";Title=model==null?"Agregar servicio":"Editar servicio";Subtitle="Lo cargás una sola vez. Cada mes conserva sus importes y pagos; desactivar conserva el historial.";
    }
    public static decimal ParseZero(string text)=>decimal.TryParse(text,System.Globalization.NumberStyles.Number,Money.Ar,out var n)&&n==0?0:Money.Parse(text);
    protected override async Task Save(){if(IsCard)Model.Currency="ARS";Model.UsualAmount=string.IsNullOrWhiteSpace(Amount)?null:ParseZero(Amount);var wasNew=Model.Id==0;Model.Id=await store.SaveServiceAsync(Model);if(!wasNew)Model.Version++;Close();}
}
public sealed class ObligationEditor : EditorViewModel
{
    private readonly FinanceStore store;
    public ServiceObligation Model {get;}
    public string Amount {get;set;}
    public string Minimum {get;set;}
    public bool NewPeriod=>Model.Id==0;
    public bool DualCurrency=>IsCard&&NewPeriod;
    public string AmountUsd {get;set;}="0,00";
    public string AmountLabel=>DualCurrency?"Importe del resumen en ARS":"Importe exigible (0 = Bonificado)";
    public bool IsCard {get;}
    public string Installment {get;set;}
    public string Installments {get;set;}
    public ObligationEditor(FinanceStore store,RecurringService service,ServiceObligation model)
    {
        this.store=store;Model=model with{};IsCard=service.Type=="Tarjeta de crédito";if(DualCurrency)Model.Currency="ARS";AmountUsd=(service.UsualAmountUsd??0).ToString("N2",Money.Ar);Amount=Model.AmountConfirmed?Model.Amount.ToString("N2",Money.Ar):"";Minimum=Model.Minimum?.ToString("N2",Money.Ar)??"";Installment=Model.Installment?.ToString()??"";Installments=Model.Installments?.ToString()??"";
        Title=(NewPeriod?"Generar período · ":"Editar período · ")+service.Name;Subtitle="Confirmá el importe real y el vencimiento. El período identifica el mes (o año) de la obligación.";
    }
    protected override async Task Save()
    {
        if(NewPeriod)Model.Period=new(Model.Period.Year,Model.Period.Month,1);
        Model.AmountConfirmed=!string.IsNullOrWhiteSpace(Amount);Model.Amount=Model.AmountConfirmed?ServiceEditor.ParseZero(Amount):0;Model.Minimum=string.IsNullOrWhiteSpace(Minimum)?null:ServiceEditor.ParseZero(Minimum);
        Model.Installment=ParseInt(Installment);Model.Installments=ParseInt(Installments);
        await store.SaveServicePeriodAsync(Model,DualCurrency?ServiceEditor.ParseZero(AmountUsd):null);Close();
    }
    private static int? ParseInt(string s)=>string.IsNullOrWhiteSpace(s)?null:int.TryParse(s,out var n)?n:throw new RuleException("Ingresá un número de cuota válido.");
}
public sealed class ServicePaymentEditor : EditorViewModel
{
    private readonly FinanceStore store;
    public ServicePayment Model {get;}
    public string Amount {get;set;}
    public string Currency {get;}
    public string[] Methods=>ServiceRules.Methods;
    public ServicePaymentEditor(FinanceStore store,ServiceRow row,ServicePayment? payment=null)
    {
        this.store=store;Model=payment is null?new(){ObligationId=row.Obligation.Id}:payment with{};Amount=(payment?.Amount??row.Balance).ToString("N2",Money.Ar);Currency=row.Obligation.Currency;
        Title=payment==null?"Registrar pago":"Corregir pago";Subtitle=$"{row.Name} · {row.PeriodLabel}\nSaldo: {row.BalanceLabel} · Moneda: {Currency}";
    }
    protected override async Task Save(){Model.Amount=Money.Parse(Amount);await store.SaveServicePaymentAsync(Model);Close();}
}
