using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PersonalFinances.App;
using PersonalFinances.Core;

internal static class TableLayoutChecks
{
    private static List<T> Visuals<T>(DependencyObject parent) where T:DependencyObject
    {
        var list=new List<T>();for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T value)list.Add(value);list.AddRange(Visuals<T>(child));}return list;
    }
    private static string Layout(DataGrid grid)=>JsonSerializer.Serialize(new{Columns=grid.Columns.Select(c=>new{c.Width.Value,c.Width.UnitType,c.DisplayIndex,c.Visibility}),Sort=grid.Items.SortDescriptions.Select(s=>new{s.PropertyName,s.Direction}),grid.FrozenColumnCount,grid.RowHeight});
    private static DataGrid Grid(Window w,string key)=>Visuals<DataGrid>(w).Single(g=>TableLayout.GetKey(g)==key);
    private static void ClickReset(Window w,DataGrid grid)
    {
        var button=Visuals<Button>(w).Single(b=>b.Content?.ToString()=="Restablecer tabla"&&ReferenceEquals(b.CommandTarget,grid));
        ((RoutedCommand)button.Command).Execute(button.CommandParameter,button.CommandTarget);
    }
    private static async Task Wait(Func<bool> predicate){var until=DateTime.UtcNow.AddSeconds(15);while(!predicate()){if(DateTime.UtcNow>until)throw new TimeoutException("Table UI startup timeout");await Task.Delay(50);}}
    public static async Task<MainWindow> Run(MainWindow window,ConnectionProfile profile,FinanceStore store,Application app,string artifacts,Action<bool,string> check)
    {
        var vm=(ShellViewModel)window.DataContext;vm.Page="Consumos";window.UpdateLayout();
        var grids=Visuals<DataGrid>(window);check(grids.Count==5&&grids.All(g=>TableLayout.GetKey(g)!=null),"Cinco tablas principales con comportamiento reutilizable");
        var history=Grid(window,"history");var recent=Grid(window,"recent");var defaults=Layout(history);
        var revision=(await store.ReadAsync()).Revision;var data=JsonSerializer.Serialize((await store.ReadAsync()).Movements);
        history.Columns[0].Width=new(240);history.Columns[1].DisplayIndex=0;history.Columns[2].Visibility=Visibility.Collapsed;history.FrozenColumnCount=1;history.RowHeight=48;
        TableLayout.SetSortChoice(history,"Importe ↓");TableLayout.Save(history);var customized=Layout(history);
        recent.Columns[0].Width=new(211);TableLayout.Save(recent);
        check(File.Exists(TableLayout.PreferencePath("history"))&&File.Exists(TableLayout.PreferencePath("recent")),"Ancho, orden, visibilidad y clasificación guardados por tabla");
        vm.Search="COTO";vm.FilterCard=1;vm.ClearFiltersCommand.Execute(null);
        check(vm.Search==""&&vm.FilterCard==0&&Layout(history)==customized,"Limpiar filtros conserva la disposición y el ordenamiento de la tabla");
        vm.Page="Inicio";vm.Page="Consumos";check(Layout(history)==customized,"Preferencias conservadas durante la navegación");
        window.Close();window=new(profile);app.MainWindow=window;window.Show();vm=(ShellViewModel)window.DataContext;await Wait(()=>vm.Ready&&vm.Tiles.Count==2);vm.Page="Consumos";window.UpdateLayout();
        history=Grid(window,"history");recent=Grid(window,"recent");
        check(Layout(history)==customized&&recent.Columns[0].Width.Value==211,"Preferencias de dos tablas recuperadas al reabrir la ventana");
        vm.FilterCard=1;vm.Search="COTO";var recentLayout=Layout(recent);ClickReset(window,history);
        check(Layout(history)==defaults&&TableLayout.GetSortChoice(history)=="Fecha ↓","Restablecer recupera exactamente ancho, orden, visibilidad, filas y fecha descendente");
        check(vm.FilterCard==1&&vm.Search=="COTO"&&Layout(recent)==recentLayout,"Restablecer conserva filtros y no afecta otra tabla");
        TableLayout.Save(history);check(!File.Exists(TableLayout.PreferencePath("history")),"Restablecer elimina las preferencias personalizadas del archivo local");
        check((await store.ReadAsync()).Revision==revision&&JsonSerializer.Serialize((await store.ReadAsync()).Movements)==data,"Personalizar y restablecer tablas no modifica MySQL");
        window.Close();window=new(profile);app.MainWindow=window;window.Show();vm=(ShellViewModel)window.DataContext;await Wait(()=>vm.Ready&&vm.Tiles.Count==2);vm.Page="Consumos";window.UpdateLayout();
        history=Grid(window,"history");recent=Grid(window,"recent");
        check(Layout(history)==defaults&&recent.Columns[0].Width.Value==211,"Restauración persistente al reabrir e independencia de preferencias");
        foreach(var key in new[]{"recent","cards","periods","categories"})
        {
            var grid=Grid(window,key);ClickReset(window,grid);var original=Layout(grid);grid.Columns[0].Width=new(199);grid.Columns[1].DisplayIndex=0;TableLayout.Save(grid);ClickReset(window,grid);
            check(Layout(grid)==original&&!File.Exists(TableLayout.PreferencePath(key)),"Restablecer tabla funciona en "+key);
        }
        var snapshot=await store.ReadAsync();var editor=new ImportEditor(store,snapshot,["22/10/2026 --- COMPRAS --- 10.699,00 <-- VISA"],new Dialogs());
        var dialog=new EditorWindow{DataContext=editor,Owner=window,Width=1080};dialog.Show();await Task.Delay(80);dialog.UpdateLayout();
        var preview=Grid(dialog,"import-preview");var previewDefault=Layout(preview);preview.Columns[0].Width=new(120);preview.Columns[1].DisplayIndex=0;TableLayout.Save(preview);ClickReset(dialog,preview);
        check(Layout(preview)==previewDefault&&!File.Exists(TableLayout.PreferencePath("import-preview")),"Vista previa de importación restaura columnas y orden original");dialog.Close();
        vm.Page="Estadísticas";window.UpdateLayout();check(!Visuals<TextBlock>(window).Any(t=>t.Text=="Evolución por período"),"Estadísticas sin bloque de evolución y sin espacio reservado");
        vm.Page="Consumos";window.Width=960;window.Height=620;window.UpdateLayout();
        var search=Visuals<TextBox>(window).First(t=>t.IsVisible&&System.Windows.Automation.AutomationProperties.GetName(t)=="Buscar movimientos");
        var clear=Visuals<Button>(window).Single(b=>b.IsVisible&&b.Content?.ToString()=="Limpiar filtros");
        var reset=Visuals<Button>(window).Single(b=>b.IsVisible&&b.Content?.ToString()=="Restablecer tabla");
        Capture(window,Path.Combine(artifacts,"consumos-960.png"));
        check(Math.Abs(search.ActualHeight-clear.ActualHeight)<1&&Math.Abs(clear.ActualHeight-reset.ActualHeight)<1,$"Buscar, limpiar filtros y restablecer con alturas uniformes ({search.ActualHeight}/{clear.ActualHeight}/{reset.ActualHeight})");
        check(history.ActualHeight>=200,"Tabla legible en ventana de 960×620, con desplazamiento para acceder al resto");
        var visible=Visuals<Button>(window).Where(b=>b.IsVisible&&b.ActualWidth>0).ToList();
        check(visible.All(b=>b.TransformToAncestor(window).Transform(new Point(0,0)).X+b.ActualWidth<=window.ActualWidth+1),"Botones sin desborde horizontal a 960 píxeles");
        Capture(window,Path.Combine(artifacts,"consumos-960.png"));window.Width=1320;window.Height=880;window.UpdateLayout();Capture(window,Path.Combine(artifacts,"consumos.png"));
        vm.Page="Estadísticas";window.UpdateLayout();Capture(window,Path.Combine(artifacts,"statistics.png"));vm.Page="Configuración";window.UpdateLayout();Capture(window,Path.Combine(artifacts,"configuration.png"));vm.Page="Inicio";
        return window;
    }
    private static void Capture(Window window,string path)
    {
        var dpi=VisualTreeHelper.GetDpi(window);var bmp=new RenderTargetBitmap((int)(window.ActualWidth*dpi.DpiScaleX),(int)(window.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);bmp.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var file=File.Create(path);encoder.Save(file);
    }
}
