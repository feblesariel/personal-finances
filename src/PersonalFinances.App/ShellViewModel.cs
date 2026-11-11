using System.Collections.ObjectModel;
using System.Windows.Input;
using PersonalFinances.Core;

namespace PersonalFinances.App;
public sealed class Choice(int id,string name) : Observable {private string label=name;public int Id {get;}=id;public string Name {get=>label;set=>Set(ref label,value);}public override string ToString()=>Name;}
public sealed class CardTile : Observable
{
    private bool selected=true;private readonly Action changed;
    public int Id {get;init;} public string Name {get;init;}="";public string Period {get;init;}="";public string Dates {get;init;}="";public string Due {get;init;}="";public string Ars {get;init;}="";public string Usd {get;init;}="";public string Count {get;init;}="";
    public bool Selected {get=>selected;set{if(Set(ref selected,value))changed();}}
    public CardTile(Action changed)=>this.changed=changed;
}
public sealed record StatRow(string Name,string Amount,double Width,string Percent);
public sealed record PeriodDisplay(Period Model,string Card,string Dates,string Due)
{
    public string Name=>Model.Name;
}
public sealed partial class ShellViewModel : Observable
{
    private readonly IDialogs dialogs;private readonly ConnectionProfile? suppliedProfile;private FinanceStore? store;private ExportService? exporter;
    private Snapshot snapshot=new([],[],[],[],new());private bool ready;private string notice="Conectando con MySQL…";private string sync="Resumen pendiente";private string page="Inicio";
    public string Page {get=>page;set{if(Set(ref page,value)){Changed(nameof(Subtitle));Changed(nameof(PageNewLabel));Changed(nameof(PageNewCommand));if(value=="Servicios"){if(Ready)PrepareServicesCommand.Execute(null);else RefreshSheetRows();}}}}
    public string PageNewLabel=>Page=="Servicios"?"+ Agregar servicio":"+ Nuevo consumo";
    public ICommand PageNewCommand=>Page=="Servicios"?AddServiceCommand:NewCommand;
    public string Subtitle=>Page switch{"Inicio"=>"Tus tarjetas, en un vistazo.","Servicios"=>"Tu planilla mensual de pagos.","Consumos"=>"Todo tu historial, siempre a mano.","Estadísticas"=>"Entendé en qué estás gastando.","Tarjetas y períodos"=>"Cada tarjeta tiene sus propias fechas.",_=>"Datos locales. Un resumen siempre disponible en Drive."};
    public string Today=>DateTime.Today.ToString("dddd d 'de' MMMM",Money.Ar);
    public bool Ready {get=>ready;set=>Set(ref ready,value);}public string Notice {get=>notice;set=>Set(ref notice,value);}public string Sync {get=>sync;set=>Set(ref sync,value);}
    public string Connection=>store==null?"Sin conexión configurada":$"MySQL local · {store.Profile.Host}:{store.Profile.Port} · {store.Profile.Database} · {store.Profile.User}";
    public string LocalFolder=>LocalConfig.Folder;public string ExportPath=>LocalConfig.ResolveExportPath(snapshot.Settings.GetValueOrDefault("export_path"))??"Sin carpeta configurada";
    public ObservableCollection<CardTile> Tiles {get;}=[];public ObservableCollection<Movement> Recent {get;}=[];public ObservableCollection<Movement> History {get;}=[];
    public ObservableCollection<StatRow> DashboardCategories {get;}=[];public ObservableCollection<StatRow> CategoryStats {get;}=[];public ObservableCollection<StatRow> CardStats {get;}=[];public ObservableCollection<StatRow> PeriodStats {get;}=[];
    public ObservableCollection<Card> Cards {get;}=[];public ObservableCollection<Category> Categories {get;}=[];public ObservableCollection<PeriodDisplay> Periods {get;}=[];
    private readonly ObservableCollection<Choice> cardChoices=[new(0,"Todas las tarjetas")],categoryChoices=[new(0,"Todas las categorías")],periodChoices=[new(0,"Todos los períodos"),new(-1,"Sin período asignado")];
    private readonly ObservableCollection<Choice> statisticsPeriods=[new(0,"Todos los períodos")];
    private int statsCard,statsPeriod;
    public ObservableCollection<Choice> StatisticsPeriodChoices=>statisticsPeriods;
    public Choice? StatisticsCardFilter {get=>cardChoices.FirstOrDefault(c=>c.Id==StatsCard);set{if(value!=null)StatsCard=value.Id;}}
    public Choice? StatisticsPeriodFilter {get=>statisticsPeriods.FirstOrDefault(c=>c.Id==StatsPeriod);set{if(value!=null)StatsPeriod=value.Id;}}
    public int StatsCard {get=>statsCard;set{if(Set(ref statsCard,value)){statsPeriod=0;Changed(nameof(StatsPeriod));Changed(nameof(StatisticsCardFilter));RebuildStatisticsPeriods();UpdateStatistics();}}}
    public int StatsPeriod {get=>statsPeriod;set{if(Set(ref statsPeriod,value)){Changed(nameof(StatisticsPeriodFilter));UpdateStatistics();}}}
    public string StatisticsArs {get;private set;}="$ 0,00";
    public string StatisticsUsd {get;private set;}="US$ 0,00";
    public bool HasHistoryFilters=>FilterCard!=0||FilterPeriod!=0||FilterCategory!=0||FilterCurrency is "ARS" or "USD"||!string.IsNullOrWhiteSpace(Search);
    public string HistoryEmptyMessage=>snapshot.Movements.Count==0?"Todavía no hay movimientos registrados.":"No hay resultados para estos filtros. Usá Limpiar filtros para ver todos los movimientos.";
    public ObservableCollection<Choice> CardChoices=>cardChoices;public ObservableCollection<Choice> CategoryChoices=>categoryChoices;public ObservableCollection<Choice> PeriodChoices=>periodChoices;
    public Choice? CardFilter {get=>cardChoices.FirstOrDefault(c=>c.Id==FilterCard);set{if(value!=null)FilterCard=value.Id;}}
    public Choice? CategoryFilter {get=>categoryChoices.FirstOrDefault(c=>c.Id==FilterCategory);set{if(value!=null)FilterCategory=value.Id;}}
    public Choice? PeriodFilter {get=>periodChoices.FirstOrDefault(c=>c.Id==FilterPeriod);set{if(value!=null)FilterPeriod=value.Id;}}
    private static void SyncChoices(ObservableCollection<Choice> choices,IEnumerable<Choice> source)
    {
        var desired=source.ToList();foreach(var obsolete in choices.Where(c=>!desired.Any(d=>d.Id==c.Id)).ToList())choices.Remove(obsolete);
        foreach(var d in desired){var existing=choices.FirstOrDefault(c=>c.Id==d.Id);if(existing==null)choices.Add(d);else existing.Name=d.Name;}
    }
    private void RebuildPeriodChoices(){SyncChoices(periodChoices,[new(0,"Todos los períodos"),new(-1,"Sin período asignado"),..snapshot.Periods.Where(p=>FilterCard==0||p.CardId==FilterCard).Select(p=>new Choice(p.Id,snapshot.Cards.Single(c=>c.Id==p.CardId).Name+" · "+p.Name))]);Changed(nameof(PeriodFilter));}
    private void RebuildStatisticsPeriods(){SyncChoices(statisticsPeriods,[new(0,"Todos los períodos"),..snapshot.Periods.Where(p=>StatsCard==0||p.CardId==StatsCard).Select(p=>new Choice(p.Id,snapshot.Cards.Single(c=>c.Id==p.CardId).Name+" · "+p.Name))]);Changed(nameof(StatisticsPeriodFilter));}
    private int filterCard,filterCategory,filterPeriod;private string filterCurrency="Todas",search="",sort="Fecha ↓",dashboardCurrency="ARS";
    public int FilterCard {get=>filterCard;set{if(Set(ref filterCard,value)){filterPeriod=0;Changed(nameof(FilterPeriod));Changed(nameof(CardFilter));RebuildPeriodChoices();ApplyFilters();}}}
    public int FilterCategory {get=>filterCategory;set{if(Set(ref filterCategory,value)){Changed(nameof(CategoryFilter));ApplyFilters();}}}public int FilterPeriod {get=>filterPeriod;set{if(Set(ref filterPeriod,value)){Changed(nameof(PeriodFilter));ApplyFilters();}}}
    public string FilterCurrency {get=>filterCurrency;set{if(Set(ref filterCurrency,value))ApplyFilters();}}public string Search {get=>search;set{if(Set(ref search,value))ApplyFilters();}}public string Sort {get=>sort;set{if(Set(ref sort,value))ApplyFilters();}}
    public string DashboardCurrency {get=>dashboardCurrency;set{if(Set(ref dashboardCurrency,value))UpdateDashboard();}}
    public string[] CurrencyChoices {get;}=["Todas","ARS","USD"];public string[] ChartCurrencyChoices {get;}=["ARS","USD"];public string[] SortChoices {get;}=["Fecha ↓","Fecha ↑","Importe ↓","Importe ↑"];
    private string totalArs="$ 0,00",totalUsd="US$ 0,00",filteredArs="$ 0,00",filteredUsd="US$ 0,00",pending="",filterSummary="",statsCurrency="ARS";
    public string TotalArs {get=>totalArs;set=>Set(ref totalArs,value);}public string TotalUsd {get=>totalUsd;set=>Set(ref totalUsd,value);}public string FilteredArs {get=>filteredArs;set=>Set(ref filteredArs,value);}public string FilteredUsd {get=>filteredUsd;set=>Set(ref filteredUsd,value);}public string Pending {get=>pending;set=>Set(ref pending,value);}public string FilterSummary {get=>filterSummary;set=>Set(ref filterSummary,value);}
    public string StatsCurrency {get=>statsCurrency;set{if(Set(ref statsCurrency,value is "USD"?"USD":"ARS"))UpdateStatistics();}}
    public Movement? SelectedMovement {get;set;}public Card? SelectedCard {get;set;}public Category? SelectedCategory {get;set;}public PeriodDisplay? SelectedPeriod {get;set;}
    public ICommand NavigateCommand {get;}public ICommand NewCommand {get;}public ICommand EditCommand {get;}public ICommand DeleteCommand {get;}public ICommand RetryCommand {get;}public ICommand ExportCommand {get;}public ICommand ImportCommand {get;}public ICommand NewCardCommand {get;}public ICommand EditCardCommand {get;}public ICommand NewCategoryCommand {get;}public ICommand EditCategoryCommand {get;}public ICommand NewPeriodCommand {get;}public ICommand EditPeriodCommand {get;}public ICommand NextPeriodCommand {get;}public ICommand ExportFolderCommand {get;}public ICommand LogsCommand {get;}public ICommand ClearFiltersCommand {get;}
    public ICommand ReconfigureCommand {get;}public ICommand TestConnectionCommand {get;}
    public ICommand ClearStatisticsFiltersCommand {get;}
    public ICommand BackupExportCommand {get;}public ICommand BackupImportCommand {get;}
    private AsyncCommand Command(Func<Task> action)=>new(action,e=>{Notice=LocalConfig.Friendly(e);if(Page=="Servicios"){SheetFeedback=Notice;Changed(nameof(SheetFeedback));}});
    public ShellViewModel(IDialogs dialogs,ConnectionProfile? profile=null)
    {
        this.dialogs=dialogs;suppliedProfile=profile;InitializeServiceCommands();NavigateCommand=new RelayCommand(p=>Page=p?.ToString()??"Inicio");NewCommand=Command(()=>EditMovement(null));EditCommand=Command(()=>SelectedMovement==null?Task.CompletedTask:EditMovement(SelectedMovement));DeleteCommand=Command(Delete);
        RetryCommand=Command(InitializeAsync);ExportCommand=Command(()=>Export(true));ImportCommand=Command(Import);
        BackupExportCommand=Command(()=>TransferBackup(false));BackupImportCommand=Command(()=>TransferBackup(true));
        ReconfigureCommand=Command(async()=>{ConnectionProfile? current=store?.Profile;try{current??=LocalConfig.Load();}catch(Exception e){LocalConfig.Log(e);}var editor=new ConnectionEditor(current);if(dialogs.Show(editor))await InitializeAsync();});
        TestConnectionCommand=Command(async()=>{var p=store?.Profile??LocalConfig.Load();if(p==null){ReconfigureCommand.Execute(null);return;}var result=await DatabaseSetup.ProbeAsync(p);Notice=result.Message;});
        NewCardCommand=Command(()=>EditName(false,true));EditCardCommand=Command(()=>EditName(false,false));NewCategoryCommand=Command(()=>EditName(true,true));EditCategoryCommand=Command(()=>EditName(true,false));
        NewPeriodCommand=Command(()=>EditPeriod(null,null));EditPeriodCommand=Command(()=>SelectedPeriod==null?Task.CompletedTask:EditPeriod(SelectedPeriod.Model,null));NextPeriodCommand=Command(()=>EditPeriod(null,SelectedCard?.Id??SelectedPeriod?.Model.CardId));
        ExportFolderCommand=Command(async()=>{if(dialogs.Show(new ExportEditor(dialogs,LocalConfig.ResolveExportPath(snapshot.Settings.GetValueOrDefault("export_path"))))){Changed(nameof(ExportPath));await Export(true);}});
        LogsCommand=new RelayCommand(_=>{System.IO.Directory.CreateDirectory(LocalConfig.Folder);dialogs.OpenFolder(LocalConfig.Folder);});
        ClearFiltersCommand=new RelayCommand(_=>{FilterCard=0;FilterPeriod=0;FilterCategory=0;FilterCurrency="Todas";Search="";});
        ClearStatisticsFiltersCommand=new RelayCommand(_=>{StatsCard=0;StatsPeriod=0;StatsCurrency="ARS";});
    }
    private async Task TransferBackup(bool importing)
    {
        if(!Ready||store==null)throw new RuleException("Conectá MySQL antes de transferir datos.");
        var path=importing?dialogs.PickBackup():dialogs.SaveBackup();if(path==null)return;
        if(!importing)
        {
            var editor=new BackupExportEditor(store,path);if(dialogs.Show(editor))Notice="Respaldo cifrado y verificado: "+path;
            return;
        }
        var restore=new BackupRestoreEditor(store,path);
        if(!dialogs.Show(restore)||restore.Result==null)return;
        ClearFiltersCommand.Execute(null);ClearStatisticsFiltersCommand.Execute(null);Tiles.Clear();SelectedMovement=null;SelectedCard=null;SelectedCategory=null;SelectedPeriod=null;
        await RefreshAsync();Sync=restore.Result.ExportStatus;
        Notice="Restauración completa verificada. Copia previa: "+restore.Result.SafetyBackupPath;
    }
    public async Task InitializeAsync()
    {
        Notice="Conectando con MySQL…";
        try
        {
            var profile=suppliedProfile??LocalConfig.Load();
            if(profile==null){var setup=new ConnectionEditor();if(!dialogs.Show(setup)){Notice="Configurá la conexión con Reintentar / conectar para comenzar.";return;}profile=setup.Result;}
            store=new(profile!);await store.InitializeAsync();exporter=new(store);Ready=true;Changed(nameof(Connection));await RefreshAsync();await Export(false);
        }
        catch(Exception e){LocalConfig.Log(e);Notice=LocalConfig.Friendly(e);Ready=false;}
    }
    public async Task RefreshAsync()
    {
        if(store==null)return;snapshot=await store.ReadAsync();LocalConfig.ResolveExportPath(snapshot.Settings.GetValueOrDefault("export_path"),true);
        var selections=Tiles.ToDictionary(t=>t.Id,t=>t.Selected);Tiles.Clear();
        foreach(var card in snapshot.Cards.Where(c=>c.Active))
        {
            var p=snapshot.Current(card.Id);var rows=snapshot.Movements.Where(m=>m.CardId==card.Id&&p!=null&&m.PeriodId==p.Id).ToList();
            var tile=new CardTile(UpdateDashboard){Id=card.Id,Name=card.Name,Period=p?.Name??"Configurá el primer período",Dates=p==null?"Fechas pendientes":$"{p.Start:dd/MM} — {p.Closing:dd/MM/yyyy}",Due=p==null?"Sin vencimiento":$"Vence {p.Due:dd/MM/yyyy}",Ars=Money.Format(rows.Where(m=>m.Currency=="ARS").Sum(m=>m.Signed)),Usd=Money.Format(rows.Where(m=>m.Currency=="USD").Sum(m=>m.Signed),"USD"),Count=$"{rows.Count} movimientos"};
            Tiles.Add(tile);if(selections.TryGetValue(card.Id,out var sel))tile.Selected=sel;
        }
        Replace(Cards,snapshot.Cards);Replace(Categories,snapshot.Categories);Replace(Periods,snapshot.Periods.Select(p=>new PeriodDisplay(p,snapshot.Cards.Single(c=>c.Id==p.CardId).Name,$"{p.Start:dd/MM/yyyy} — {p.Closing:dd/MM/yyyy}",p.Due.ToString("dd/MM/yyyy"))));
        SyncChoices(cardChoices,[new(0,"Todas las tarjetas"),..snapshot.Cards.Select(c=>new Choice(c.Id,c.Name))]);SyncChoices(categoryChoices,[new(0,"Todas las categorías"),..snapshot.Categories.Select(c=>new Choice(c.Id,c.Name))]);
        Changed(nameof(CardFilter));Changed(nameof(CategoryFilter));RebuildPeriodChoices();Changed(nameof(StatisticsCardFilter));RebuildStatisticsPeriods();Changed(nameof(ExportPath));Changed(nameof(Connection));
        UpdateDashboard();ApplyFilters();UpdateStatistics();RefreshServices();
        var unassigned=snapshot.Movements.Count(m=>m.PeriodId==null);
        var manualOutside=snapshot.Movements.Count(m=>m.ManualPeriod&&snapshot.Periods.Any(p=>p.Id==m.PeriodId&&(m.Date<p.Start||m.Date>p.Closing)));
        var needs=snapshot.Cards.Count(c=>c.Active&&(snapshot.Current(c.Id)==null||snapshot.Current(c.Id)!.Closing<DateTime.Today));
        Pending=string.Join(" · ",new[]{unassigned>0?$"{unassigned} movimientos sin período (visibles en Consumos)":"",manualOutside>0?$"{manualOutside} imputaciones manuales fuera de fecha: revisar":"",needs>0?$"{needs} tarjetas requieren configurar un período":""}.Where(x=>x.Length>0));
        Notice=Pending.Length>0?Pending:"Datos actualizados · MySQL conectado";
    }
    private void UpdateDashboard()
    {
        var ids=Tiles.Where(t=>t.Selected).Select(t=>t.Id).ToHashSet();var rows=snapshot.Movements.Where(m=>ids.Contains(m.CardId)&&snapshot.Current(m.CardId)?.Id==m.PeriodId&&m.PeriodId!=null).ToList();
        TotalArs=Money.Format(rows.Where(m=>m.Currency=="ARS").Sum(m=>m.Signed));TotalUsd=Money.Format(rows.Where(m=>m.Currency=="USD").Sum(m=>m.Signed),"USD");
        Replace(Recent,snapshot.Movements.Where(m=>ids.Contains(m.CardId)).Take(12));Replace(DashboardCategories,Bars(rows.Where(m=>m.Currency==DashboardCurrency),m=>m.CategoryName,DashboardCurrency).Take(5));
    }
    private List<Movement> Filter()
    {
        IEnumerable<Movement> q=snapshot.Movements;
        if(FilterCard>0)q=q.Where(m=>m.CardId==FilterCard);if(FilterPeriod>0)q=q.Where(m=>m.PeriodId==FilterPeriod);if(FilterPeriod==-1)q=q.Where(m=>m.PeriodId==null);if(FilterCategory>0)q=q.Where(m=>m.CategoryId==FilterCategory);if(FilterCurrency is "ARS" or "USD")q=q.Where(m=>m.Currency==FilterCurrency);
        var term=Search.Trim();if(term.Length>0)q=q.Where(m=>m.Description.Contains(term,StringComparison.OrdinalIgnoreCase)||m.Notes.Contains(term,StringComparison.OrdinalIgnoreCase));
        return Sort switch{"Fecha ↑"=>q.OrderBy(m=>m.Date).ThenBy(m=>m.Id).ToList(),"Importe ↓"=>q.OrderByDescending(m=>m.Signed).ToList(),"Importe ↑"=>q.OrderBy(m=>m.Signed).ToList(),_=>q.OrderByDescending(m=>m.Date).ThenByDescending(m=>m.Id).ToList()};
    }
    private void ApplyFilters()
    {
        var rows=Filter();Replace(History,rows);FilteredArs=Money.Format(rows.Where(m=>m.Currency=="ARS").Sum(m=>m.Signed));FilteredUsd=Money.Format(rows.Where(m=>m.Currency=="USD").Sum(m=>m.Signed),"USD");
        FilterSummary=HasHistoryFilters?$"{rows.Count} de {snapshot.Movements.Count} movimientos · filtros activos":$"{rows.Count} movimientos · total neto del filtro";
        Changed(nameof(HasHistoryFilters));Changed(nameof(HistoryEmptyMessage));
    }
    private void UpdateStatistics()
    {
        var rows=snapshot.Movements.Where(m=>(StatsCard==0||m.CardId==StatsCard)&&(StatsPeriod==0||m.PeriodId==StatsPeriod)).ToList();
        StatisticsArs=Money.Format(rows.Where(m=>m.Currency=="ARS").Sum(m=>m.Signed));StatisticsUsd=Money.Format(rows.Where(m=>m.Currency=="USD").Sum(m=>m.Signed),"USD");
        Changed(nameof(StatisticsArs));Changed(nameof(StatisticsUsd));
        var currency=StatsCurrency;var stats=rows.Where(m=>m.Currency==currency).ToList();
        Replace(CategoryStats,Bars(stats,m=>m.CategoryName,currency));Replace(CardStats,Bars(stats,m=>m.CardName,currency));
        var periodRows=stats.Where(m=>m.PeriodId!=null).GroupBy(m=>m.PeriodId).OrderBy(g=>snapshot.Periods.Single(p=>p.Id==g.Key).Start).ToList();
        var max=periodRows.Select(g=>Math.Abs(g.Sum(m=>m.Signed))).DefaultIfEmpty(0).Max();
        Replace(PeriodStats,periodRows.Select(g=>new StatRow(g.First().CardName+" · "+g.First().PeriodName,Money.Format(g.Sum(m=>m.Signed),currency),max==0?0:(double)(Math.Abs(g.Sum(m=>m.Signed))/max)*240,"")));
    }
    private static IEnumerable<StatRow> Bars(IEnumerable<Movement> rows,Func<Movement,string> key,string currency)
    {
        var groups=rows.GroupBy(key).Select(g=>new{ Name=g.Key,Value=g.Sum(m=>m.Signed)}).OrderByDescending(g=>g.Value).ToList();var positive=groups.Sum(g=>Math.Max(g.Value,0));var max=groups.Select(g=>Math.Abs(g.Value)).DefaultIfEmpty(0).Max();
        return groups.Select(g=>new StatRow(g.Name,Money.Format(g.Value,currency),max==0?0:(double)(Math.Abs(g.Value)/max)*240,g.Value<0?"Reintegro neto":positive==0?"0 %":(g.Value/positive).ToString("P1",Money.Ar)));
    }
    private static void Replace<T>(ObservableCollection<T> c,IEnumerable<T> rows){c.Clear();foreach(var r in rows)c.Add(r);}
    private async Task EditMovement(Movement? m)
    {
        if(!Ready){Notice="Conectá MySQL para registrar movimientos.";return;}
        var vm=new MovementEditor(store!,snapshot,async()=>{await RefreshAsync();Notice="Movimiento guardado";_ = Export(false);},m);dialogs.Show(vm);await Task.CompletedTask;
    }
    private async Task Delete()
    {
        if(!Ready||SelectedMovement==null)return;var m=SelectedMovement;
        if(!dialogs.Confirm($"¿Eliminar {m.TypeLabel.ToLower()} de {m.DisplayAmount} del {m.Date:dd/MM/yyyy}?\n{m.Description}\nSe conservará el registro de auditoría."))return;
        await store!.DeleteMovementAsync(m);await RefreshAsync();Notice="Movimiento eliminado";await Export(false);
    }
    private async Task EditName(bool category,bool create)
    {
        if(!Ready)return;if(!create&&(category?SelectedCategory==null:SelectedCard==null))return;
        if(dialogs.Show(new NameEditor(store!,category,create?null:SelectedCard,create?null:SelectedCategory))){await RefreshAsync();await Export(false);}
    }
    private async Task EditPeriod(Period? p,int? card)
    {
        if(!Ready)return;if(dialogs.Show(new PeriodEditor(store!,snapshot,p,card))){await RefreshAsync();Notice=Pending.Length>0?Pending:"Período guardado. Asignaciones automáticas verificadas.";await Export(false);}
    }
    private async Task Import()
    {
        if(!Ready)return;var path=dialogs.PickText();if(path==null)return;
        if(new System.IO.FileInfo(path).Length>10_000_000)throw new RuleException("El archivo supera 10 MB. Dividilo en archivos más pequeños.");
        var lines=await System.IO.File.ReadAllLinesAsync(path);if(dialogs.Show(new ImportEditor(store!,snapshot,lines,dialogs))){await RefreshAsync();await Export(false);}
    }
    public async Task Export(bool force=false){if(exporter==null)return;await exporter.TryAsync(force);Sync=exporter.Status;}
}
