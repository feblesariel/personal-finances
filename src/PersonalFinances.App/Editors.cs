using System.Collections.ObjectModel;
using System.Windows.Input;
using PersonalFinances.Core;

namespace PersonalFinances.App;
public sealed class MovementEditor : EditorViewModel
{
    private readonly FinanceStore store;private readonly Snapshot snapshot;private readonly Func<Task> changed;private readonly bool editing;private Movement model;
    public List<Card> Cards {get;} public List<Category> Categories {get;} public string[] Currencies {get;}=["ARS","USD"];
    private Card? card;private Category? category;private string amount="";private string currency="ARS";private DateTime? date=DateTime.Today;private Period? period;private bool manual;private bool refund;private Movement? original;
    public Card? Card {get=>card;set{if(Set(ref card,value)){Period=null;Original=null;Changed(nameof(Periods));Changed(nameof(Originals));Changed(nameof(Assignment));}}}
    public Category? Category {get=>category;set=>Set(ref category,value);} public string Amount {get=>amount;set=>Set(ref amount,value);}
    public string Currency {get=>currency;set{if(Set(ref currency,value)){Original=null;Changed(nameof(Originals));}}}
    public DateTime? Date {get=>date;set{if(Set(ref date,value))Changed(nameof(Assignment));}}
    public Period? Period {get=>period;set=>Set(ref period,value);}public bool Manual {get=>manual;set{Set(ref manual,value);Changed(nameof(Assignment));}}
    public bool Refund {get=>refund;set{Set(ref refund,value);if(!value)Original=null;}}
    public Movement? Original {get=>original;set=>Set(ref original,value);}
    public ICommand ClearOriginalCommand {get;}
    public string Description {get;set;}="";public string Notes {get;set;}="";public bool KeepOpen {get;set;}=true;public bool IsNew=>!editing;
    public IEnumerable<Period> Periods => snapshot.Periods.Where(p=>p.CardId==Card?.Id).OrderByDescending(p=>p.Start);
    public IEnumerable<Movement> Originals=>snapshot.Movements.Where(m=>!m.Refund&&m.CardId==Card?.Id&&m.Currency==Currency&&m.Id!=model.Id);
    public string Assignment => Manual?"Imputación manual: elegí un período de esta tarjeta.":Date!=null&&Card!=null?(Periods.FirstOrDefault(p=>p.Start<=Date.Value.Date&&p.Closing>=Date.Value.Date) is {} p?"Período automático: "+p.Name:"Fuera de los períodos configurados. Se guardará visible como pendiente."):"Seleccioná fecha y tarjeta.";
    public MovementEditor(FinanceStore store,Snapshot snapshot,Func<Task> changed,Movement? source=null)
    {
        this.store=store;this.snapshot=snapshot;this.changed=changed;editing=source!=null;model=source?.Copy()??new();ClearOriginalCommand=new RelayCommand(_=>Original=null);
        Title=editing?"Editar movimiento":"Nuevo consumo";Subtitle="Cargá el importe y guardá con Enter. Escape para cerrar.";
        Cards=snapshot.Cards.Where(c=>c.Active||c.Id==source?.CardId).ToList();Categories=snapshot.Categories.Where(c=>c.Active||c.Id==source?.CategoryId).ToList();
        Card=Cards.FirstOrDefault(c=>c.Id==(source?.CardId??int.Parse(snapshot.Settings.GetValueOrDefault("last_card","0"))))??Cards.FirstOrDefault();
        Category=Categories.FirstOrDefault(c=>c.Id==(source?.CategoryId??int.Parse(snapshot.Settings.GetValueOrDefault("last_category","0"))))??Categories.FirstOrDefault();
        Currency=source?.Currency??snapshot.Settings.GetValueOrDefault("last_currency","ARS"); Date=source?.Date??DateTime.Today;
        if(source!=null){Amount=source.Amount.ToString("N2",Money.Ar);Description=source.Description;Notes=source.Notes;Refund=source.Refund;Manual=source.ManualPeriod;Period=Periods.FirstOrDefault(p=>p.Id==source.PeriodId);Original=Originals.FirstOrDefault(m=>m.Id==source.OriginalId);}
    }
    protected override async Task Save()
    {
        if(Card==null||Category==null||Date==null)throw new RuleException("Completá fecha, tarjeta y categoría.");
        model.CardId=Card.Id;model.CategoryId=Category.Id;model.Date=Date.Value.Date;model.Amount=Money.Parse(Amount);model.Currency=Currency;
        model.Description=Description.Trim();model.Notes=Notes.Trim();model.Refund=Refund;model.OriginalId=Original?.Id;model.ManualPeriod=Manual;model.PeriodId=Period?.Id;
        await store.SaveMovementAsync(model);
        // Saved first: a failure while remembering preferences must never make a purchase look unsaved.
        try { await store.SettingAsync("last_card",Card.Id.ToString());await store.SettingAsync("last_category",Category.Id.ToString());await store.SettingAsync("last_currency",Currency); } catch(Exception e){LocalConfig.Log(e);}
        try { await changed(); } catch(Exception e) { LocalConfig.Log(e);Error="Movimiento guardado. Usá Actualizar para recargar los totales."; }
        if(editing||!KeepOpen){Close();return;}
        model=new();Amount="";Description="";Notes="";Refund=false;Original=null;
        Changed(nameof(Description));Changed(nameof(Notes));Error="Guardado. Podés cargar otro consumo.";
    }
}
public sealed class PeriodEditor : EditorViewModel
{
    private readonly FinanceStore store;private readonly Snapshot snapshot;private readonly int id;public List<Card> Cards {get;}
    private Card? card;public Card? Card {get=>card;set{if(Set(ref card,value)&&id==0)Propose();}}public bool CanChangeCard=>id==0;
    public string Name {get;set;}="";public DateTime? Start {get;set;} public DateTime? Closing {get;set;}public DateTime? Due {get;set;}
    public PeriodEditor(FinanceStore store,Snapshot snapshot,Period? period=null,int? cardId=null)
    {
        this.store=store;this.snapshot=snapshot;id=period?.Id??0;Cards=snapshot.Cards.Where(c=>c.Active||c.Id==period?.CardId).ToList();
        Title=period==null?"Configurar período":"Editar período";Subtitle="Las fechas son inclusivas. Confirmá cierre y vencimiento del resumen real.";
        Card=Cards.FirstOrDefault(c=>c.Id==(period?.CardId??cardId))??Cards.FirstOrDefault();
        if(period!=null){Name=period.Name;Start=period.Start;Closing=period.Closing;Due=period.Due;}
    }
    private void Propose()
    {
        var last=snapshot.Periods.Where(p=>p.CardId==Card?.Id).OrderByDescending(p=>p.Closing).FirstOrDefault();
        Start=last?.Closing.AddDays(1);Closing=last?.Closing.AddMonths(1);Due=last?.Due.AddMonths(1);Name=Closing?.ToString("MMMM yyyy",Money.Ar)??"";
        Changed(nameof(Start));Changed(nameof(Closing));Changed(nameof(Due));Changed(nameof(Name));
    }
    protected override async Task Save()
    {
        if(Card==null||Start==null||Closing==null||Due==null)throw new RuleException("Completá nombre, inicio, cierre y vencimiento del período.");
        await store.SavePeriodAsync(new(){Id=id,CardId=Card.Id,Name=Name,Start=Start.Value,Closing=Closing.Value,Due=Due.Value});Close();
    }
}
public sealed class NameEditor : EditorViewModel
{
    private readonly FinanceStore store;private readonly bool category;private readonly int id;
    public string Name {get;set;}="";public bool Active {get;set;}=true;
    public NameEditor(FinanceStore store,bool category,Card? card=null,Category? cat=null)
    {
        this.store=store;this.category=category;id=card?.Id??cat?.Id??0;Name=card?.Name??cat?.Name??"";Active=card?.Active??cat?.Active??true;
        Title=(id==0?"Agregar ":"Editar ")+(category?"categoría":"tarjeta");Subtitle="Desactivar conserva todos los períodos y movimientos históricos.";
    }
    protected override async Task Save(){if(category)await store.SaveCategoryAsync(new(){Id=id,Name=Name,Active=Active});else await store.SaveCardAsync(new(){Id=id,Name=Name,Active=Active});Close();}
}
public sealed class ImportEditor : EditorViewModel
{
    private readonly FinanceStore store;private readonly IDialogs dialogs;private readonly Snapshot snapshot;
    public ObservableCollection<ImportRow> Rows {get;} public ImportRow? Selected {get;set;}
    public ICommand ReviewCommand {get;} public ICommand ExcludeCommand {get;} public string Summary=> $"{Rows.Count} líneas · marcá las que querés importar. Revisá fechas, tarjeta, moneda, categoría y posibles duplicados.";
    public ImportEditor(FinanceStore store,Snapshot snapshot,IEnumerable<string> lines,IDialogs dialogs)
    {
        this.store=store;this.snapshot=snapshot;this.dialogs=dialogs;Title="Importar movimientos desde TXT";Subtitle="Vista previa. Nada se guardará hasta que presiones Guardar.";
        Rows=new(TextImport.Parse(lines,snapshot));ReviewCommand=new RelayCommand(_=>Review());ExcludeCommand=new RelayCommand(_=>{foreach(var row in Rows)row.Include=false;Changed(nameof(Rows));});
    }
    private void Review()
    {
        if(Selected==null)return;var row=Selected;var editor=new ImportRowEditor(row,snapshot);
        if(dialogs.Show(editor)){var index=Rows.IndexOf(row);Rows.RemoveAt(index);row.Reviewed=true;row.Problem="Revisado manualmente";Rows.Insert(index,row);}
    }
    protected override async Task Save()
    {
        var selected=Rows.Where(r=>r.Include).ToList();if(selected.Count==0)throw new RuleException("Seleccioná al menos una línea.");
        if(selected.Any(r=>r.Movement==null||r.Duplicate&&!r.Reviewed))throw new RuleException("Revisá las líneas ambiguas o duplicadas con el botón Revisar línea, o excluilas.");
        if(!dialogs.Confirm($"¿Importar {selected.Count} movimientos? Confirmá que revisaste fechas, categorías e importes de la vista previa."))return;
        await store.ImportAsync(selected.Select(r=>r.Movement!));Close();
    }
}
public sealed class ImportRowEditor : EditorViewModel
{
    private readonly ImportRow row;public List<Card> Cards {get;}public List<Category> Categories {get;}
    public Card? Card {get;set;}public Category? Category {get;set;}public string Amount {get;set;}="";public DateTime? Date {get;set;} public string Description {get;set;}="";public string Currency {get;set;}="ARS";public string[] Currencies {get;}=["ARS","USD"];public bool Refund {get;set;}
    public string Raw=>row.Raw;
    public ImportRowEditor(ImportRow row,Snapshot s)
    {
        this.row=row;Title="Revisar línea "+row.Line;Subtitle="Corregí los datos o confirmá expresamente un posible duplicado.";Cards=s.Cards.Where(c=>c.Active).ToList();Categories=s.Categories.Where(c=>c.Active).ToList();var m=row.Movement;
        Card=Cards.FirstOrDefault(c=>c.Id==m?.CardId);Category=Categories.FirstOrDefault(c=>c.Id==m?.CategoryId)??Categories.FirstOrDefault();Amount=m?.Amount.ToString("N2",Money.Ar)??"";Date=m?.Date;Description=m?.Description??"";Currency=m?.Currency??"ARS";Refund=m?.Refund??false;
    }
    protected override Task Save()
    {
        if(Card==null||Category==null||Date==null)throw new RuleException("Completá fecha, tarjeta y categoría.");
        var m=row.Movement??new();m.Date=Date.Value.Date;m.CardId=Card.Id;m.CardName=Card.Name;m.CategoryId=Category.Id;m.CategoryName=Category.Name;m.Amount=Money.Parse(Amount);m.Currency=Currency;m.Description=Description.Trim();m.Refund=Refund;row.Movement=m;row.Include=true;Close();return Task.CompletedTask;
    }
}
