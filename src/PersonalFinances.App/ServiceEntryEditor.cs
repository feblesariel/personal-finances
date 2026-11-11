using System.Security.Cryptography;
using System.Windows.Input;
using PersonalFinances.Core;
namespace PersonalFinances.App;
public sealed class ServiceEntryEditor : EditorViewModel
{
    private readonly FinanceStore store;private readonly IDialogs dialogs;private ServiceSheetEntry entry;
    private string token=Guid.NewGuid().ToString("N"),amount="",payment="";private DateTime? date=DateTime.Today,due;
    private bool saved;private readonly HashSet<string> invalidDates=[];
    public string Amount {get=>amount;set=>Set(ref amount,value);}
    public string NewPayment {get=>payment;set=>Set(ref payment,value);}
    public DateTime? PaymentDate {get=>date;set=>Set(ref date,value);}
    public DateTime? Due {get=>due;set=>Set(ref due,value);}
    public string Currency=>entry.Currency;
    public string Coverage=>entry.CoverageEnd is {} end?$"Vigencia: {entry.CoverageStart:dd/MM/yyyy} al {end.AddDays(-1):dd/MM/yyyy}. Fecha original conservada.":"";
    public bool CanRenew=>entry.Service.Frequency=="Anual"&&entry.CoverageEnd!=null;
    public bool Deleted {get;private set;}
    public ICommand HistoryCommand {get;}public ICommand GeneralCommand {get;}public ICommand DeleteCommand {get;}public ICommand RenewCommand {get;}
    public ServiceEntryEditor(FinanceStore store,ServiceSheetEntry entry,IDialogs dialogs)
    {
        this.store=store;this.entry=entry;this.dialogs=dialogs;
        Title=entry.Name+" — "+entry.Month.ToString("MMMM yyyy",Money.Ar);SaveLabel="Guardar";
        Subtitle=$"{entry.Currency} · Abonado: {Money.Format(entry.RecordedPaid,entry.Currency)} · Saldo: {Money.Format(entry.RecordedBalance,entry.Currency)}";
        Amount=entry.Confirmed?entry.Obligation!.Amount.ToString("N2",Money.Ar):"";Due=entry.Obligation?.Due;
        HistoryCommand=new AsyncCommand(async()=>{if(Busy)return;dialogs.Show(new ServicePaymentsEditor(store,this.entry,()=>Task.CompletedTask,dialogs));await Reload();},e=>Error=LocalConfig.Friendly(e));
        GeneralCommand=new AsyncCommand(async()=>{if(Busy)return;if(dialogs.Show(new ServiceEditor(store,await store.ReadAsync(),this.entry.Service)))Close();},e=>Error=LocalConfig.Friendly(e));
        DeleteCommand=new RelayCommand(_=>{if(Busy)return;if(dialogs.Show(new DeleteServiceEditor(store,this.entry.Service,dialogs))){Deleted=true;Close();}});
        RenewCommand=new RelayCommand(_=>{if(Busy)return;var renewal=this.entry.History.FirstOrDefault(r=>r.Obligation.Period==this.entry.Month&&r.Obligation.Id!=this.entry.Obligation?.Id&&r.Obligation.Currency==(this.entry.Service.Type=="Tarjeta de crédito"?"ARS":this.entry.Service.Currency));this.entry=this.entry with{Obligation=renewal?.Obligation,Payments=renewal?.Payments??[],CoverageStart=null,CoverageEnd=null,RenewalRequired=true};Amount=renewal?.Obligation.AmountConfirmed==true?renewal.Obligation.Amount.ToString("N2",Money.Ar):"";NewPayment="";PaymentDate=DateTime.Today;Due=null;token=Guid.NewGuid().ToString("N");Changed(nameof(Coverage));Changed(nameof(CanRenew));Subtitle="Renovación anual. Se conserva el pago anterior.";Changed(nameof(Subtitle));});
    }
    public void DateValidation(string field,bool valid){if(valid)invalidDates.Remove(field);else invalidDates.Add(field);}
    private async Task Reload(){var s=await store.ReadAsync();entry=ServiceSheet.Rows(s.Services,entry.Month,DateTime.Today).Single(r=>r.ServiceId==entry.ServiceId);Amount=entry.Confirmed?entry.Obligation!.Amount.ToString("N2",Money.Ar):"";Due=entry.Obligation?.Due;NewPayment="";Subtitle=$"{entry.Currency} · Abonado: {Money.Format(entry.RecordedPaid,entry.Currency)} · Saldo: {Money.Format(entry.RecordedBalance,entry.Currency)}";Changed(nameof(Subtitle));Changed(nameof(Coverage));Changed(nameof(CanRenew));}
    protected override async Task Save()
    {
        if(saved)return;
        if(invalidDates.Count>0)throw new RuleException("Revisá las fechas: usá día/mes/año o el calendario.");
        var o=entry.Obligation is {} original?original with{}:new ServiceObligation{ServiceId=entry.ServiceId,Period=entry.Month,Currency=entry.Currency};
        o.AmountConfirmed=!string.IsNullOrWhiteSpace(Amount);o.Amount=o.AmountConfirmed?ServiceEditor.ParseZero(Amount):0;o.Due=Due?.Date;
        decimal? newPayment=string.IsNullOrWhiteSpace(NewPayment)?null:Money.Parse(NewPayment);
        await store.SaveServiceChangeAsync(new(o,newPayment,PaymentDate?.Date,ServiceSheet.PaymentStamp(entry.RecordedPayments),token));
        saved=true;NewPayment="";Close();
    }
}
public sealed class DeleteServiceEditor : EditorViewModel
{
    private readonly FinanceStore store;private readonly RecurringService service;private readonly IDialogs dialogs;private ServiceRecovery? recovery;
    public bool ConfirmDelete {get;set;}
    public string BackupStatus {get;private set;}="Primero generá un respaldo cifrado recuperable. No se elimina nada sin una copia verificada.";
    public string Warning=>"Se eliminará definitivamente "+service.Name+", todos sus períodos, pagos y observaciones. No afecta consumos de tarjetas ni otros servicios.";
    public ICommand BackupCommand {get;}
    public DeleteServiceEditor(FinanceStore store,RecurringService service,IDialogs dialogs)
    {
        this.store=store;this.service=service;this.dialogs=dialogs;Title="Eliminar servicio · "+service.Name;Subtitle="Esta operación elimina todo el historial del servicio.";SaveLabel="Eliminar definitivamente";
        BackupCommand=new AsyncCommand(async()=>
        {
            if(Busy)return;var path=dialogs.SaveBackup();if(path==null)return;var editor=new BackupExportEditor(store,path);
            if(!dialogs.Show(editor)||editor.Result==null)return;
            await using var file=System.IO.File.OpenRead(path);recovery=new(path,Convert.ToHexString(await SHA256.HashDataAsync(file)),BackupValidation.Fingerprint(editor.Result),service.Id,service.Version);
            BackupStatus="Respaldo verificado: "+path+". Conservá la contraseña para recuperarlo.";Changed(nameof(BackupStatus));
        },e=>Error=LocalConfig.Friendly(e));
    }
    protected override async Task Save(){if(recovery==null)throw new RuleException("Generá un respaldo verificado antes de eliminar.");await store.DeleteServiceWithRecoveryAsync(recovery,ConfirmDelete);Close();}
}
