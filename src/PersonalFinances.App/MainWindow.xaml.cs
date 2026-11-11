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
    private bool sheetSaving,sheetCommitting;
    private async void ServiceRowEnding(object sender,DataGridRowEditEndingEventArgs e)
    {
        if(sheetCommitting||e.EditAction!=DataGridEditAction.Commit||e.Row.Item is not ServiceSheetRow row||!row.Dirty)return;
        e.Cancel=true;if(sheetSaving)return;sheetSaving=true;
        try
        {
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Background);
            if(await vm.SaveSheetRowAsync(row)){sheetCommitting=true;try{ServicesGrid.CommitEdit(DataGridEditingUnit.Row,true);ServicesGrid.Items.Refresh();}finally{sheetCommitting=false;}}
        }
        finally{sheetSaving=false;}
    }
    private void ServicePreparingCell(object sender,DataGridPreparingCellForEditEventArgs e)
    {
        Dispatcher.BeginInvoke(()=>{var input=FindSheetInput(e.EditingElement);if(input!=null){input.Focus();input.SelectAll();}},DispatcherPriority.Input);
    }
    private static TextBox? FindSheetInput(DependencyObject root)
    {
        if(root is TextBox box)return box;
        for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)
        {var found=FindSheetInput(System.Windows.Media.VisualTreeHelper.GetChild(root,i));if(found!=null)return found;}
        return null;
    }
    private void ServiceBeginningEdit(object sender,DataGridBeginningEditEventArgs e){if(sheetSaving||e.Row.Item is ServiceSheetRow {Applicable:false})e.Cancel=true;}
    private void ServiceKeyDown(object sender,KeyEventArgs e)
    {
        if(sheetSaving){e.Handled=true;return;}
        if(e.Key==Key.Enter){ServicesGrid.CommitEdit(DataGridEditingUnit.Cell,true);ServicesGrid.CommitEdit(DataGridEditingUnit.Row,true);e.Handled=true;}
        else if(e.Key==Key.Escape){ServicesGrid.CancelEdit(DataGridEditingUnit.Cell);ServicesGrid.CancelEdit(DataGridEditingUnit.Row);e.Handled=true;}
    }
    private void ServiceContextRow(object sender,MouseButtonEventArgs e)
    {
        var target=e.OriginalSource as DependencyObject;
        while(target!=null&&target is not DataGridRow)target=System.Windows.Media.VisualTreeHelper.GetParent(target);
        if(target is DataGridRow row)ServicesGrid.SelectedItem=row.Item;
    }
    private async Task<bool> FinishSheetEdit()
    {
        ServicesGrid.CommitEdit(DataGridEditingUnit.Cell,true);
        var draft=(ServicesGrid.Items as System.ComponentModel.IEditableCollectionView)?.CurrentEditItem as ServiceSheetRow;
        if(draft?.Dirty==true&&!await vm.SaveSheetRowAsync(draft))return false;
        sheetCommitting=true;try{ServicesGrid.CommitEdit(DataGridEditingUnit.Row,true);ServicesGrid.Items.Refresh();}finally{sheetCommitting=false;}
        return true;
    }
    private async void ResetServices(object sender,RoutedEventArgs e){if(await FinishSheetEdit())TableLayout.Reset(ServicesGrid);}
    private async void ServiceActions(object sender,RoutedEventArgs e)
    {
        if(sender is FrameworkElement {DataContext:ServiceSheetRow row})ServicesGrid.SelectedItem=row;
        if(!await FinishSheetEdit())return;
        ServicesGrid.ContextMenu.PlacementTarget=ServicesGrid;ServicesGrid.ContextMenu.IsOpen=true;
    }}
