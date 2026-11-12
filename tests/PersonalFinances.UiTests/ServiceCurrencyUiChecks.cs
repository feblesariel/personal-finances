using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PersonalFinances.App;
using PersonalFinances.Core;
internal static class ServiceCurrencyUiChecks
{
    internal static async Task Run(MainWindow window,ConnectionProfile profile,Action<bool,string> check,Action<Window,string> screenshot,string artifacts)
    {
        var vm=(ShellViewModel)window.DataContext;vm.Page="Servicios";await ServiceUiChecks.Wait(()=>vm.PrepareServicesCommand.CanExecute(null));
        await ServiceUiChecks.Edit(vm,()=>vm.AddServiceCommand.Execute(null),(_,m)=>{var e=(SimpleServiceEditor)m;e.Name="Moneda simple UI";e.Frequency="Anual";e.Amount="19,99";e.PaymentDate=new(2026,6,2);});
        var row=vm.SheetRows.Single(r=>r.Name=="Moneda simple UI");vm.SelectedSheetRow=row;
        await ServiceUiChecks.Edit(vm,()=>vm.OpenServiceCommand.Execute(null),(w,m)=>{var selector=InteractionChecks.Visuals<ComboBox>(w).Single(c=>AutomationProperties.GetAutomationId(c)=="SimpleServiceCurrency");selector.SelectedItem="USD";selector.GetBindingExpression(ComboBox.SelectedItemProperty)!.UpdateSource();screenshot(w,System.IO.Path.Combine(artifacts,"simple-currency-modal.png"));});
        row=vm.SheetRows.Single(r=>r.ServiceId==row.ServiceId);check(row.Currency=="USD"&&row.PaidDisplay=="US$ 19,99"&&row.PaymentDate==new DateTime(2026,6,2),"Simple UI: corregir moneda se refleja en la fila sin modificar monto ni fecha");
        var editor=new SimpleServiceEditor(new FinanceStore(profile),row.Entry.Month,new Dialogs(),row.Entry);check(editor.Currency=="USD"&&editor.Amount=="19,99","Simple UI: moneda y monto coinciden al reabrir el modal");
    }
}
