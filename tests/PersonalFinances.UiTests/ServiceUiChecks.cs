using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PersonalFinances.App;
using PersonalFinances.Core;
internal static class ServiceUiChecks
{
    internal static async Task Wait(Func<bool> condition){var end=DateTime.UtcNow.AddSeconds(45);while(!condition()){if(DateTime.UtcNow>end)throw new TimeoutException("Simple service UI timeout");await Task.Delay(30);}}
    internal static async Task Edit(ShellViewModel vm,Action open,Action<EditorWindow,EditorViewModel> fill)
    {
        var done=new TaskCompletionSource();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};
        timer.Tick+=async(_,_)=>{var editor=Application.Current.Windows.OfType<EditorWindow>().FirstOrDefault();if(editor==null)return;timer.Stop();try{var m=(EditorViewModel)editor.DataContext;fill(editor,m);m.SaveCommand.Execute(null);m.SaveCommand.Execute(null);await Wait(()=>!editor.IsVisible||!m.Busy&&m.Error.Length>0);if(editor.IsVisible)throw new Exception(m.Error);done.SetResult();}catch(Exception e){done.SetException(e);editor.Close();}};
        timer.Start();open();await done.Task;await Wait(()=>vm.OpenServiceCommand.CanExecute(null)&&vm.AddServiceCommand.CanExecute(null)&&vm.NewServiceMonthCommand.CanExecute(null));
    }
    internal static async Task Run(MainWindow window,ConnectionProfile profile,Action<bool,string> check,Action<Window,string> screenshot,string artifacts)
    {
        var store=new FinanceStore(profile);var vm=(ShellViewModel)window.DataContext;vm.Page="Servicios";await Wait(()=>vm.PrepareServicesCommand.CanExecute(null));
        var grid=(DataGrid)window.FindName("ServicesGrid");
        check(grid.IsReadOnly&&grid.Columns.Select(c=>c.Header?.ToString()).SequenceEqual(new[]{"Servicio","Periodicidad","Monto abonado","Fecha de pago","Estado","Observaciones"}),"Simple UI: tabla de seis columnas, sin importe del período ni vencimiento");
        await Edit(vm,()=>vm.AddServiceCommand.Execute(null),(_,m)=>{var e=(SimpleServiceEditor)m;check(e.PaymentDate==DateTime.Today,"Simple UI: fecha inicial es hoy");e.Name="INTERNET simple UI";e.Amount="22.554,99";e.PaymentDate=DateTime.Today;e.Notes="Pago Internet";});
        var monthly=vm.SheetRows.Single(r=>r.Name=="INTERNET simple UI");check(monthly.BaseStatus=="Pagado"&&monthly.PaidDisplay=="$ 22.554,99","Simple UI: completar monto y fecha muestra PAGADO");
        grid.SelectedItem=monthly;grid.ScrollIntoView(monthly);window.UpdateLayout();
        await Edit(vm,()=>{var row=(DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(monthly);grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Control.MouseDoubleClickEvent,Source=row});},(w,m)=>
        {
            var e=(SimpleServiceEditor)m;check(e.Amount=="22.554,99"&&e.Notes=="Pago Internet","Simple UI: doble clic abre los valores existentes para editarlos");
            var labels=InteractionChecks.Visuals<TextBlock>(w).Select(t=>t.Text).ToList();check(!labels.Any(t=>t.Contains("Vencimiento")||t.Contains("vigencia")||t.Contains("habitual")||t.Contains("acumulado"))&&!InteractionChecks.Visuals<Button>(w).Any(b=>b.Content?.ToString() is "Historial" or "Datos del servicio"),"Simple UI: modal sin historial, vencimientos, habituales ni acumulados");
            e.Name="INTERNET editado UI";e.Amount="22.000,00";e.PaymentDate=DateTime.Today.AddDays(-1);e.Notes="Corregido";screenshot(w,System.IO.Path.Combine(artifacts,"simple-service-modal.png"));
        });
        monthly=vm.SheetRows.Single(r=>r.ServiceId==monthly.ServiceId);check(monthly.Name=="INTERNET editado UI"&&monthly.Entry.Paid==22000&&monthly.PaymentDate==DateTime.Today.AddDays(-1),"Simple UI: nombre, monto y fecha se modifican sin sumar otro pago");
        vm.SelectedSheetRow=monthly;await Edit(vm,()=>grid.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(grid),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent}),(_,m)=>{var e=(SimpleServiceEditor)m;e.Amount="";e.PaymentDate=null;e.Notes="";});
        check(vm.SheetRows.Single(r=>r.ServiceId==monthly.ServiceId).Entry is {Paid:null,PaymentDate:null,Status:"Pendiente"},"Simple UI: Enter abre y limpiar monto/fecha deja PENDIENTE");
        vm.SelectedSheetRow=vm.SheetRows.Single(r=>r.ServiceId==monthly.ServiceId);await Edit(vm,()=>vm.OpenServiceCommand.Execute(null),(_,m)=>{var e=(SimpleServiceEditor)m;e.Amount="150,00";e.PaymentDate=DateTime.Today;e.Notes="Antes de diciembre";});
        var annual=new RecurringService{Name="GOOGLE ONE anual UI",Frequency="Anual",Currency="USD",DueMonth=6};annual.Id=await store.SaveServiceAsync(annual);
        var charge=new ServiceObligation{ServiceId=annual.Id,Period=new(2026,6,1),Amount=19.99m,Currency="USD"};charge.Id=await store.SaveObligationAsync(charge);await store.SaveServicePaymentAsync(new(){ObligationId=charge.Id,Amount=19.99m,Date=new(2026,6,2)});await vm.RefreshAsync();
        check(!vm.SheetMonths.Any(m=>m.Month==new DateTime(2026,6,1)),"Simple UI: junio de un pago anual no se agrega al selector de trabajo");
        var month=vm.SheetMonth!.Month;await Edit(vm,()=>vm.NewServiceMonthCommand.Execute(null),(_,m)=>{check(((ServiceMonthEditor)m).Month==month.AddMonths(1),"Simple UI: Nuevo período propone el mes siguiente");});
        check(vm.SheetRows.Single(r=>r.ServiceId==monthly.ServiceId).Entry is {Paid:null,PaymentDate:null,Notes:"",Status:"Pendiente"},"Simple UI: nuevo período limpia mensuales");
        check(vm.SheetRows.Single(r=>r.ServiceId==annual.Id).Entry is {Paid:19.99m,PaymentDate:{Year:2026,Month:6,Day:2},Status:"Pagado"},"Simple UI: nuevo período conserva anual y fecha original");
        vm.SheetMonth=vm.SheetMonths.Single(m=>m.Month==month);check(vm.SheetRows.Single(r=>r.ServiceId==monthly.ServiceId).Entry.Paid==150,"Simple UI: el mes anterior conserva su monto y observaciones");
        for(var i=0;i<6;i++)await store.SaveSimpleServiceAsync(new(new(){Name="Stress simple "+i},month,null,null,"","ARS",""));await vm.RefreshAsync();
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);using var release=new ManualResetEventSlim();var blocked=new ExportService(store){BeforeFileWrite=()=>{entered.TrySetResult();if(!release.Wait(TimeSpan.FromSeconds(90)))throw new TimeoutException("Test IO delay");}};
        var originalFolder=System.IO.Path.GetDirectoryName(LocalConfig.Preferences().ExportPath!)!;var stressFolder=System.IO.Path.Combine(LocalConfig.Folder,"stress-simple");System.IO.Directory.CreateDirectory(stressFolder);LocalConfig.SetExportFolder(stressFolder);Task<bool>? exporting=null;
        var limit=DateTime.UtcNow.AddSeconds(40);while(!entered.Task.IsCompleted&&DateTime.UtcNow<limit){var attempt=blocked.TryAsync(true);await Task.WhenAny(entered.Task,attempt,Task.Delay(100));if(entered.Task.IsCompleted){exporting=attempt;break;}if(!attempt.IsCompleted){await entered.Task.WaitAsync(TimeSpan.FromSeconds(40));exporting=attempt;break;}await Task.Delay(80);}if(exporting==null)throw new Exception("Could not delay export");
        var ticks=0;var heartbeat=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};heartbeat.Tick+=(_,_)=>ticks++;heartbeat.Start();var watch=System.Diagnostics.Stopwatch.StartNew();var ioBlocked=true;
        try{for(var i=0;i<60;i++){ioBlocked&=!exporting.IsCompleted;vm.SelectedSheetRow=vm.SheetRows.Single(r=>r.Name=="Stress simple "+i%6);await Edit(vm,()=>vm.OpenServiceCommand.Execute(null),(_,m)=>{var e=(SimpleServiceEditor)m;e.Amount=(i+1).ToString(Money.Ar);e.PaymentDate=DateTime.Today;vm.OpenServiceCommand.Execute(null);});}}
        finally{ioBlocked&=!exporting.IsCompleted;release.Set();heartbeat.Stop();await exporting;}
        var state=await store.ReadAsync();var stress=SimpleServices.Rows(state.Services,month).Where(r=>r.Name.StartsWith("Stress simple ")).ToList();
        check(stress.Count==6&&stress.Sum(r=>r.Paid)==345&&state.Services.Payments.Count==1,"Simple UI: 60 ediciones con doble Guardar reemplazan seis valores sin pagos acumulados ni duplicados");
        Console.WriteLine($"STRESS SIMPLE: 60 saves, UI ticks={ticks}, elapsedMs={watch.ElapsedMilliseconds}, IO blocked throughout={ioBlocked}");check(ticks>30&&ioBlocked,"Simple UI: interfaz responde con Drive bloqueado durante guardados consecutivos");
        check(await new ExportService(store).TryAsync(true),"Simple UI: TXT se escribe al liberar Drive");LocalConfig.SetExportFolder(originalFolder);await vm.Export(true);
        var width=grid.Columns[0].Width;grid.Columns[0].Width=new(300);grid.Columns[0].DisplayIndex=2;TableLayout.Save(grid);TableLayout.Reset(grid);check(grid.Columns[0].Width==width&&grid.Columns[0].DisplayIndex==0,"Simple UI: Restablecer tabla sólo restaura presentación");
        window.WindowState=WindowState.Maximized;window.UpdateLayout();check(grid.ActualHeight>500,"Simple UI: tabla crece y aprovecha el espacio vertical");
        var paidItem=vm.SheetRows.First(r=>r.Entry.Paid!=null);grid.ScrollIntoView(paidItem);window.UpdateLayout();
        void CheckPaidGeometry()
        {
            var paid=(TextBlock)grid.Columns[2].GetCellContent(paidItem);var date=(TextBlock)grid.Columns[3].GetCellContent(paidItem);
            var header=InteractionChecks.Visuals<System.Windows.Controls.Primitives.DataGridColumnHeader>(grid).Single(h=>h.Column==grid.Columns[2]);var title=InteractionChecks.Visuals<TextBlock>(header).Single(t=>t.Text=="Monto abonado");
            var amountRight=paid.TransformToAncestor(grid).Transform(new Point(paid.ActualWidth,0)).X;var titleRight=title.TransformToAncestor(grid).Transform(new Point(title.ActualWidth,0)).X;var dateLeft=date.TransformToAncestor(grid).Transform(new Point(0,0)).X;
            check(Math.Abs(amountRight-titleRight)<=2,"Servicios UI: encabezado de monto alineado sobre el borde de los números");check(dateLeft-amountRight>=44,"Servicios UI: monto y fecha separados por al menos 44 píxeles");
        }
        CheckPaidGeometry();var paidWidth=grid.Columns[2].Width;grid.Columns[2].Width=new(405);window.UpdateLayout();CheckPaidGeometry();grid.Columns[2].Width=paidWidth;window.UpdateLayout();screenshot(window,System.IO.Path.Combine(artifacts,"simple-services-table.png"));
        check(!InteractionChecks.Visuals<TextBlock>(grid).Any(t=>t.Text.Contains("Vigente hasta")),"Simple UI: sin badges ni texto de vigencia");
        var fake=new ConfirmDialogs();var deleteEntry=vm.SheetRows.Single(r=>r.ServiceId==monthly.ServiceId).Entry;var delete=new SimpleServiceEditor(store,month,fake,deleteEntry);delete.DeleteCommand.Execute(null);await Wait(()=>delete.DeleteCommand.CanExecute(null));check((await store.ReadAsync()).Services.Services.Any(s=>s.Id==monthly.ServiceId),"Simple UI: cancelar confirmación conserva el servicio");
        fake.Approve=true;delete.DeleteCommand.Execute(null);await Wait(()=>delete.DeleteCommand.CanExecute(null));check(delete.Deleted&&!(await store.ReadAsync()).Services.Obligations.Any(o=>o.ServiceId==monthly.ServiceId)&&fake.Message.Contains(monthly.Name),"Simple UI: confirmar por nombre elimina todos los períodos con copia recuperable automática");await vm.RefreshAsync();
    }
    private sealed class ConfirmDialogs:IDialogs
    {
        public bool Approve;public string Message="";public bool Confirm(string text){Message=text;return Approve;}public bool Show(EditorViewModel vm)=>throw new NotSupportedException();public string? PickText()=>null;public string? PickFolder()=>null;public string? SaveBackup()=>null;public string? PickBackup()=>null;public void OpenFolder(string path){}
    }
}
