using System.Collections.ObjectModel;
using System.Windows.Input;
using PersonalFinances.Core;
namespace PersonalFinances.App;
public sealed class ServiceMonthEditor : EditorViewModel
{
    private readonly FinanceStore store;
    public DateTime Month {get;set;}
    public ServiceMonthEditor(FinanceStore store,DateTime proposed){this.store=store;Month=proposed;Title="Nuevo período";Subtitle="Se conserva la lista de servicios. Los importes y pagos comienzan vacíos.";SaveLabel="Crear período";}
    protected override async Task Save(){Month=ServiceSheet.Month(Month);await store.CreateServiceMonthAsync(Month);Close();}
}
public sealed record SheetPaymentItem(ServicePayment Payment,string Currency)
{
    public string Amount=>Money.Format(Payment.Amount,Currency);
}
public sealed class ServicePaymentsEditor : EditorViewModel
{
    private readonly FinanceStore store;private ServiceSheetEntry entry;private readonly Func<Task> changed;private readonly IDialogs dialogs;
    public ObservableCollection<SheetPaymentItem> Payments {get;}=[];
    public SheetPaymentItem? Selected {get;set;}
    public string Archive=>string.Join("\n",entry.Legacy.Select(r=>$"Registro anterior {r.Obligation.Currency}: total {r.AmountLabel} · abonado {r.PaidLabel} · pendiente {r.BalanceLabel}\n{r.Obligation.Notes}"));
    public ICommand AddCommand {get;}public ICommand EditCommand {get;}public ICommand VoidCommand {get;}
    public ServicePaymentsEditor(FinanceStore store,ServiceSheetEntry entry,Func<Task> changed,IDialogs dialogs)
    {
        this.store=store;this.entry=entry;this.changed=changed;this.dialogs=dialogs;Title="Pagos · "+entry.Name;Subtitle=entry.Month.ToString("MMMM yyyy",Money.Ar)+" · "+entry.Currency;SaveLabel="Cerrar";ReloadItems();
        AddCommand=new AsyncCommand(async()=>{if(this.entry.Obligation is not {} o||!o.AmountConfirmed||this.entry.Balance<=0)throw new RuleException("Cargá el importe pendiente en la planilla antes de agregar un pago.");if(dialogs.Show(new ServicePaymentEditor(store,new(this.entry.Service,o,this.entry.Payments,DateTime.Today))))await Reload();},e=>Error=LocalConfig.Friendly(e));
        EditCommand=new AsyncCommand(async()=>{if(Selected is not {Payment.Voided:false} p)return;var row=FindRow(p.Payment.ObligationId);if(dialogs.Show(new ServicePaymentEditor(store,row,p.Payment)))await Reload();},e=>Error=LocalConfig.Friendly(e));
        VoidCommand=new AsyncCommand(async()=>{if(Selected is {Payment.Voided:false} p&&dialogs.Confirm("¿Anular este pago? El registro se conserva y se recalcula el saldo.")){await store.SaveServicePaymentAsync(p.Payment with{Voided=true});await Reload();}},e=>Error=LocalConfig.Friendly(e));
    }
    private ServiceRow FindRow(long id)=>entry.Obligation?.Id==id?new(entry.Service,entry.Obligation,entry.Payments,DateTime.Today):entry.Legacy.Single(r=>r.Obligation.Id==id);
    private void ReloadItems(){Payments.Clear();foreach(var p in entry.Payments)Payments.Add(new(p,entry.Currency));foreach(var r in entry.Legacy)foreach(var p in r.Payments)Payments.Add(new(p,r.Obligation.Currency));Changed(nameof(Archive));Selected=null;Changed(nameof(Selected));}
    private async Task Reload(){var snapshot=await store.ReadAsync();entry=ServiceSheet.Rows(snapshot.Services,entry.Month,DateTime.Today).Single(r=>r.ServiceId==entry.ServiceId);ReloadItems();await changed();}
    protected override Task Save(){Close();return Task.CompletedTask;}
}
