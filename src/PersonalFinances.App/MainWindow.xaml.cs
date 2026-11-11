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
}
