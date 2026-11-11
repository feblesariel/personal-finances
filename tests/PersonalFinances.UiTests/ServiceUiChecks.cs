using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PersonalFinances.App;
using PersonalFinances.Core;
internal static class ServiceUiChecks
{
    static async Task Wait(Func<bool> condition){var end=DateTime.UtcNow.AddSeconds(40);while(!condition()){if(DateTime.UtcNow>end)throw new TimeoutException("Service modal UI timeout");await Task.Delay(30);}}
    sealed class ProbeRow:IEditableObject {public string Name {get;set;}="Probe";public void BeginEdit(){}public void CancelEdit(){}public void EndEdit(){}}
    internal static async Task Run(MainWindow window,ConnectionProfile profile,Action<bool,string> check,Action<Window,string> screenshot,string artifacts)
    {
        // Reproduce the failure mechanism of the old refresh-during-edit path without using real data.
        var probe=new DataGrid{AutoGenerateColumns=false,IsReadOnly=false,ItemsSource=new ObservableCollection<ProbeRow>{new()}};probe.Columns.Add(new DataGridTextColumn{Binding=new System.Windows.Data.Binding("Name")});var host=new Window{Width=300,Height=180,Content=probe};host.Show();probe.UpdateLayout();probe.SelectedIndex=0;probe.CurrentCell=new(probe.Items[0],probe.Columns[0]);probe.BeginEdit();var reproduced=false;try{probe.Items.Refresh();}catch(InvalidOperationException){reproduced=true;}probe.CancelEdit(DataGridEditingUnit.Row);host.Close();check(reproduced,"Regresión: refrescar CollectionView en edición reproduce InvalidOperationException del flujo anterior");
        var vm=(ShellViewModel)window.DataContext;vm.Page="Servicios";await Wait(()=>vm.PrepareServicesCommand.CanExecute(null));
        var grid=(DataGrid)window.FindName("ServicesGrid");check(grid.IsReadOnly&&!grid.BeginEdit()&&!InteractionChecks.Visuals<Button>(grid).Any(b=>b.Content?.ToString() is "⋯" or "..."),"Servicios UI: tabla sólo lectura, sin edición directa ni botones de tres puntos");
        async Task Dialog(Action open,Action<EditorWindow,EditorViewModel> fill)
        {
            var done=new TaskCompletionSource();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};
            timer.Tick+=async(_,_)=>{var editor=Application.Current.Windows.OfType<EditorWindow>().FirstOrDefault();if(editor==null)return;timer.Stop();try{var m=(EditorViewModel)editor.DataContext;fill(editor,m);m.SaveCommand.Execute(null);m.SaveCommand.Execute(null);await Wait(()=>!editor.IsVisible||!m.Busy&&m.Error.Length>0);if(editor.IsVisible)throw new Exception(m.Error);done.SetResult();}catch(Exception e){done.SetException(e);editor.Close();}};
            timer.Start();open();await done.Task;await Wait(()=>vm.OpenServiceCommand.CanExecute(null)&&vm.AddServiceCommand.CanExecute(null));
        }
        await Dialog(()=>vm.AddServiceCommand.Execute(null),(_,m)=>{var e=(ServiceEditor)m;e.Model.Name="TUENTI modal UI";});
        var first=vm.SheetRows.Single(r=>r.Name=="TUENTI modal UI");grid.SelectedItem=first;grid.ScrollIntoView(first);window.UpdateLayout();
        await Dialog(()=>{var row=(DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(first);grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Control.MouseDoubleClickEvent,Source=row});},(editor,m)=>
        {var e=(ServiceEntryEditor)m;check(e.Title.Contains("TUENTI modal UI")&&e.NewPayment==""&&e.PaymentDate==DateTime.Today,"Servicios UI: doble clic abre modal correcto con nuevo abono vacío y fecha actual");e.Amount="15.000,00";e.NewPayment="5.000,00";e.PaymentDate=DateTime.Today.AddDays(-1);screenshot(editor,System.IO.Path.Combine(artifacts,"service-modal.png"));});
        check(vm.SheetRows.Single(r=>r.Name==first.Name).Entry is {Paid:5000,Status:"Parcial"},"Servicios UI: nuevo pago parcial guardado desde modal sin duplicarse");
        vm.SelectedSheetRow=vm.SheetRows.Single(r=>r.Name==first.Name);await Dialog(()=>grid.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(grid),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent}),(w,m)=>{var e=(ServiceEntryEditor)m;check(e.NewPayment=="","Servicios UI: reabrir no precarga el pago anterior");e.NewPayment="10.000,00";});
        check(vm.SheetRows.Single(r=>r.Name==first.Name).Entry is {Paid:15000,Status:"Pagado",Payments.Count:2},"Servicios UI: segundo pago acumula y deja PAGADO");
        var store=new FinanceStore(profile);for(var i=0;i<6;i++)await store.SaveServiceAsync(new(){Name="Stress servicio "+i});await store.CreateServiceMonthAsync(DateTime.Today,false);await vm.RefreshAsync();
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);using var release=new ManualResetEventSlim();var blocked=new ExportService(store){BeforeFileWrite=()=>{entered.TrySetResult();if(!release.Wait(TimeSpan.FromSeconds(90)))throw new TimeoutException("Test IO delay");}};
        var previousFolder=System.IO.Path.GetDirectoryName(LocalConfig.Preferences().ExportPath!)!;var stressFolder=System.IO.Path.Combine(LocalConfig.Folder,"stress-drive");System.IO.Directory.CreateDirectory(stressFolder);LocalConfig.SetExportFolder(stressFolder);
        Task<bool>? exporting=null;var startLimit=DateTime.UtcNow.AddSeconds(30);
        while(!entered.Task.IsCompleted&&DateTime.UtcNow<startLimit)
        {
            var attempt=blocked.TryAsync(true);await Task.WhenAny(entered.Task,attempt,Task.Delay(100));
            if(entered.Task.IsCompleted){exporting=attempt;break;}
            if(attempt.IsCompleted){await Task.Delay(100);continue;}
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));exporting=attempt;
        }
        if(!entered.Task.IsCompleted)throw new Exception("Could not start delayed export: "+blocked.Status);
        var ticks=0;var heartbeat=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};heartbeat.Tick+=(_,_)=>ticks++;heartbeat.Start();var allEmpty=true;var dates=true;var ioStayedBlocked=true;var stressWatch=System.Diagnostics.Stopwatch.StartNew();
        try
        {
            for(var i=0;i<60;i++)
            {
                ioStayedBlocked&=exporting!=null&&!exporting.IsCompleted;vm.SelectedSheetRow=vm.SheetRows.Single(r=>r.Name=="Stress servicio "+i%6);
                await Dialog(()=>vm.OpenServiceCommand.Execute(null),(w,m)=>{var e=(ServiceEntryEditor)m;allEmpty&=e.NewPayment=="";dates&=e.PaymentDate==DateTime.Today;e.Amount="10,00";e.NewPayment="1,00";vm.OpenServiceCommand.Execute(null);});
            }
        }
        finally{ioStayedBlocked&=exporting!=null&&!exporting.IsCompleted;release.Set();heartbeat.Stop();if(exporting!=null)await exporting;}
        var snapshot=await store.ReadAsync();var stressIds=snapshot.Services.Services.Where(s=>s.Name.StartsWith("Stress servicio ")).Select(s=>s.Id).ToHashSet();var stressObligations=snapshot.Services.Obligations.Where(o=>stressIds.Contains(o.ServiceId)).Select(o=>o.Id).ToHashSet();
        check(snapshot.Services.Payments.Count(p=>stressObligations.Contains(p.ObligationId))==60&&allEmpty&&dates,"Estrés UI: 60 pagos en seis servicios, doble Guardar y reapertura repetida sin duplicados");
        Console.WriteLine($"STRESS: 60 payments, UI ticks={ticks}, elapsedMs={stressWatch.ElapsedMilliseconds}, IO blocked throughout={ioStayedBlocked}");
        check(ticks>30&&ioStayedBlocked,"Estrés UI: dispatcher responde mientras se guardan pagos con escritura de Drive bloqueada");
        check(await new ExportService(store).TryAsync(true),"Estrés UI: TXT final se actualiza al liberar Drive");LocalConfig.SetExportFolder(previousFolder);await vm.Export(true);
        var defaults=grid.Columns[0].Width;grid.Columns[0].Width=new(300);grid.Columns[0].DisplayIndex=2;TableLayout.Save(grid);TableLayout.Reset(grid);check(grid.Columns[0].Width==defaults&&grid.Columns[0].DisplayIndex==0&&!System.IO.File.Exists(TableLayout.PreferencePath("services")),"Servicios UI: Restablecer tabla conserva sólo presentación");
        window.WindowState=WindowState.Maximized;window.UpdateLayout();await Task.Delay(80);screenshot(window,System.IO.Path.Combine(artifacts,"services-modal-table.png"));
        foreach(var item in vm.SheetRows)
        {
            grid.ScrollIntoView(item,grid.Columns[5]);window.UpdateLayout();if(grid.ItemContainerGenerator.ContainerFromItem(item) is not DataGridRow row)continue;var content=grid.Columns[5].GetCellContent(item);if(content==null)continue;
            var stack=InteractionChecks.Visuals<StackPanel>(content).FirstOrDefault();if(stack!=null){var center=stack.TransformToAncestor(row).Transform(new Point(0,stack.ActualHeight/2)).Y;if(Math.Abs(center-row.ActualHeight/2)>2)throw new Exception("Status not vertically centered");}
        }
        check(true,"Servicios UI: estados y controles centrados verticalmente");
    }
}