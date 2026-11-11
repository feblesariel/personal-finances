using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace PersonalFinances.App;
public partial class MainWindow : Window
{
    private readonly ShellViewModel vm;private readonly DispatcherTimer timer;
    public MainWindow(PersonalFinances.Core.ConnectionProfile? profile=null)
    {
        InitializeComponent();vm=new(new Dialogs(),profile);DataContext=vm;
        timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(30)};timer.Tick+=async(_,_)=>await vm.Export();
        Loaded+=async(_,_)=>{MaxHeight=SystemParameters.WorkArea.Height;MaxWidth=SystemParameters.WorkArea.Width;Height=Math.Min(Height,MaxHeight);Width=Math.Min(Width,MaxWidth);await vm.InitializeAsync();timer.Start();};Closed+=(_,_)=>timer.Stop();
    }
    private void MovementDoubleClick(object sender,MouseButtonEventArgs e)
    {
        if(sender is DataGrid {SelectedItem:PersonalFinances.Core.Movement m}) {vm.SelectedMovement=m;vm.EditCommand.Execute(null);}
    }
    private void OpenServiceRow(object sender,MouseButtonEventArgs e)
    {
        var target=e.OriginalSource as DependencyObject;
        while(target!=null&&target is not DataGridRow)target=System.Windows.Media.VisualTreeHelper.GetParent(target);
        if(target is not DataGridRow row)return;
        ServicesGrid.SelectedItem=row.Item;vm.OpenServiceCommand.Execute(null);e.Handled=true;
    }
    private void ServiceKeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Enter&&ServicesGrid.SelectedItem!=null){vm.OpenServiceCommand.Execute(null);e.Handled=true;}}
    private void ResetServices(object sender,RoutedEventArgs e)=>TableLayout.Reset(ServicesGrid);
}