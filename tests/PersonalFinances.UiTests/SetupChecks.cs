using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Dapper;
using MySqlConnector;
using PersonalFinances.App;
using PersonalFinances.Core;

internal static class SetupChecks
{
    private static List<T> Visuals<T>(DependencyObject parent) where T:DependencyObject
    {
        var list=new List<T>();for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var c=VisualTreeHelper.GetChild(parent,i);if(c is T value)list.Add(value);list.AddRange(Visuals<T>(c));}return list;
    }
    private static async Task Wait(Func<bool> predicate){var deadline=DateTime.UtcNow.AddSeconds(20);while(!predicate()){if(DateTime.UtcNow>deadline)throw new TimeoutException("Setup UI timeout");await Task.Delay(50);}}
    private static void Fill(Window w,string property,string value)
    {
        var field=Visuals<TextBox>(w).Single(t=>t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path==property);field.Text=value;field.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
    }
    private static void Save(Window w)=>Visuals<Button>(w).Single(b=>System.Windows.Automation.AutomationProperties.GetAutomationId(b)=="SaveButton").Command.Execute(null);
    private static Task PrepareDialog<T>(Application app,Func<EditorWindow,T,Task> edit) where T:EditorViewModel
    {
        var completion=new TaskCompletionSource();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};
        timer.Tick+=async(_,_)=>
        {
            var window=app.Windows.OfType<EditorWindow>().FirstOrDefault(w=>w.DataContext is T);if(window==null)return;timer.Stop();
            try{await edit(window,(T)window.DataContext);completion.TrySetResult();}catch(Exception ex){completion.TrySetException(ex);window.Close();}
        };timer.Start();return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
    public static async Task Run(Application app,ConnectionProfile rootProfile,MySqlConnection root,string temp,Action<bool,string> check)
    {
        var db=rootProfile.Database+"_setup";var p=rootProfile with{Database=db};MainWindow? window=null;
        if(!db.StartsWith("personal_finances_tests_ui_"))throw new Exception("Unsafe setup test database");
        try
        {
            check(LocalConfig.Load()==null,"Primer inicio sin credenciales guardadas");
            var setup=PrepareDialog<ConnectionEditor>(app,async(w,vm)=>
            {
                Fill(w,"Host",p.Host);Fill(w,"Port",p.Port.ToString());Fill(w,"User",p.User);Fill(w,"Database",p.Database);Visuals<PasswordBox>(w).Single().Password=p.Password;
                vm.TestCommand.Execute(null);await Wait(()=>!vm.Busy&&vm.Status.Length>0);
                check(vm.MissingDatabase,"Interfaz prueba conexión y ofrece crear una base inexistente");
                Visuals<CheckBox>(w).Single(c=>c.Content?.ToString()?.StartsWith("Crear la base")==true).IsChecked=true;Save(w);await Wait(()=>!w.IsVisible);
            });
            window=new MainWindow();app.MainWindow=window;window.Show();await setup;
            var shell=(ShellViewModel)window.DataContext;await Wait(()=>shell.Ready&&shell.Tiles.Count==2);
            check(LocalConfig.Load()==p&&new FileInfo(LocalConfig.FilePath).Length>0,"Guardar desde la interfaz conserva credenciales DPAPI y abre el dashboard");
            check(LocalConfig.Preferences().ExportPath==null&&LocalConfig.Preferences().LegacyExportPathMigrated,"Una instalación nueva elige su carpeta y no hereda la ruta de otro equipo");
            var created=await new FinanceStore(p).ReadAsync();check(created.Cards.Count==2&&created.Categories.Count==12&&created.Movements.Count==0,"Base nueva inicializada sin consumos ficticios");
            foreach(var name in new[]{"first","second"})
            {
                var folder=Path.Combine(temp,name);Directory.CreateDirectory(folder);
                await Wait(()=>shell.ExportFolderCommand.CanExecute(null));
                var pending=PrepareDialog<ExportEditor>(app,async(w,vm)=>
                {
                    Fill(w,"Folder",folder);vm.TestCommand.Execute(null);await Wait(()=>!vm.Busy&&vm.Status.Length>0);Save(w);await Wait(()=>!w.IsVisible);
                });
                shell.ExportFolderCommand.Execute(null);await pending;await Wait(()=>File.Exists(Path.Combine(folder,FinanceStore.ExportFileName)));
                check(LocalConfig.Preferences().ExportPath==Path.Combine(folder,FinanceStore.ExportFileName),"Interfaz prueba, guarda y exporta a carpeta "+name);
            }
            var credentials=File.ReadAllBytes(LocalConfig.FilePath);var bad=new ConnectionEditor(p with{Port=1});var dialog=new EditorWindow{DataContext=bad,Owner=window};dialog.Show();bad.TestCommand.Execute(null);await Wait(()=>!bad.Busy&&bad.Error.Length>0);
            check(bad.Error.Contains("MySQL")&&File.ReadAllBytes(LocalConfig.FilePath).SequenceEqual(credentials),"MySQL no disponible: mensaje controlado sin reemplazar la conexión guardada");dialog.Close();
            await DatabaseSetup.PrepareAsync(p,false);check((await new FinanceStore(p).ReadAsync()).Cards.Count==2,"Reconexión de una base existente conserva tarjetas y categorías");
            window.Close();window=null;
        }
        finally
        {
            window?.Close();MySqlConnection.ClearAllPools();await root.ExecuteAsync($"DROP DATABASE IF EXISTS `{db}`");
            if(File.Exists(LocalConfig.FilePath))File.Delete(LocalConfig.FilePath);
            if(File.Exists(LocalConfig.PreferencesPath))File.Delete(LocalConfig.PreferencesPath);
        }
    }
}
