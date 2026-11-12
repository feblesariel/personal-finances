using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using PersonalFinances.App;
using PersonalFinances.Core;

internal static class ServiceCurrencyUiChecks
{
    private static async Task Wait(Func<bool> condition){var end=DateTime.UtcNow.AddSeconds(40);while(!condition()){if(DateTime.UtcNow>end)throw new TimeoutException("Currency UI timeout");await Task.Delay(30);}}
    internal static async Task Run(MainWindow window,ConnectionProfile profile,Action<bool,string> check,Action<Window,string> screenshot,string artifacts)
    {
        var store=new FinanceStore(profile);var service=new RecurringService{Name="GOOGLE ONE moneda UI",Frequency="Anual",DueMonth=6};service.Id=await store.SaveServiceAsync(service);
        var obligation=new ServiceObligation{ServiceId=service.Id,Period=new(2026,6,1),Amount=19.99m};obligation.Id=await store.SaveObligationAsync(obligation);
        var payment=new ServicePayment{ObligationId=obligation.Id,Amount=19.99m,Date=new(2026,6,2),Notes="Pago a conservar"};payment.Id=await store.SaveServicePaymentAsync(payment);
        var vm=(ShellViewModel)window.DataContext;vm.Page="Servicios";await Wait(()=>vm.PrepareServicesCommand.CanExecute(null));await vm.RefreshAsync();
        vm.SheetMonth=vm.SheetMonths.Single(m=>m.Month==ServiceSheet.Month(DateTime.Today));vm.SelectedSheetRow=vm.SheetRows.Single(r=>r.ServiceId==service.Id);
        async Task Edit(string currency)
        {
            var done=new TaskCompletionSource();var stage=0;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};
            timer.Tick+=async(_,_)=>
            {
                try
                {
                    if(stage==0&&Application.Current.Windows.OfType<EditorWindow>().FirstOrDefault(w=>w.DataContext is ServiceEntryEditor) is {} entryWindow){stage=1;((ServiceEntryEditor)entryWindow.DataContext).GeneralCommand.Execute(null);return;}
                    if(stage!=1||Application.Current.Windows.OfType<EditorWindow>().FirstOrDefault(w=>w.DataContext is ServiceEditor) is not {} editor)return;
                    stage=2;timer.Stop();var model=(ServiceEditor)editor.DataContext;
                    var selector=InteractionChecks.Visuals<ComboBox>(editor).Single(c=>c.ItemsSource==model.Currencies);selector.SelectedItem=currency;selector.GetBindingExpression(ComboBox.SelectedItemProperty)!.UpdateSource();editor.UpdateLayout();
                    screenshot(editor,System.IO.Path.Combine(artifacts,"google-one-currency-editor.png"));model.SaveCommand.Execute(null);model.SaveCommand.Execute(null);
                    await Wait(()=>!editor.IsVisible||!model.Busy&&model.Error.Length>0);if(editor.IsVisible)throw new Exception(model.Error);done.SetResult();
                }
                catch(Exception e){timer.Stop();done.TrySetException(e);foreach(var w in Application.Current.Windows.OfType<EditorWindow>().ToList())w.Close();}
            };
            timer.Start();vm.OpenServiceCommand.Execute(null);await done.Task;await Wait(()=>vm.OpenServiceCommand.CanExecute(null));
        }
        await Edit("USD");var row=vm.SheetRows.Single(r=>r.ServiceId==service.Id);
        check(row.Currency=="USD"&&row.AmountDisplay=="US$ 19,99"&&row.PaidDisplay=="US$ 19,99","GOOGLE ONE UI: cambiar ARS a USD desde Datos del servicio actualiza inmediatamente la fila");
        check(row.Entry is {Paid:19.99m,Status:"Pagado",PaymentDate:{Year:2026,Month:6,Day:2},CoverageEnd:{Year:2027,Month:6,Day:2}}&&row.Entry.RecordedPayments.Count==1,"GOOGLE ONE UI: conserva importe, único pago y cobertura anual");
        var reopened=new ServiceEntryEditor(store,row.Entry,new Dialogs());check(reopened.Currency=="USD"&&reopened.Subtitle.Contains("US$ 19,99"),"GOOGLE ONE UI: tabla, modal y detalle coinciden en USD");
        await store.SaveServiceAsync(row.Entry.Service with{Currency="ARS"});await vm.RefreshAsync();vm.SelectedSheetRow=vm.SheetRows.Single(r=>r.ServiceId==service.Id);
        await Edit("ARS");check(vm.SheetRows.Single(r=>r.ServiceId==service.Id).Currency=="ARS","GOOGLE ONE UI: guardar también corrige una diferencia preexistente entre definición y fila");
        await vm.RefreshAsync();vm.SelectedSheetRow=vm.SheetRows.Single(r=>r.ServiceId==service.Id);await Edit("USD");
        var export=new ExportService(store);var success=false;var limit=DateTime.UtcNow.AddSeconds(40);while(!success&&DateTime.UtcNow<limit){success=await export.TryAsync(true);if(!success)await Task.Delay(80);}if(!success)throw new Exception("Currency TXT export failed");
        var txt=await System.IO.File.ReadAllTextAsync(LocalConfig.Preferences().ExportPath!);check(txt[txt.IndexOf(service.Name,StringComparison.Ordinal)..].Contains("US$ 19,99"),"GOOGLE ONE UI: TXT recibe la moneda corregida");
        window.UpdateLayout();screenshot(window,System.IO.Path.Combine(artifacts,"google-one-currency-table.png"));
    }
}
