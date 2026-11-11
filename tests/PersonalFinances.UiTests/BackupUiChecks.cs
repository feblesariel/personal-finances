using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PersonalFinances.App;
using PersonalFinances.Core;

internal static class BackupUiChecks
{
    private static List<T> Visuals<T>(DependencyObject root) where T:DependencyObject=>InteractionChecks.Visuals<T>(root);
    private static async Task Wait(Func<bool> predicate)
    {var until=DateTime.UtcNow.AddSeconds(40);while(!predicate()){if(DateTime.UtcNow>until)throw new TimeoutException("Backup UI timeout");await Task.Delay(50);}}
    private static void Save(EditorWindow window)=>Visuals<Button>(window).Single(b=>AutomationProperties.GetAutomationId(b)=="SaveButton").Command.Execute(null);
    private static Task HandleDialog<T>(Application app,Func<EditorWindow,T,Task> edit)where T:EditorViewModel
    {
        var completion=new TaskCompletionSource();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(70)};
        timer.Tick+=async(_,_)=>
        {
            var dialog=app.Windows.OfType<EditorWindow>().FirstOrDefault(w=>w.DataContext is T);if(dialog==null)return;timer.Stop();
            try{await edit(dialog,(T)dialog.DataContext);completion.TrySetResult();}
            catch(Exception e){completion.TrySetException(e);if(!((T)dialog.DataContext).Busy)dialog.Close();}
        };timer.Start();return completion.Task.WaitAsync(TimeSpan.FromSeconds(70));
    }
    private static void Screenshot(Window window,string path)
    {
        window.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(window);var bitmap=new RenderTargetBitmap((int)(window.ActualWidth*dpi.DpiScaleX),(int)(window.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);bitmap.Render(window);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);png.Save(file);
    }
    internal static async Task Run(MainWindow window,ConnectionProfile profile,Application app,string temp,string artifacts,Action<bool,string> check)
    {
        var originalVm=(ShellViewModel)window.DataContext;var path=Path.Combine(temp,"ui-transfer.pfinance");var dialogs=new FileDialogs(path);
        var vm=new ShellViewModel(dialogs,profile);await vm.InitializeAsync();window.DataContext=vm;vm.Page="Configuración";window.Width=1100;window.Height=820;window.UpdateLayout();
        var buttons=Visuals<Button>(window).Where(b=>AutomationProperties.GetAutomationId(b) is "BackupExportButton" or "BackupImportButton").ToArray();
        check(buttons.Length==2&&buttons.All(b=>b.IsVisible&&b.ActualHeight==38)&&buttons[0].Command==vm.BackupExportCommand&&buttons[1].Command==vm.BackupImportCommand,"Configuración ofrece las dos acciones de respaldo con estilo y altura coherentes");
        buttons[0].BringIntoView();await Task.Delay(100);Screenshot(window,Path.Combine(artifacts,"backup-settings.png"));
        var store=new FinanceStore(profile);var baseline=await store.CaptureBackupAsync("1.4.0-test");var password="Respaldo interfaz 2026!";
        var pending=HandleDialog<BackupExportEditor>(app,async(dialog,editor)=>
        {
            var boxes=Visuals<PasswordBox>(dialog);check(boxes.Count==2&&boxes[0].IsKeyboardFocused,"Exportación usa campos de contraseña ocultos y foco inicial correcto");
            boxes[0].Password=password;boxes[1].Password="distinta";Save(dialog);await Wait(()=>!editor.Busy);
            check(editor.Error.Contains("coinciden")&&!File.Exists(path),"Validación de confirmación de contraseña antes de exportar");
            var ticks=0;var heartbeat=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};heartbeat.Tick+=(_,_)=>{if(editor.Busy)ticks++;};heartbeat.Start();
            try{boxes[1].Password=password;Save(dialog);check(editor.Busy,"Exportación muestra actividad mientras trabaja");await Wait(()=>!dialog.IsVisible);}finally{heartbeat.Stop();}
            check(ticks>0,"El dispatcher WPF sigue respondiendo durante el cifrado y la verificación");check(File.Exists(path),"Exportación WPF genera archivo .pfinance cifrado y verificado");
        });
        vm.BackupExportCommand.Execute(null);await pending;await Wait(()=>vm.Notice.Contains("Respaldo cifrado y verificado"));check(true,"Acción de exportación de Configuración confirma la ubicación final");
        var first=(await store.ReadAsync()).Movements.First(m=>m.Currency=="ARS");first.Amount+=1;await store.SaveMovementAsync(first);
        var changed=BackupValidation.Fingerprint((await store.CaptureBackupAsync("test")).Document);var config=LocalConfig.Preferences().ExportPath;
        vm.Search="filtro que no coincide";vm.StatsCard=baseline.Document.Cards.Last().Id;vm.StatsCurrency="USD";foreach(var tile in vm.Tiles)tile.Selected=false;
        pending=HandleDialog<BackupRestoreEditor>(app,async(dialog,editor)=>
        {
            var box=Visuals<PasswordBox>(dialog).Single();box.Password="contraseña incorrecta";Save(dialog);await Wait(()=>!editor.Busy);
            check(editor.Error.Contains("contraseña")&&!editor.PreviewAvailable,"Contraseña incorrecta muestra error controlado y permite reintentar");
            box.Password=password;Save(dialog);await Wait(()=>!editor.Busy&&editor.PreviewAvailable);dialog.UpdateLayout();
            check(editor.Preview.Contains("2 movimientos")&&editor.Preview.Contains("BASE ACTUAL")&&File.Exists(editor.SafetyPath)&&!editor.ConfirmReplacement,"Vista previa con cantidades de origen/destino y copia previa verificada antes de confirmar");
            var save=Visuals<Button>(dialog).Single(b=>AutomationProperties.GetAutomationId(b)=="SaveButton");check(save.Content?.ToString()=="Restaurar datos","La acción cambia de validación a restauración explícita");
            check(BackupValidation.Fingerprint((await store.CaptureBackupAsync("test")).Document)==changed,"Validación y vista previa no reemplazan datos");
            Save(dialog);await Wait(()=>!editor.Busy);check(editor.Error.Contains("explícitamente")&&dialog.IsVisible,"Restauración exige confirmación explícita sin cerrar el diálogo");
            var confirm=Visuals<CheckBox>(dialog).Single(c=>AutomationProperties.GetAutomationId(c)=="ConfirmRestore");confirm.BringIntoView();dialog.UpdateLayout();Screenshot(dialog,Path.Combine(artifacts,"backup-preview.png"));
            confirm.IsChecked=true;Save(dialog);await Wait(()=>!dialog.IsVisible);
        });
        vm.BackupImportCommand.Execute(null);await pending;await Wait(()=>vm.Notice.Contains("Restauración completa verificada"));
        check(dialogs.Restore?.Result is {SummaryExported:true}&&BackupValidation.Fingerprint((await store.CaptureBackupAsync("test")).Document)==BackupValidation.Fingerprint(baseline.Document),"Restauración WPF recupera importes, relaciones e historial exactos");
        check(vm.History.Count==2&&vm.TotalArs=="$ 100,00"&&vm.TotalUsd=="US$ 12,99"&&vm.CategoryStats.Count>0,"Dashboard, historial y estadísticas se recalculan tras restaurar");
        check(LocalConfig.Preferences().ExportPath==config&&File.ReadAllText(config!).Contains("$ 100,00"),"Restauración respeta la carpeta del equipo y regenera TXT simplificado");
        check(vm.Search==""&&vm.StatsCard==0&&vm.StatsCurrency=="ARS"&&vm.Tiles.All(t=>t.Selected),"Acción de restauración limpia filtros transitorios y actualiza todas las pantallas automáticamente");
        window.DataContext=originalVm;await originalVm.RefreshAsync();
    }
    private sealed class FileDialogs(string path):IDialogs
    {
        private readonly Dialogs native=new();public BackupRestoreEditor? Restore{get;private set;}
        public bool Show(EditorViewModel vm){if(vm is BackupRestoreEditor restore)Restore=restore;return native.Show(vm);}
        public string? SaveBackup()=>path;public string? PickBackup()=>path;
        public bool Confirm(string text)=>native.Confirm(text);public string? PickText()=>native.PickText();public string? PickFolder()=>native.PickFolder();public void OpenFolder(string folder)=>native.OpenFolder(folder);
    }
}
