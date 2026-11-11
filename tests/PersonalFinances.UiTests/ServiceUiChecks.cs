using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PersonalFinances.App;
using PersonalFinances.Core;

internal static class ServiceUiChecks
{
    internal static async Task Run(MainWindow window,ConnectionProfile profile,Action<bool,string> check,Action<Window,string> screenshot,string artifacts)
    {
        var vm=(ShellViewModel)window.DataContext;vm.Page="Servicios";
        async Task Dialog(System.Windows.Input.ICommand command,Action<EditorViewModel> fill)
        {
            var done=new TaskCompletionSource();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(120)};
            timer.Tick+=async(_,_)=>{var editor=Application.Current.Windows.OfType<EditorWindow>().FirstOrDefault();if(editor==null)return;timer.Stop();try{var model=(EditorViewModel)editor.DataContext;fill(model);model.SaveCommand.Execute(null);var end=DateTime.UtcNow.AddSeconds(15);while(editor.IsVisible){if(DateTime.UtcNow>end)throw new Exception("Service editor: "+model.Error);await Task.Delay(50);}done.SetResult();}catch(Exception e){done.SetException(e);editor.Close();}};
            timer.Start();command.Execute(null);await done.Task;
            var until=DateTime.UtcNow.AddSeconds(30);while(!command.CanExecute(null)){if(DateTime.UtcNow>until)throw new TimeoutException("Service refresh timeout");await Task.Delay(50);}
        }
        await Dialog(vm.AddServiceCommand,m=>{var e=(ServiceEditor)m;e.Model.Name="Internet UI";e.Amount="22.554,99";});
        check(vm.Services.Any(s=>s.Name=="Internet UI"),"Servicios UI: alta desde formulario real");vm.SelectedService=vm.Services.Single(s=>s.Name=="Internet UI");
        await Dialog(vm.GenerateServicePeriodCommand,_=>{});
        vm.SelectedObligation=vm.ServiceRows.Single(r=>r.Name=="Internet UI");
        await Dialog(vm.PayServiceCommand,m=>{var e=(ServicePaymentEditor)m;e.Amount="10.000,00";e.Model.Method="VISA";e.Model.Notes="Pago parcial UI";});
        check(vm.ServiceRows.Single(r=>r.Name=="Internet UI").BaseStatus=="Pago parcial","Servicios UI: pago rápido y saldo parcial");
        check(vm.ServiceRows.Single(r=>r.Name=="Internet UI").DueLabel=="Sin definir","Servicios UI: permite vencimiento desconocido");
        await Dialog(vm.AddServiceCommand,m=>{var e=(ServiceEditor)m;e.Model.Name="Resumen UI";e.Type="Tarjeta de crédito";e.Amount="100.000,00";e.AmountUsd="20,00";});
        check(vm.ServiceRows.Count(r=>r.Service.Name=="Resumen UI")==2,"Servicios UI: alta única de tarjeta prepara ARS y USD sin referencia");
        vm.SelectedObligation=vm.ServiceRows.Single(r=>r.Service.Name=="Resumen UI"&&r.Obligation.Currency=="USD");
        await Dialog(vm.PayServiceCommand,m=>{var e=(ServicePaymentEditor)m;check(e.Currency=="USD","Servicios UI: pago hereda USD del cargo elegido");e.Amount="5,00";});
        check(vm.ServiceRows.Single(r=>r.Service.Name=="Resumen UI"&&r.Obligation.Currency=="ARS").Paid==0,"Servicios UI: pago USD conserva saldo ARS");
        vm.SelectedObligation=vm.ServiceRows.Single(r=>r.Service.Name=="Resumen UI"&&r.Obligation.Currency=="USD");window.Width=1320;window.Height=880;window.UpdateLayout();screenshot(window,System.IO.Path.Combine(artifacts,"services.png"));
        window.Width=960;window.Height=620;window.UpdateLayout();screenshot(window,System.IO.Path.Combine(artifacts,"services-960.png"));
        check(InteractionChecks.Visuals<DataGrid>(window).Single(g=>g.Name=="ServicesGrid").IsVisible,"Servicios UI: tabla visible a 960 píxeles");
        vm.SelectedService=vm.Services.Single(s=>s.Name=="Internet UI");vm.ServiceHistoryCommand.Execute(null);check(vm.ServicePeriod=="Todos"&&vm.ServiceRows.Count>0,"Servicios UI: historial del servicio");
        vm.ClearServiceHistoryCommand.Execute(null);
    }
}
