using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using PersonalFinances.App;
using PersonalFinances.Core;

internal static class ExportFileNameUiChecks
{
    private static async Task Wait(Func<bool> condition){var limit=DateTime.UtcNow.AddSeconds(45);while(!condition()){if(DateTime.UtcNow>limit)throw new TimeoutException("Filename UI timeout");await Task.Delay(40);}}
    internal static async Task Run(MainWindow window,ConnectionProfile profile,Action<bool,string> check,Action<Window,string> screenshot,string artifacts)
    {
        var vm=(ShellViewModel)window.DataContext;vm.Page="Configuración";window.UpdateLayout();var preferences=LocalConfig.Preferences();var folder=System.IO.Path.GetDirectoryName(preferences.ExportPath!)!;
        // The main UI fixture supplies its profile in memory after setup clears its temporary DPAPI file.
        if(!System.IO.File.Exists(LocalConfig.FilePath))LocalConfig.Save(profile);
        var credentials=System.IO.File.ReadAllBytes(LocalConfig.FilePath);var store=new FinanceStore(profile);var ledger=(await store.CaptureBackupAsync("filename-ui")).Document;
        async Task Dialog(Func<EditorWindow,ExportEditor,Task> body)
        {
            await Wait(()=>vm.ExportFolderCommand.CanExecute(null));var done=new TaskCompletionSource();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};
            timer.Tick+=async(_,_)=>{var dialog=Application.Current.Windows.OfType<EditorWindow>().FirstOrDefault(w=>w.DataContext is ExportEditor);if(dialog==null)return;timer.Stop();try{await body(dialog,(ExportEditor)dialog.DataContext);done.SetResult();}catch(Exception e){done.SetException(e);dialog.Close();}};
            timer.Start();vm.ExportFolderCommand.Execute(null);await done.Task;await Wait(()=>vm.ExportFolderCommand.CanExecute(null));
        }
        TextBox NameField(Window w)=>InteractionChecks.Visuals<TextBox>(w).Single(t=>AutomationProperties.GetAutomationId(t)=="ExportFileName");
        try
        {
            await Dialog(async(w,m)=>
            {
                check(NameField(w).Text==System.IO.Path.GetFileName(preferences.ExportPath),"Archivo UI: Configuración muestra el nombre actual en un campo editable");NameField(w).Text="Mi resumen personal";NameField(w).GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                check(m.Destination==System.IO.Path.Combine(folder,"Mi resumen personal.txt"),"Archivo UI: la vista previa combina carpeta y nombre con extensión automática");screenshot(w,System.IO.Path.Combine(artifacts,"export-file-name.png"));m.SaveCommand.Execute(null);await Wait(()=>!w.IsVisible||!m.Busy&&m.Error.Length>0);if(w.IsVisible)throw new Exception(m.Error);
            });
            var first=System.IO.Path.Combine(folder,"Mi resumen personal.txt");await Wait(()=>System.IO.File.Exists(first));check(vm.ExportPath==first&&System.IO.File.ReadAllText(first).Contains("PERSONAL FINANCES"),"Archivo UI: Guardar crea el TXT elegido y actualiza la ruta visible");
            await Dialog(async(w,m)=>
            {
                check(m.FileName=="Mi resumen personal.txt","Archivo UI: reabrir conserva el nombre elegido");NameField(w).Text="../fuera.txt";NameField(w).GetBindingExpression(TextBox.TextProperty)!.UpdateSource();m.SaveCommand.Execute(null);await Wait(()=>!m.Busy&&m.Error.Length>0);
                check(w.IsVisible&&LocalConfig.Preferences().ExportPath==first,"Archivo UI: nombre inválido mantiene el modal abierto y conserva el destino");NameField(w).Text="Mi resumen final.txt";NameField(w).GetBindingExpression(TextBox.TextProperty)!.UpdateSource();m.SaveCommand.Execute(null);await Wait(()=>!w.IsVisible||!m.Busy&&m.Error.Length>0);if(w.IsVisible)throw new Exception(m.Error);
            });
            var final=System.IO.Path.Combine(folder,"Mi resumen final.txt");await Wait(()=>System.IO.File.Exists(final));check(LocalConfig.Preferences().ExportPath==final&&vm.ExportPath==final,"Archivo UI: las siguientes exportaciones usan el nuevo nombre");
            check(credentials.SequenceEqual(System.IO.File.ReadAllBytes(LocalConfig.FilePath))&&BackupValidation.Fingerprint(ledger)==BackupValidation.Fingerprint((await store.CaptureBackupAsync("filename-ui")).Document),"Archivo UI: editar nombre no altera datos MySQL ni credenciales");
        }
        finally{LocalConfig.UpdatePreferences(_=>preferences);await vm.RefreshAsync();await vm.Export(true);}
    }
}
