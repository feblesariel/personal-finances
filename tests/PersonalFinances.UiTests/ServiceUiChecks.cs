using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PersonalFinances.App;
using PersonalFinances.Core;
internal static class ServiceUiChecks
{
    static async Task Wait(Func<bool> condition){var end=DateTime.UtcNow.AddSeconds(30);while(!condition()){if(DateTime.UtcNow>end)throw new TimeoutException("Sheet UI timeout");await Task.Delay(50);}}
    internal static async Task Run(MainWindow window,ConnectionProfile profile,Action<bool,string> check,Action<Window,string> screenshot,string artifacts)
    {
        var vm=(ShellViewModel)window.DataContext;vm.Page="Servicios";await Wait(()=>vm.PrepareServicesCommand.CanExecute(null));
        async Task Dialog(ICommand command,Action<EditorViewModel> fill)
        {
            var done=new TaskCompletionSource();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
            timer.Tick+=async(_,_)=>{var editor=Application.Current.Windows.OfType<EditorWindow>().FirstOrDefault();if(editor==null)return;timer.Stop();try{var m=(EditorViewModel)editor.DataContext;fill(m);m.SaveCommand.Execute(null);await Wait(()=>!editor.IsVisible||!m.Busy&&m.Error.Length>0);if(editor.IsVisible)throw new Exception(m.Error);done.SetResult();}catch(Exception e){done.SetException(e);editor.Close();}};
            timer.Start();command.Execute(null);await done.Task;await Wait(()=>command.CanExecute(null));
        }
        await Dialog(vm.AddServiceCommand,m=>{var e=(ServiceEditor)m;e.Model.Name="Internet planilla UI";e.Amount="22.554,99";});
        var row=vm.SheetRows.Single(r=>r.Name=="Internet planilla UI");check(row.Status=="SIN CARGAR","Planilla UI: importe habitual no implica bonificación ni deuda confirmada");
        var grid=(DataGrid)window.FindName("ServicesGrid");
        void Input(ServiceSheetRow item,int col,string text)
        {
            grid.SelectedItem=item;grid.CurrentCell=new(item,grid.Columns[col]);grid.ScrollIntoView(item,grid.Columns[col]);window.UpdateLayout();if(!grid.BeginEdit())throw new Exception("Cannot begin sheet cell edit");window.UpdateLayout();var cell=grid.Columns[col].GetCellContent(item);var box=cell is TextBox tb?tb:InteractionChecks.Visuals<TextBox>(cell).First();box.Text=text;box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();grid.CommitEdit(DataGridEditingUnit.Cell,true);
        }
        Input(row,1,"22.554,99");Input(row,2,"10.000,00");grid.CurrentCell=new(row,grid.Columns[3]);grid.BeginEdit();window.UpdateLayout();var picker=InteractionChecks.Visuals<DatePicker>(grid.Columns[3].GetCellContent(row)).Single();picker.SelectedDate=DateTime.Today.AddDays(-1);picker.GetBindingExpression(DatePicker.SelectedDateProperty)!.UpdateSource();grid.CommitEdit(DataGridEditingUnit.Cell,true);grid.CommitEdit(DataGridEditingUnit.Row,true);await Wait(()=>row.Entry.Paid==10000);
        check(row.Status=="PARCIAL"&&row.Entry.PaymentDate==DateTime.Today.AddDays(-1),"Planilla UI: celdas reales guardan importe, pago y fecha en MySQL");
        Input(row,2,"22.554,99");grid.CommitEdit(DataGridEditingUnit.Row,true);await Wait(()=>row.Entry.Status=="Pagado");
        check(row.Entry.Payments.Count==2,"Planilla UI: aumentar acumulado agrega un segundo pago sin perder el primero");
        Input(row,1,"1,00");grid.CancelEdit(DataGridEditingUnit.Row);check(row.AmountText=="22.554,99","Planilla UI: cancelar fila restaura el importe sin escribir en MySQL");
        await Dialog(vm.AddServiceCommand,m=>{var e=(ServiceEditor)m;e.Model.Name="Tarjeta planilla UI";e.Type="Tarjeta de crédito";});
        check(vm.SheetRows.Count(r=>r.Name=="Tarjeta planilla UI")==1&&vm.SheetRows.Single(r=>r.Name=="Tarjeta planilla UI").Currency=="ARS","Planilla UI: una fila ARS por tarjeta");
        var card=vm.SheetRows.Single(r=>r.Name=="Tarjeta planilla UI");Input(card,1,"0,00");grid.CommitEdit(DataGridEditingUnit.Row,true);await Wait(()=>card.Entry.Status=="Bonificado");check(true,"Planilla UI: cero escrito confirma bonificación");
        var originalMonth=vm.SheetMonth!.Month;await Dialog(vm.NewServiceMonthCommand,_=>{});
        check(vm.SheetMonth!.Month==originalMonth.AddMonths(1)&&vm.SheetRows.Single(r=>r.Name=="Internet planilla UI").PaidText=="","Planilla UI: nuevo mes completo con pagos vacíos");
        vm.SheetMonth=vm.SheetMonths.Single(m=>m.Month==originalMonth);check(vm.SheetRows.Single(r=>r.Name=="Internet planilla UI").Entry.Paid==22554.99m,"Planilla UI: volver a noviembre conserva pagos anteriores");
        var data=(await new FinanceStore(profile).CaptureBackupAsync("test")).Document;var hash=BackupValidation.Fingerprint(data);
        var defaults=grid.Columns[0].Width;grid.Columns[0].Width=new(300);grid.Columns[0].DisplayIndex=2;grid.Items.SortDescriptions.Clear();grid.Items.SortDescriptions.Add(new("Name",System.ComponentModel.ListSortDirection.Descending));TableLayout.Save(grid);check(System.IO.File.Exists(TableLayout.PreferencePath("services")),"Planilla UI: anchos y orden se guardan independientemente de Consumos");TableLayout.Reset(grid);
        check(grid.Columns[0].Width==defaults&&grid.Columns[0].DisplayIndex==0&&grid.Items.SortDescriptions[0].PropertyName=="ServiceId"&&!System.IO.File.Exists(TableLayout.PreferencePath("services")),"Planilla UI: Restablecer tabla recupera columnas y elimina preferencias");
        check(hash==BackupValidation.Fingerprint((await new FinanceStore(profile).CaptureBackupAsync("test")).Document),"Planilla UI: Restablecer tabla no altera la contabilidad");
        var fixtureStore=new FinanceStore(profile);for(var i=1;i<=11;i++)await fixtureStore.SaveServiceAsync(new(){Name="Servicio mensual "+i});await fixtureStore.CreateServiceMonthAsync(originalMonth,false);await vm.RefreshAsync();
        window.Width=1320;window.Height=880;window.UpdateLayout();var shortHeight=grid.ActualHeight;window.WindowState=WindowState.Maximized;window.UpdateLayout();check(grid.ActualHeight>shortHeight,"Planilla UI: tabla crece verticalmente al maximizar");screenshot(window,System.IO.Path.Combine(artifacts,"services-sheet.png"));window.WindowState=WindowState.Normal;window.Width=960;window.Height=620;window.UpdateLayout();screenshot(window,System.IO.Path.Combine(artifacts,"services-sheet-960.png"));
        check(!InteractionChecks.Visuals<DataGrid>(window).Any(g=>g.Name=="ServicePaymentsGrid"),"Planilla UI: no hay panel de pagos permanente");
    }
}