using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using System.Globalization;
using System.IO;
using ShapePath=System.Windows.Shapes.Path;
using PersonalFinances.App;

internal static class InteractionChecks
{
    internal static List<T> Visuals<T>(DependencyObject root) where T:DependencyObject
    {
        var result=new List<T>();for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T item)result.Add(item);result.AddRange(Visuals<T>(child));}return result;
    }
    public static async Task ProbeSearch(MainWindow window,ShellViewModel vm,Action<bool,string> check)
    {
        vm.Page="Consumos";window.UpdateLayout();
        var search=Visuals<TextBox>(window).Single(t=>t.IsVisible&&AutomationProperties.GetName(t)=="Buscar movimientos");
        search.Focus();search.Text="COTO";await Task.Delay(300);window.UpdateLayout();
        var host=(ScrollViewer)search.Template.FindName("PART_ContentHost",search);
        Console.WriteLine($"SEARCH DIAGNOSTIC: Text={search.Text}; Foreground={search.Foreground}; Padding={search.Padding}; HostMargin={host.Margin}; HostPadding={host.Padding}; Viewport={host.ViewportHeight}; Character={search.GetRectFromCharacterIndex(0)}");
        check(host.ViewportHeight>=14&&!search.GetRectFromCharacterIndex(0).IsEmpty,"Buscador tiene espacio real para texto y cursor");
        vm.ClearFiltersCommand.Execute(null);vm.Page="Inicio";
    }
    public static async Task Run(MainWindow window,ShellViewModel vm,Action<bool,string> check,string artifacts)
    {
        var nav=Visuals<NavigationButton>(window);window.UpdateLayout();
        check(nav.Count==6&&nav.All(n=>ReferenceEquals(n.Style,nav[0].Style)&&ReferenceEquals(n.Template,nav[0].Template)),"Sidebar usa un único componente, estilo y plantilla para las seis rutas");
        check(nav.All(n=>n.Height==44&&n.Padding==nav[0].Padding&&n.Margin==nav[0].Margin),"Botones laterales con altura, padding y separación idénticos");
        var glyphs=nav.Select(n=>(ShapePath)n.Template.FindName("IconGlyph",n)).ToList();
        check(glyphs.All(g=>g.ActualWidth==18&&g.ActualHeight==18&&g.Stretch==Stretch.Uniform&&Math.Abs(g.Data.Bounds.Width-18)<0.01&&Math.Abs(g.Data.Bounds.Height-18)<0.01),"Seis iconos vectoriales normalizados en 18×18, sin caracteres Unicode");
        void CheckAlignment()
        {
            window.UpdateLayout();var labels=nav.Select(n=>(TextBlock)n.Template.FindName("NavLabel",n)).ToList();var left=labels.Select(l=>l.TransformToAncestor(window).Transform(new Point()).X).ToList();
            check(left.Max()-left.Min()<0.1,"Textos del sidebar comienzan exactamente en la misma coordenada horizontal");
            check(labels.All(l=>new FormattedText(l.Text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(l.FontFamily,l.FontStyle,l.FontWeight,l.FontStretch),l.FontSize,l.Foreground,VisualTreeHelper.GetDpi(l).PixelsPerDip).WidthIncludingTrailingWhitespace<=l.ActualWidth+0.5),"Etiquetas completas del menú, sin cortes");
        }
        CheckAlignment();
        foreach(var button in nav){button.Command.Execute(button.CommandParameter);window.UpdateLayout();check(vm.Page==button.Route&&nav.Count(n=>n.IsActive)==1&&button.IsActive,"Navegación y selección activa uniformes: "+button.Route);}
        check(Visuals<Button>(window).Count(b=>b.Content?.ToString()=="Restablecer tabla")==2,"Restablecer tabla disponible en Consumos y Servicios");
        check(!Visuals<TextBlock>(window).Any(t=>t.Text=="Importar tu TXT anterior")&&!Visuals<Button>(window).Any(b=>b.Content?.ToString()=="Seleccionar TXT y revisar"),"Sección de importación retirada completamente de Configuración");
        vm.Page="Consumos";vm.ClearFiltersCommand.Execute(null);window.Activate();window.UpdateLayout();
        var all=vm.History.Count;var search=Visuals<TextBox>(window).Single(t=>t.IsVisible&&AutomationProperties.GetName(t)=="Buscar movimientos");
        search.Focus();ApplicationCommands.SelectAll.Execute(null,search);
        foreach(var letter in "COTO")TextCompositionManager.StartComposition(new TextComposition(InputManager.Current,search,letter.ToString()));
        await Task.Delay(100);window.UpdateLayout();
        check(search.Text=="COTO"&&vm.Search=="COTO"&&search.CaretIndex==4,"Escritura real del control conserva texto y posición del cursor");
        check(vm.History.Count==1&&vm.History[0].Description=="COTO UI"&&search.IsKeyboardFocused,"Buscar filtra correctamente sin perder el foco");
        var host=(ScrollViewer)search.Template.FindName("PART_ContentHost",search);var caret=search.GetRectFromCharacterIndex(0);var hint=(TextBlock)search.Template.FindName("InputHint",search);
        check(host.ViewportHeight>=caret.Height&&hint.Visibility==Visibility.Collapsed&&search.Foreground.ToString()=="#FF17253C"&&search.CaretBrush.ToString()=="#FF17253C","Texto y caret visibles: espacio suficiente, contraste y placeholder oculto al escribir");
        Capture(window,System.IO.Path.Combine(artifacts,"search-focused.png"));nav[0].Focus();window.UpdateLayout();check(search.Text=="COTO"&&!search.IsKeyboardFocused&&vm.History.Count==1,"Texto y resultados se conservan al quitar el foco");
        Capture(window,System.IO.Path.Combine(artifacts,"search-unfocused.png"));search.Focus();EditingCommands.Backspace.Execute(null,search);
        check(search.Text=="COT","Backspace edita el texto normalmente");ApplicationCommands.SelectAll.Execute(null,search);EditingCommands.Backspace.Execute(null,search);window.UpdateLayout();
        check(search.Text==""&&vm.Search==""&&vm.History.Count==all&&hint.Visibility==Visibility.Visible,"Ctrl+A y borrar recuperan la lista completa y el placeholder");
        vm.Search="sin-coincidencias";check(vm.History.Count==0&&vm.FilterSummary.Contains("filtros activos")&&vm.HistoryEmptyMessage.Contains("Limpiar filtros"),"Cero resultados explica el filtro activo y ofrece recuperar el historial");
        vm.Page="Estadísticas";window.UpdateLayout();
        check(Visuals<ComboBox>(window).Count(c=>c.IsVisible)==3&&!Visuals<TextBox>(window).Any(t=>t.IsVisible),"Estadísticas presenta solo tarjeta, período y moneda, sin buscador ni categoría");
        var currency=Visuals<ComboBox>(window).Single(c=>c.IsVisible&&AutomationProperties.GetName(c)=="Moneda de estadísticas");
        check(currency.Items.Cast<string>().SequenceEqual(new[]{"ARS","USD"}),"Moneda única de estadísticas contiene solo ARS y USD");
        vm.StatsCard=2;vm.StatsCurrency="USD";check(vm.CategoryStats.Single().Amount=="US$ 12,99"&&vm.CardStats.Single().Name=="MASTERCARD"&&vm.StatisticsUsd=="US$ 12,99","Tarjeta y moneda actualizan ambos gráficos y sus totales");
        check(vm.History.Count==0&&vm.Search=="sin-coincidencias","Cambiar estadísticas no modifica filtros del historial");
        vm.ClearFiltersCommand.Execute(null);check(vm.History.Count==all&&vm.StatsCard==2&&vm.StatsCurrency=="USD","Limpiar Consumos recupera todos los movimientos sin alterar Estadísticas");
        vm.StatsCard=0;var visaPeriod=vm.StatisticsPeriodChoices.Single(p=>p.Id==1);vm.StatisticsPeriodFilter=visaPeriod;vm.StatsCurrency="ARS";
        check(vm.CategoryStats.Single().Amount=="$ 100,00"&&vm.CardStats.Single().Name=="VISA"&&vm.StatisticsArs=="$ 100,00"&&vm.StatisticsUsd=="US$ 0,00","El período restringe estadísticas a sus movimientos y separa monedas");
        vm.ClearStatisticsFiltersCommand.Execute(null);check(vm.StatsCard==0&&vm.StatsPeriod==0&&vm.StatsCurrency=="ARS"&&vm.StatisticsArs=="$ 100,00"&&vm.StatisticsUsd=="US$ 12,99","Limpiar estadísticas restaura Todas / Todos / ARS y mantiene los dos saldos separados");
        vm.FilterCurrency="USD";vm.FilterCategory=1;vm.Search="sin-coincidencias";vm.ClearFiltersCommand.Execute(null);
        check(vm.History.Count==all&&!vm.HasHistoryFilters,"Todos los filtros y búsqueda vacíos siempre muestran todos los registros cargados");
        window.Width=960;window.Height=620;window.UpdateLayout();CheckAlignment();vm.Page="Consumos";window.UpdateLayout();Capture(window,System.IO.Path.Combine(artifacts,"consumos-960.png"));
        window.WindowState=WindowState.Maximized;await Task.Delay(120);window.UpdateLayout();CheckAlignment();window.WindowState=WindowState.Normal;window.Width=1320;window.Height=880;await Task.Delay(120);window.UpdateLayout();
        check(vm.History.Count==all&&nav.All(n=>n.ActualHeight==44),"Maximizar y restaurar conserva alineación, navegación y datos visibles");
        Capture(window,System.IO.Path.Combine(artifacts,"consumos.png"));vm.Page="Estadísticas";window.UpdateLayout();Capture(window,System.IO.Path.Combine(artifacts,"statistics.png"));vm.Page="Configuración";window.UpdateLayout();Capture(window,System.IO.Path.Combine(artifacts,"configuration.png"));vm.Page="Inicio";window.UpdateLayout();Capture(window,System.IO.Path.Combine(artifacts,"dashboard.png"));
    }
    private static void Capture(Window window,string path)
    {
        var dpi=VisualTreeHelper.GetDpi(window);var bmp=new RenderTargetBitmap((int)(window.ActualWidth*dpi.DpiScaleX),(int)(window.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);bmp.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var file=File.Create(path);encoder.Save(file);
    }
}
