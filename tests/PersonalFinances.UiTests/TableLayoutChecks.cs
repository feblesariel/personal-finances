using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dapper;
using PersonalFinances.App;
using PersonalFinances.Core;

internal static class TableLayoutChecks
{
    private static string Layout(DataGrid grid)=>JsonSerializer.Serialize(new{Columns=grid.Columns.Select(c=>new{c.Width.Value,c.Width.UnitType,c.DisplayIndex,c.Visibility}),Sort=grid.Items.SortDescriptions.Select(s=>new{s.PropertyName,s.Direction}),grid.FrozenColumnCount,grid.RowHeight});
    private static DataGrid History(Window window)=>InteractionChecks.Visuals<DataGrid>(window).Single(g=>g.Name=="HistoryGrid");
    private static void ClickReset(Window window,DataGrid grid)
    {
        var button=InteractionChecks.Visuals<Button>(window).Single(b=>b.Content?.ToString()=="Restablecer tabla"&&ReferenceEquals(b.CommandTarget,grid));
        ((RoutedCommand)button.Command).Execute(button.CommandParameter,button.CommandTarget);
    }
    private static async Task Wait(Func<bool> predicate){var until=DateTime.UtcNow.AddSeconds(15);while(!predicate()){if(DateTime.UtcNow>until)throw new TimeoutException("Table startup timeout");await Task.Delay(50);}}
    public static async Task<MainWindow> Run(MainWindow window,ConnectionProfile profile,FinanceStore store,Application app,string artifacts,Action<bool,string> check)
    {
        var vm=(ShellViewModel)window.DataContext;vm.Page="Consumos";window.UpdateLayout();
        var grids=InteractionChecks.Visuals<DataGrid>(window);var history=History(window);
        check(grids.Count==7&&grids.Count(g=>TableLayout.GetKey(g)!=null)==1&&TableLayout.GetKey(history)=="history","Solo Consumos conserva preferencias personalizadas de columnas");
        check(grids.Where(g=>g!=history&&g.Name!="ServicesGrid").All(g=>!g.CanUserResizeColumns&&!g.CanUserReorderColumns&&!g.CanUserSortColumns),"Tablas sencillas de consulta sin personalización innecesaria");
        var defaults=Layout(history);var before=await store.ReadAsync();var revision=before.Revision;var data=JsonSerializer.Serialize(before.Movements);
        history.Columns[0].Width=new(240);history.Columns[1].DisplayIndex=0;history.Columns[2].Visibility=Visibility.Collapsed;history.FrozenColumnCount=1;history.RowHeight=48;
        TableLayout.SetSortChoice(history,"Importe ↓");TableLayout.Save(history);var customized=Layout(history);
        check(File.Exists(TableLayout.PreferencePath("history")),"Disposición personalizada de Consumos guardada localmente");
        vm.Search="COTO";vm.ClearFiltersCommand.Execute(null);check(vm.Search==""&&Layout(history)==customized,"Limpiar filtros conserva la disposición y el ordenamiento de Consumos");
        vm.Page="Inicio";vm.Page="Consumos";check(Layout(history)==customized,"Preferencias conservadas durante la navegación");
        window.Close();window=new(profile);app.MainWindow=window;window.Show();vm=(ShellViewModel)window.DataContext;await Wait(()=>vm.Ready&&vm.Tiles.Count==2);vm.Page="Consumos";window.UpdateLayout();history=History(window);
        check(Layout(history)==customized,"Preferencias de Consumos recuperadas al reabrir");
        vm.FilterCard=1;vm.Search="COTO";ClickReset(window,history);
        check(Layout(history)==defaults&&TableLayout.GetSortChoice(history)=="Fecha ↓","Restablecer recupera anchos, orden, visibilidad y fecha descendente de Consumos");
        check(vm.FilterCard==1&&vm.Search=="COTO","Restablecer tabla conserva los filtros de datos");
        TableLayout.Save(history);check(!File.Exists(TableLayout.PreferencePath("history")),"Restablecer elimina únicamente la preferencia visual de Consumos");
        check((await store.ReadAsync()).Revision==revision&&JsonSerializer.Serialize((await store.ReadAsync()).Movements)==data,"Cambiar o restaurar columnas no modifica MySQL");
        window.Close();window=new(profile);app.MainWindow=window;window.Show();vm=(ShellViewModel)window.DataContext;await Wait(()=>vm.Ready&&vm.Tiles.Count==2);vm.Page="Consumos";window.UpdateLayout();history=History(window);
        check(Layout(history)==defaults,"Restauración de Consumos persistente tras cerrar y reabrir");
        check(InteractionChecks.Visuals<Button>(window).Count(b=>b.Content?.ToString()=="Restablecer tabla")==1,"Un solo botón Restablecer tabla en toda la ventana");
        vm.Page="Estadísticas";window.UpdateLayout();check(!InteractionChecks.Visuals<TextBlock>(window).Any(t=>t.Text=="Evolución por período"),"Estadísticas conserva la eliminación del bloque de evolución");
        vm.Page="Consumos";window.Width=960;window.Height=620;window.UpdateLayout();
        var search=InteractionChecks.Visuals<TextBox>(window).Single(t=>t.IsVisible&&System.Windows.Automation.AutomationProperties.GetName(t)=="Buscar movimientos");
        var clear=InteractionChecks.Visuals<Button>(window).Single(b=>b.IsVisible&&b.Content?.ToString()=="Limpiar filtros");
        var reset=InteractionChecks.Visuals<Button>(window).Single(b=>b.IsVisible&&b.Content?.ToString()=="Restablecer tabla");
        check(Math.Abs(search.ActualHeight-clear.ActualHeight)<1&&Math.Abs(clear.ActualHeight-reset.ActualHeight)<1,"Buscador y botones de Consumos con altura uniforme");
        check(history.ActualHeight>=200,"Historial legible a 960×620 con scroll vertical");
        window.Width=1320;window.Height=880;vm.ClearFiltersCommand.Execute(null);vm.Page="Inicio";return window;
    }
}
