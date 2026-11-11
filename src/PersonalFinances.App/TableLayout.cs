using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PersonalFinances.Core;

namespace PersonalFinances.App;

// Presentation-only behavior shared by every interactive grid. No repository or SQL dependency.
public static class TableLayout
{
    public static string PreferenceDirectory {get;set;}=Path.Combine(LocalConfig.Folder,"Tables");
    public static readonly RoutedUICommand ResetCommand=new("Restablecer tabla",nameof(ResetCommand),typeof(TableLayout));
    public static readonly DependencyProperty KeyProperty=DependencyProperty.RegisterAttached("Key",typeof(string),typeof(TableLayout),new PropertyMetadata(null,Attach));
    public static readonly DependencyProperty DefaultSortProperty=DependencyProperty.RegisterAttached("DefaultSort",typeof(string),typeof(TableLayout),new PropertyMetadata(""));
    public static readonly DependencyProperty SortChoiceProperty=DependencyProperty.RegisterAttached("SortChoice",typeof(string),typeof(TableLayout),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,SortChoiceChanged));
    private static readonly ConditionalWeakTable<DataGrid,State> States=new();
    public static string? GetKey(DependencyObject o)=>(string?)o.GetValue(KeyProperty);
    public static void SetKey(DependencyObject o,string value)=>o.SetValue(KeyProperty,value);
    public static string GetDefaultSort(DependencyObject o)=>(string)o.GetValue(DefaultSortProperty);
    public static void SetDefaultSort(DependencyObject o,string value)=>o.SetValue(DefaultSortProperty,value);
    public static string? GetSortChoice(DependencyObject o)=>(string?)o.GetValue(SortChoiceProperty);
    public static void SetSortChoice(DependencyObject o,string? value)=>o.SetValue(SortChoiceProperty,value);
    public static void Reset(DataGrid grid){if(States.TryGetValue(grid,out var s))s.Reset();}
    public static void Save(DataGrid grid){if(States.TryGetValue(grid,out var s))s.Save();}
    public static string PreferencePath(string key)=>Path.Combine(PreferenceDirectory,key+".json");
    private static void Attach(DependencyObject o,DependencyPropertyChangedEventArgs e)
    {
        if(o is not DataGrid grid)return;
        if(e.OldValue!=null)throw new InvalidOperationException("La clave de una tabla no puede cambiar durante su uso.");
        if(e.NewValue is not string key||key.Length==0||key.Any(c=>!(char.IsAsciiLetterOrDigit(c)||c is '-' or '_')))throw new ArgumentException("Clave de tabla inválida.");
        grid.Loaded+=(_,_)=>States.GetValue(grid,g=>new(g,key)).Activate();
        grid.Unloaded+=(_,_)=>{if(States.TryGetValue(grid,out var state))state.Deactivate();};
        grid.CommandBindings.Add(new CommandBinding(ResetCommand,(_,args)=>{Reset(grid);args.Handled=true;},(_,args)=>{args.CanExecute=true;args.Handled=true;}));
    }
    private static void SortChoiceChanged(DependencyObject o,DependencyPropertyChangedEventArgs e)
    {
        if(o is DataGrid grid&&States.TryGetValue(grid,out var s))s.ChooseSort(e.NewValue as string);
    }
    private sealed record ColumnState(string Key,double Width,DataGridLengthUnitType Unit,int DisplayIndex,Visibility Visibility);
    private sealed record SortState(string Property,ListSortDirection Direction);
    private sealed record Layout(int Version,List<ColumnState> Columns,List<SortState> Sort,int Frozen,double? RowHeight);
    private sealed class State
    {
        private readonly DataGrid grid;private readonly string path;
        private readonly Layout defaults;private readonly DispatcherTimer timer;
        private readonly Dictionary<DataGridColumn,string> columnKeys;
        private bool applying,active;
        private static readonly DependencyPropertyDescriptor WidthDescriptor=DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty,typeof(DataGridColumn));
        public State(DataGrid grid,string key)
        {
            this.grid=grid;path=PreferencePath(key);
            // WPF initializes display indices lazily for grids on hidden pages.
            // Establish the declared order before capturing any defaults or loading preferences.
            for(var i=0;i<grid.Columns.Count;i++)if(grid.Columns[i].DisplayIndex<0)grid.Columns[i].DisplayIndex=i;
            columnKeys=grid.Columns.Select((c,i)=>(Column:c,Key:ColumnKey(c,i))).ToDictionary(x=>x.Column,x=>x.Key);
            defaults=Capture() with {Sort=ParseSort(GetDefaultSort(grid))};
            timer=new DispatcherTimer(DispatcherPriority.Background,grid.Dispatcher){Interval=TimeSpan.FromMilliseconds(350)};
            timer.Tick+=(_,_)=>{timer.Stop();Save();};
        }
        private static string ColumnKey(DataGridColumn column,int index)=>!string.IsNullOrEmpty(column.SortMemberPath)?column.SortMemberPath:
            column is DataGridBoundColumn {Binding:Binding {Path: {} p}}?p.Path:"column-"+index;
        private static List<SortState> ParseSort(string text)=>text.Split(';',StringSplitOptions.RemoveEmptyEntries).Select(part=>
        {
            var pair=part.Split(':');return new SortState(pair[0],pair.Length>1&&pair[1]=="Ascending"?ListSortDirection.Ascending:ListSortDirection.Descending);
        }).ToList();
        public void Activate()
        {
            if(active)return;active=true;
            Apply(defaults);
            if(File.Exists(path))
            {
                try
                {
                    var saved=JsonSerializer.Deserialize<Layout>(File.ReadAllText(path));
                    var allowedSort=defaults.Sort.Select(s=>s.Property).Concat(columnKeys.Values).ToHashSet();
                    if(saved is {Version:1}&&saved.Columns.Count==defaults.Columns.Count&&saved.Columns.Select(c=>c.Key).ToHashSet().SetEquals(columnKeys.Values)
                        &&saved.Sort.All(s=>allowedSort.Contains(s.Property))&&saved.Columns.Select(c=>c.DisplayIndex).Order().SequenceEqual(Enumerable.Range(0,grid.Columns.Count))
                        &&saved.Columns.All(c=>double.IsFinite(c.Width)&&c.Width>=0&&Enum.IsDefined(c.Unit)&&Enum.IsDefined(c.Visibility))&&saved.Frozen>=0&&saved.Frozen<=grid.Columns.Count)
                        Apply(saved);
                }
                catch(Exception e){LocalConfig.Log(e);Apply(defaults);}
            }
            foreach(var column in grid.Columns)WidthDescriptor.AddValueChanged(column,Changed);
            grid.ColumnDisplayIndexChanged+=ColumnChanged;grid.Sorting+=Sorting;
        }
        public void Deactivate()
        {
            if(!active)return;timer.Stop();Save();active=false;
            foreach(var column in grid.Columns)WidthDescriptor.RemoveValueChanged(column,Changed);
            grid.ColumnDisplayIndexChanged-=ColumnChanged;grid.Sorting-=Sorting;
        }
        private void Changed(object? sender,EventArgs e)=>Schedule();
        private void ColumnChanged(object? sender,DataGridColumnEventArgs e)=>Schedule();
        private void Sorting(object sender,DataGridSortingEventArgs e)=>Schedule();
        private void Schedule(){if(applying||!active)return;timer.Stop();timer.Start();}
        private Layout Capture()=>new(1,grid.Columns.Select(c=>new ColumnState(columnKeys.GetValueOrDefault(c)??ColumnKey(c,grid.Columns.IndexOf(c)),c.Width.Value,c.Width.UnitType,c.DisplayIndex<0?grid.Columns.IndexOf(c):c.DisplayIndex,c.Visibility)).ToList(),
            grid.Items.SortDescriptions.Select(s=>new SortState(s.PropertyName,s.Direction)).ToList(),grid.FrozenColumnCount,double.IsFinite(grid.RowHeight)?grid.RowHeight:null);
        private void Apply(Layout layout)
        {
            applying=true;
            try
            {
                foreach(var item in layout.Columns)
                {
                    var column=columnKeys.Single(p=>p.Value==item.Key).Key;column.Width=new(item.Width,item.Unit);column.Visibility=item.Visibility;
                }
                foreach(var item in layout.Columns.OrderBy(c=>c.DisplayIndex))columnKeys.Single(p=>p.Value==item.Key).Key.DisplayIndex=item.DisplayIndex;
                grid.FrozenColumnCount=layout.Frozen;grid.RowHeight=layout.RowHeight??double.NaN;ApplySort(layout.Sort);
            }
            finally{applying=false;}
        }
        private void ApplySort(List<SortState> sort)
        {
            using(grid.Items.DeferRefresh())
            {
                grid.Items.SortDescriptions.Clear();foreach(var s in sort)grid.Items.SortDescriptions.Add(new(s.Property,s.Direction));
            }
            foreach(var column in grid.Columns)column.SortDirection=sort.FirstOrDefault(s=>s.Property==columnKeys[column])?.Direction;
            UpdateChoice(sort);
        }
        private void UpdateChoice(List<SortState> sort)
        {
            var first=sort.FirstOrDefault();var text=first?.Property switch {"Date"=>first.Direction==ListSortDirection.Descending?"Fecha ↓":"Fecha ↑","Signed"=>first.Direction==ListSortDirection.Descending?"Importe ↓":"Importe ↑",_=>"Personalizado"};
            grid.SetCurrentValue(SortChoiceProperty,text);
        }
        public void ChooseSort(string? choice)
        {
            if(applying||!active)return;
            var sort=choice switch
            {
                "Fecha ↓"=>ParseSort("Date:Descending;Id:Descending"),"Fecha ↑"=>ParseSort("Date:Ascending;Id:Ascending"),
                "Importe ↓"=>ParseSort("Signed:Descending;Id:Descending"),"Importe ↑"=>ParseSort("Signed:Ascending;Id:Ascending"),_=>null
            };
            if(sort==null)return;applying=true;try{ApplySort(sort);}finally{applying=false;}Schedule();
        }
        public void Reset()
        {
            timer.Stop();Apply(defaults);Save();
            grid.UnselectAll();var scroll=FindScroll(grid);scroll?.ScrollToHorizontalOffset(0);scroll?.ScrollToVerticalOffset(0);
        }
        public void Save()
        {
            if(applying)return;
            try
            {
                var layout=Capture();applying=true;try{UpdateChoice(layout.Sort);}finally{applying=false;}
                if(JsonSerializer.Serialize(layout)==JsonSerializer.Serialize(defaults)){if(File.Exists(path))File.Delete(path);return;}
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temp=path+".tmp";
                File.WriteAllText(temp,JsonSerializer.Serialize(layout));File.Move(temp,path,true);
            }
            catch(Exception e){LocalConfig.Log(e);}
        }
        private static ScrollViewer? FindScroll(DependencyObject parent)
        {
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is ScrollViewer scroll)return scroll;var found=FindScroll(child);if(found!=null)return found;}return null;
        }
    }
}
