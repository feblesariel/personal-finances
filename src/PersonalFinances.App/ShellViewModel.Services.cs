using System.Collections.ObjectModel;
using System.Windows.Input;
using PersonalFinances.Core;

namespace PersonalFinances.App;
public sealed partial class ShellViewModel
{
    public ObservableCollection<RecurringService> Services {get;}=[];
    public ObservableCollection<ServiceRow> ServiceRows {get;}=[];
    public ObservableCollection<ServicePayment> ServicePayments {get;}=[];
    public RecurringService? SelectedService {get;set;}
    private ServiceRow? selectedObligation;
    public ServiceRow? SelectedObligation {get=>selectedObligation;set{Set(ref selectedObligation,value);SelectedServicePayment=null;Changed(nameof(SelectedServicePayment));Replace(ServicePayments,value?.Payments.OrderBy(p=>p.Date)??Enumerable.Empty<ServicePayment>());Changed(nameof(ServiceDetail));if(value!=null){SelectedService=Services.FirstOrDefault(s=>s.Id==value.Service.Id);Changed(nameof(SelectedService));}}}
    public string ServiceDetail=>SelectedObligation?.Detail??"Seleccioná una obligación para consultar sus pagos.";
    public ServicePayment? SelectedServicePayment {get;set;}
    public string[] ServiceStates {get;}=["Todos","Pendiente","Pago parcial","Pagado","Vencido","Bonificado"];
    public string[] ServiceTypes {get;}=["Todos",..ServiceRules.Types];
    public ObservableCollection<string> ServicePeriods {get;}=[];
    private string servicePeriod=DateTime.Today.ToString("yyyy-MM"),serviceState="Todos",serviceType="Todos";
    private int? historyService;
    public string ServicePeriod {get=>servicePeriod;set{if(Set(ref servicePeriod,value))FilterServices();}}
    public string ServiceState {get=>serviceState;set{if(Set(ref serviceState,value))FilterServices();}}
    public string ServiceType {get=>serviceType;set{if(Set(ref serviceType,value))FilterServices();}}
    public string ServiceArsBalance=>Money.Format(ServiceRows.Where(r=>r.Obligation.Currency=="ARS").Sum(r=>r.Balance));
    public string ServiceUsdBalance=>Money.Format(ServiceRows.Where(r=>r.Obligation.Currency=="USD").Sum(r=>r.Balance),"USD");
    public int ServicePendingCount=>ServiceRows.Count(r=>r.BaseStatus=="Pendiente");
    public int ServicePartialCount=>ServiceRows.Count(r=>r.BaseStatus=="Pago parcial");
    public int ServicePaidCount=>ServiceRows.Count(r=>r.BaseStatus=="Pagado");
    public int ServiceOverdueCount=>ServiceRows.Count(r=>r.Overdue);
    public string ServicesSummary {get;private set;}="";
    public ICommand PrepareServicesCommand {get;private set;}=null!;
    public ICommand AddServiceCommand {get;private set;}=null!;
    public ICommand EditServiceCommand {get;private set;}=null!;
    public ICommand GenerateServicePeriodCommand {get;private set;}=null!;
    public ICommand EditServicePeriodCommand {get;private set;}=null!;
    public ICommand PayServiceCommand {get;private set;}=null!;
    public ICommand EditServicePaymentCommand {get;private set;}=null!;
    public ICommand VoidServicePaymentCommand {get;private set;}=null!;
    public ICommand ServiceHistoryCommand {get;private set;}=null!;
    public ICommand ClearServiceHistoryCommand {get;private set;}=null!;
    private void InitializeServiceCommands()
    {
        async Task Edit(EditorViewModel vm){if(dialogs.Show(vm)){await RefreshAsync();await Export(false);}}
        PrepareServicesCommand=Command(async()=>{if(Ready){await store!.PrepareServiceMonthAsync(DateTime.Today);await RefreshAsync();await Export(false);}});
        AddServiceCommand=Command(async()=>{if(Ready)await Edit(new ServiceEditor(store!,snapshot));});
        EditServiceCommand=Command(async()=>{if(Ready&&SelectedService!=null)await Edit(new ServiceEditor(store!,snapshot,SelectedService));});
        GenerateServicePeriodCommand=Command(async()=>{if(Ready&&SelectedService!=null)await Edit(new ObligationEditor(store!,SelectedService,ServiceRules.Propose(SelectedService,snapshot.Services.Obligations,DateTime.Today)));});
        EditServicePeriodCommand=Command(async()=>{if(Ready&&SelectedObligation is {} r)await Edit(new ObligationEditor(store!,r.Service,r.Obligation));});
        PayServiceCommand=Command(async()=>{if(Ready&&SelectedObligation is {} r&&r.Balance>0)await Edit(new ServicePaymentEditor(store!,r));});
        EditServicePaymentCommand=Command(async()=>{if(Ready&&SelectedObligation is {} r&&SelectedServicePayment is {Voided:false} p)await Edit(new ServicePaymentEditor(store!,r,p));});
        VoidServicePaymentCommand=Command(async()=>{if(Ready&&SelectedServicePayment is {Voided:false} p&&dialogs.Confirm("¿Anular este pago? Se conservará en el historial y se recalculará el saldo.")){await store!.SaveServicePaymentAsync(p with{Voided=true});await RefreshAsync();await Export(false);}});
        ServiceHistoryCommand=new RelayCommand(_=>{if(SelectedService!=null){historyService=SelectedService.Id;servicePeriod="Todos";Changed(nameof(ServicePeriod));FilterServices();}});
        ClearServiceHistoryCommand=new RelayCommand(_=>{historyService=null;ServicePeriod=DateTime.Today.ToString("yyyy-MM");FilterServices();});
    }
    private void RefreshServices()
    {
        var serviceId=SelectedService?.Id;Replace(Services,snapshot.Services.Services);SelectedService=Services.FirstOrDefault(s=>s.Id==serviceId)??Services.FirstOrDefault();Changed(nameof(SelectedService));
        var period=ServicePeriod;Replace(ServicePeriods,new[]{"Todos",DateTime.Today.ToString("yyyy-MM")}.Concat(snapshot.Services.Obligations.Select(o=>o.Period.ToString("yyyy-MM"))).Distinct().OrderByDescending(x=>x));servicePeriod=period;Changed(nameof(ServicePeriod));FilterServices();
    }
    private void FilterServices()
    {
        var selectedId=SelectedObligation?.Obligation.Id;
        var rows=snapshot.Services.Rows(DateTime.Today).Where(r=>(historyService==null||r.Service.Id==historyService)&&(ServicePeriod=="Todos"||r.Obligation.Period.ToString("yyyy-MM")==ServicePeriod)&&(ServiceState=="Todos"||(ServiceState=="Vencido"?r.Overdue:r.BaseStatus==ServiceState))&&(ServiceType=="Todos"||r.Service.Type==ServiceType)).OrderBy(r=>r.Balance==0).ThenByDescending(r=>r.Overdue).ThenBy(r=>r.Obligation.Due??DateTime.MaxValue).ToList();
        Replace(ServiceRows,rows);SelectedObligation=rows.FirstOrDefault(r=>r.Obligation.Id==selectedId);
        ServicesSummary=$"Pendientes {rows.Count(r=>r.BaseStatus=="Pendiente")}   ·   Parciales {rows.Count(r=>r.BaseStatus=="Pago parcial")}   ·   Pagadas {rows.Count(r=>r.BaseStatus=="Pagado")}   ·   Vencidas {rows.Count(r=>r.Overdue)}\nSaldo ARS {Money.Format(rows.Where(r=>r.Obligation.Currency=="ARS").Sum(r=>r.Balance))}     ·     Saldo USD {Money.Format(rows.Where(r=>r.Obligation.Currency=="USD").Sum(r=>r.Balance),"USD")}";
        if(historyService!=null)ServicesSummary="Historial de "+snapshot.Services.Services.FirstOrDefault(s=>s.Id==historyService)?.Name+"\n"+ServicesSummary;
        Changed(nameof(ServicesSummary));foreach(var name in new[]{nameof(ServiceArsBalance),nameof(ServiceUsdBalance),nameof(ServicePendingCount),nameof(ServicePartialCount),nameof(ServicePaidCount),nameof(ServiceOverdueCount)})Changed(name);
    }
}
