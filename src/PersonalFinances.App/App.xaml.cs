using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using PersonalFinances.Core;

namespace PersonalFinances.App;
public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);CultureInfo.DefaultThreadCurrentCulture=Money.Ar;CultureInfo.DefaultThreadCurrentUICulture=Money.Ar;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("es-AR")));
        instance=new Mutex(true,"Local\\PersonalFinances_"+Environment.UserName,out var first);
        if(!first){MessageBox.Show("Personal Finances ya está abierto. Buscalo en la barra de tareas.","Personal Finances");Shutdown();return;}
        DispatcherUnhandledException+=(_,args)=>{LocalConfig.Log(args.Exception);if(MainWindow?.DataContext is ShellViewModel vm)vm.Notice=LocalConfig.Friendly(args.Exception);args.Handled=true;};
        TaskScheduler.UnobservedTaskException+=(_,args)=>{LocalConfig.Log(args.Exception);args.SetObserved();};
        var window=new MainWindow();MainWindow=window;window.Show();
    }
    protected override void OnExit(ExitEventArgs e){instance?.Dispose();base.OnExit(e);}
    private void ConnectionPasswordChanged(object sender,RoutedEventArgs e){if(sender is PasswordBox {DataContext:ConnectionEditor vm} p)vm.Password=p.Password;}
}
