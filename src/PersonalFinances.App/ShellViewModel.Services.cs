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
    public ServiceMonthChoice? SheetMonth {get=>sheetMonth;set{if(value!=null&&Set(ref sheetMonth,value))RefreshSheetRows();}}
    public ServiceSheetRow? SelectedSheetRow {get;set;}
    public string SheetFeedback {get;private set;}="Doble clic o Enter sobre una fila para abrir el servicio";
    public string SheetSummary=>$"{SheetRows.Count(r=>r.BaseStatus=="Pagado")} pagados   ·   {SheetRows.Count(r=>r.BaseStatus=="Parcial")} parciales   ·   {SheetRows.Count(r=>r.BaseStatus is "Pendiente" or "Sin cargar")} pendientes";
    public ICommand PrepareServicesCommand {get;private set;}=null!;
    public ICommand AddServiceCommand {get;private set;}=null!;
    public ICommand NewServiceMonthCommand {get;private set;}=null!;
    public ICommand OpenServiceCommand {get;private set;}=null!;
    private bool serviceDialogOpen;
    private void InitializeServiceCommands()
    {
        PrepareServicesCommand=Command(async()=>{if(Ready){if(SheetMonth?.Month==ServiceSheet.Month(DateTime.Today))await store!.PrepareServiceMonthAsync(DateTime.Today);await RefreshAsync();_ = Export(false);}});
        AddServiceCommand=Command(async()=>{if(Ready&&!serviceDialogOpen&&dialogs.Show(new ServiceEditor(store!,snapshot))){await store!.CreateServiceMonthAsync(SheetMonth!.Month,false);await RefreshAsync();_ = Export(false);}});
        NewServiceMonthCommand=Command(async()=>{if(!Ready||serviceDialogOpen)return;var editor=new ServiceMonthEditor(store!,SheetMonth!.Month.AddMonths(1));if(dialogs.Show(editor)){sheetMonth=new(editor.Month);await RefreshAsync();_ = Export(false);}});
        OpenServiceCommand=Command(async()=>
        {
            if(!Ready||serviceDialogOpen||SelectedSheetRow is not {} row)return;
            serviceDialogOpen=true;
            try
            {
                var editor=new ServiceEntryEditor(store!,row.Entry,dialogs);
                if(dialogs.Show(editor)){_ = Export(false);try{await RefreshAsync();SheetFeedback=editor.Deleted?"Servicio eliminado; copia de recuperación conservada.":"Guardado";}catch(Exception e){LocalConfig.Log(e);SheetFeedback="Los cambios se guardaron. No se pudo refrescar la tabla; usá F5 para actualizar.";}Changed(nameof(SheetFeedback));}
            }
            finally{serviceDialogOpen=false;}
        });
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
}