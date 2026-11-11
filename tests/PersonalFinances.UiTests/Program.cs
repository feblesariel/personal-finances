using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dapper;
using MySqlConnector;
using PersonalFinances.App;
using PersonalFinances.Core;

internal static class Program
{
    private static int exitCode;private static readonly List<string> results=[];
    [STAThread] public static int Main()
    {
        CultureInfo.DefaultThreadCurrentCulture=Money.Ar;CultureInfo.DefaultThreadCurrentUICulture=Money.Ar;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage("es-AR")));
        var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown,Resources=new Theme() };
        var trace=new BindingTrace();System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(trace);
        app.DispatcherUnhandledException+=(_,e)=>{if(exitCode==0)Console.WriteLine("UI EXCEPTION: "+e.Exception);e.Handled=true;exitCode=1;};
        app.Startup+=async(_,_)=>{try{await Run(app,trace);}catch(Exception e){Console.WriteLine(e);exitCode=1;}finally{app.Shutdown();}};
        app.Run();return exitCode;
    }
    private static void Check(bool ok,string label){if(!ok)throw new Exception("FAIL: "+label);results.Add(label);Console.WriteLine("PASS UI: "+label);}
    private static async Task Wait(Func<bool> predicate,int ms=15000){var end=DateTime.UtcNow.AddMilliseconds(ms);while(!predicate()){if(DateTime.UtcNow>end)throw new TimeoutException("UI condition timeout");await Task.Delay(60);}}
    private static List<T> Visuals<T>(DependencyObject root) where T:DependencyObject
    {
        var result=new List<T>();for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T t)result.Add(t);result.AddRange(Visuals<T>(child));}return result;
    }
    private static Button Button(Window w,string id)=>Visuals<Button>(w).Single(b=>AutomationProperties.GetAutomationId(b)==id);
    private static void Click(Button b)=>b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); // Routed Click alone does not execute ICommand.
    private static void Invoke(Button b){Check(b.IsVisible&&b.IsEnabled,"Botón disponible: "+b.Content);b.Command.Execute(b.CommandParameter);}
    private static void Screenshot(Window w,string path)
    {
        w.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(w);var image=new RenderTargetBitmap((int)(w.ActualWidth*dpi.DpiScaleX),(int)(w.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);image.Render(w);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=System.IO.File.Create(path);encoder.Save(file);
    }
    private static async Task Run(Application app,BindingTrace trace)
    {
        var admin=new ConnectionProfile("localhost",3306,Environment.GetEnvironmentVariable("PF_ADMIN_USER")??throw new Exception("Missing test user"),Environment.GetEnvironmentVariable("PF_ADMIN_PASSWORD")??throw new Exception("Missing test password"),"");
        await using var root=new MySqlConnection(admin.ConnectionString);await root.OpenAsync();var db="personal_finances_tests_ui_"+DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        await root.ExecuteAsync($"CREATE DATABASE `{db}` CHARACTER SET utf8mb4");var profile=admin with {Database=db};var store=new FinanceStore(profile);
        var artifacts=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../../../artifacts"));System.IO.Directory.CreateDirectory(artifacts);
        var temp=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"PersonalFinancesUi_"+Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(temp);
        var originalConfig=LocalConfig.Folder;LocalConfig.Folder=System.IO.Path.Combine(temp,"profile");
        var originalPreferences=TableLayout.PreferenceDirectory;TableLayout.PreferenceDirectory=System.IO.Path.Combine(temp,"tables");
        MainWindow? window=null;
        try
        {
            await SetupChecks.Run(app,profile,root,temp,Check);
            await store.InitializeAsync();var s=await store.ReadAsync();var visa=s.Cards.Single(c=>c.Name=="VISA");var master=s.Cards.Single(c=>c.Name=="MASTERCARD");
            foreach(var card in s.Cards)await store.SavePeriodAsync(new(){CardId=card.Id,Name="Resumen noviembre 2026",Start=new(2026,10,21),Closing=new(2026,11,20),Due=new(2026,12,1)});
            LocalConfig.SetExportFolder(temp);
            window=new MainWindow(profile);app.MainWindow=window;window.Show();var vm=(ShellViewModel)window.DataContext;
            await Wait(()=>vm.Ready&&vm.Tiles.Count==2);await Wait(()=>System.IO.File.Exists(System.IO.Path.Combine(temp,"RESUMEN_TARJETAS.txt")));Check(vm.Page=="Inicio","Inicio directo en dashboard");
            foreach(var page in new[]{"Consumos","Estadísticas","Tarjetas y períodos","Configuración","Inicio"}){vm.Page=page;window.UpdateLayout();await Task.Delay(50);Check(Visuals<TextBlock>(window).Any(t=>t.IsVisible&&t.Text==page),"Pantalla visible: "+page);}
            var task=new TaskCompletionSource();var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(100)};
            timer.Tick+=async(_,_)=>
            {
                var editor=app.Windows.OfType<EditorWindow>().FirstOrDefault();if(editor==null)return;timer.Stop();
                try
                {
                    var edit=(MovementEditor)editor.DataContext;var input=Visuals<TextBox>(editor).Single(t=>AutomationProperties.GetAutomationId(t)=="AmountInput");
                    Check(input.IsKeyboardFocused,"Foco inicial en importe");input.Text="10.699,00";input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                    var description=Visuals<TextBox>(editor).Single(t=>t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path=="Description"); description.Text="COTO UI";
                    var category=Visuals<ComboBox>(editor).Single(c=>c.ItemsSource==edit.Categories);category.SelectedItem=edit.Categories.Single(c=>c.Name=="Supermercado");
                    Visuals<CheckBox>(editor).Single(c=>c.Content?.ToString()=="Seguir cargando después de guardar").IsChecked=false;
                    Invoke(Button(editor,"SaveButton"));await Wait(()=>!editor.IsVisible);task.SetResult();
                }
                catch(Exception e){task.SetException(e);editor.Close();}
            };
            timer.Start();Invoke(Button(window,"NewMovementButton"));await task.Task;
            await Wait(()=>vm.TotalArs=="$ 10.699,00");Check(vm.TotalUsd=="US$ 0,00","Guardar consumo actualiza dashboard sin mezclar monedas");
            await Wait(()=>System.IO.File.ReadAllText(System.IO.Path.Combine(temp,"RESUMEN_TARJETAS.txt")).Contains("$ 10.699,00"));Check(true,"Guardado WPF actualiza automáticamente TXT");
            s=await store.ReadAsync();var movement=s.Movements.Single();movement.Amount=100m;await store.SaveMovementAsync(movement);
            await store.SaveMovementAsync(new(){CardId=master.Id,CategoryId=s.Categories.First().Id,Date=DateTime.Today,Amount=12.99m,Currency="USD",Description="Suscripción UI"});await vm.RefreshAsync();await vm.Export(true);
            vm.FilterCurrency="USD";vm.Page="Consumos";Check(vm.History.Count==1&&vm.FilteredUsd=="US$ 12,99"&&vm.FilteredArs=="$ 0,00","Filtros de historial y totales separados");
            vm.ClearFiltersCommand.Execute(null);vm.Page="Estadísticas";vm.StatsCurrency="ARS";Check(vm.CategoryStats.Count==1&&vm.CategoryStats[0].Amount=="$ 100,00","Estadística por categoría refleja edición");
            window.UpdateLayout();Check(Visuals<ComboBox>(window).Where(c=>c.ItemsSource is IEnumerable<Choice>).All(c=>!string.IsNullOrWhiteSpace(c.Text)),"Nombres de filtros disponibles para lectura");
            window.Width=1100;window.Height=760;window.UpdateLayout();Screenshot(window,System.IO.Path.Combine(artifacts,"statistics.png"));
            vm.Page="Inicio";window.Width=1320;window.Height=880;window.UpdateLayout();Screenshot(window,System.IO.Path.Combine(artifacts,"dashboard.png"));
            window.Width=960;window.Height=620;window.UpdateLayout();Screenshot(window,System.IO.Path.Combine(artifacts,"dashboard-960.png"));Check(true,"Render a 1320 y 960 píxeles sin excepciones");
            window.Close();window=new MainWindow(profile);app.MainWindow=window;window.Show();var restarted=(ShellViewModel)window.DataContext;await Wait(()=>restarted.Ready&&restarted.TotalArs=="$ 100,00");Check(restarted.TotalUsd=="US$ 12,99","Reabrir interfaz conserva movimientos y totales");
            window=await TableLayoutChecks.Run(window,profile,store,app,artifacts,Check);
            Check(!Visuals<FrameworkElement>(window).Any(e=>Validation.GetHasError(e)),"Controles sin errores de validación WPF"); Check(Visuals<ComboBox>(window).Where(c=>c.ItemsSource is IEnumerable<Choice>).All(c=>c.SelectedItem!=null),"Selectores de filtros con opción visible"); Check(trace.Errors.Count==0,"Enlaces WPF sin errores: "+string.Join(";",trace.Errors.Take(4)));
            await System.IO.File.WriteAllLinesAsync(System.IO.Path.Combine(artifacts,"ui-test-results.txt"),results.Prepend(DateTime.Now.ToString("O")));Console.WriteLine($"RESULT UI: {results.Count} passed, 0 failed");
        }
        finally
        {
            window?.Close();TableLayout.PreferenceDirectory=originalPreferences;LocalConfig.Folder=originalConfig;MySqlConnection.ClearAllPools();if(!db.StartsWith("personal_finances_tests_ui_")||!db[27..].All(char.IsDigit))throw new Exception("Unsafe test cleanup target");await root.ExecuteAsync($"DROP DATABASE `{db}`");
            if(System.IO.Path.GetFullPath(temp).StartsWith(System.IO.Path.GetTempPath(),StringComparison.OrdinalIgnoreCase))System.IO.Directory.Delete(temp,true);
        }
    }
    private sealed class BindingTrace : System.Diagnostics.TraceListener
    {
        public List<string> Errors {get;}=[];public override void Write(string? message){}public override void WriteLine(string? message){if(message?.Contains("Error:")==true)Errors.Add(message);}
    }
}
