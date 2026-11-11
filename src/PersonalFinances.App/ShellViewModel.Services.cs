using System.Collections.ObjectModel;
using System.Windows.Input;
using PersonalFinances.Core;
namespace PersonalFinances.App;
public sealed record ServiceMonthChoice(DateTime Month){public string Name=>Month.ToString("MMMM yyyy",Money.Ar);}
public sealed partial class ShellViewModel
{
    public ObservableCollection<ServiceSheetRow> SheetRows {get;}=[];
    public ObservableCollection<ServiceMonthChoice> SheetMonths {get;}=[];
    private ServiceMonthChoice? sheetMonth=new(ServiceSheet.Month(DateTime.Today));
    public ServiceMonthChoice? SheetMonth {get=>sheetMonth;set{if(value==null||value.Month==sheetMonth?.Month)return;if(SheetRows.Any(r=>r.Dirty)){_ = SelectSheetMonthAsync(value);return;}Set(ref sheetMonth,value);RefreshSheetRows();}}
    public ServiceSheetRow? SelectedSheetRow {get;set;}
    public string SheetFeedback {get;private set;}="Doble clic para editar · Tab siguiente campo · Enter guardar · Esc cancelar";
    public string SheetSummary=>$"{SheetRows.Count(r=>r.BaseStatus=="Pagado")} pagados   ·   {SheetRows.Count(r=>r.BaseStatus=="Parcial")} parciales   ·   {SheetRows.Count(r=>r.BaseStatus is "Pendiente" or "Sin cargar")} pendientes";
    public ICommand PrepareServicesCommand {get;private set;}=null!;
    public ICommand AddServiceCommand {get;private set;}=null!;
    public ICommand NewServiceMonthCommand {get;private set;}=null!;
    public ICommand EditSheetServiceCommand {get;private set;}=null!;
    public ICommand SheetPaymentsCommand {get;private set;}=null!;
    public ICommand SheetPeriodDetailsCommand {get;private set;}=null!;
    private void InitializeServiceCommands()
    {
        PrepareServicesCommand=Command(async()=>{if(Ready&&await SavePendingSheetAsync()){if(SheetMonth?.Month==ServiceSheet.Month(DateTime.Today))await store!.PrepareServiceMonthAsync(DateTime.Today);await RefreshAsync();await Export(false);}});
        AddServiceCommand=Command(async()=>{if(Ready&&await SavePendingSheetAsync()&&dialogs.Show(new ServiceEditor(store!,snapshot))){await store!.CreateServiceMonthAsync(SheetMonth!.Month,false);await RefreshAsync();await Export(false);}});
        NewServiceMonthCommand=Command(async()=>{if(!Ready||!await SavePendingSheetAsync())return;var editor=new ServiceMonthEditor(store!,SheetMonth!.Month.AddMonths(1));if(dialogs.Show(editor)){sheetMonth=new(editor.Month);await RefreshAsync();await Export(false);}});
        EditSheetServiceCommand=Command(async()=>{if(Ready&&SelectedSheetRow is {} row&&dialogs.Show(new ServiceEditor(store!,snapshot,row.Entry.Service))){await RefreshAsync();await Export(false);}});
        SheetPaymentsCommand=Command(async()=>{if(Ready&&SelectedSheetRow is {} row){dialogs.Show(new ServicePaymentsEditor(store!,row.Entry,async()=>{await RefreshAsync();await Export(false);},dialogs));await RefreshAsync();}});
        SheetPeriodDetailsCommand=Command(async()=>{if(Ready&&SelectedSheetRow?.Entry.Obligation is {} o&&dialogs.Show(new ObligationEditor(store!,SelectedSheetRow.Entry.Service,o))){await RefreshAsync();await Export(false);}});
    }
    private void RefreshServices()
    {
        var month=SheetMonth?.Month??ServiceSheet.Month(DateTime.Today);
        Replace(SheetMonths,snapshot.Services.Months.Append(ServiceSheet.Month(DateTime.Today)).Append(month).Distinct().OrderByDescending(d=>d).Select(d=>new ServiceMonthChoice(d)));
        sheetMonth=SheetMonths.Single(m=>m.Month==month);Changed(nameof(SheetMonth));RefreshSheetRows();
    }
    private void RefreshSheetRows()
    {
        var selected=SelectedSheetRow?.ServiceId;
        Replace(SheetRows,ServiceSheet.Rows(snapshot.Services,SheetMonth?.Month??DateTime.Today,DateTime.Today).Select(r=>new ServiceSheetRow(r)));
        SelectedSheetRow=SheetRows.FirstOrDefault(r=>r.ServiceId==selected);Changed(nameof(SelectedSheetRow));Changed(nameof(SheetSummary));
    }
    private readonly SemaphoreSlim sheetSaveGate=new(1,1);
    private async Task SelectSheetMonthAsync(ServiceMonthChoice month){if(await SavePendingSheetAsync()){sheetMonth=month;RefreshSheetRows();}Changed(nameof(SheetMonth));}
    private async Task<bool> SavePendingSheetAsync(){foreach(var row in SheetRows.Where(r=>r.Dirty).ToList())if(!await SaveSheetRowAsync(row))return false;return true;}
    public async Task<bool> SaveSheetRowAsync(ServiceSheetRow row)
    {
        await sheetSaveGate.WaitAsync();
        try
        {
            if(!row.Dirty)return true;
            await store!.SaveServiceSheetAsync(row.BuildEdit());snapshot=await store.ReadAsync();
            row.Apply(ServiceSheet.Rows(snapshot.Services,row.Entry.Month,DateTime.Today).Single(r=>r.ServiceId==row.ServiceId));
            SheetFeedback="Guardado";Changed(nameof(SheetFeedback));Changed(nameof(SheetSummary));_ = Export(false);return true;
        }
        catch(Exception e){LocalConfig.Log(e);SheetFeedback=LocalConfig.Friendly(e);Changed(nameof(SheetFeedback));return false;}
        finally{sheetSaveGate.Release();}
    }
}