using System.Security.Cryptography;
using System.Windows.Input;
using PersonalFinances.Core;
namespace PersonalFinances.App;
public sealed class ServiceEntryEditor : EditorViewModel
{
    private readonly FinanceStore store;private readonly IDialogs dialogs;private ServiceSheetEntry entry;
    private string token=Guid.NewGuid().ToString("N"),amount="",payment="";private DateTime? date=DateTime.Today,due;
    private bool saved,paymentDateEdited;private ServicePayment? paymentToCorrect;private readonly HashSet<string> invalidDates=[];
    public string Amount {get=>amount;set=>Set(ref amount,value);}
    private bool HasNewPayment=>!string.IsNullOrWhiteSpace(payment);
    public string NewPayment
    {
        get=>payment;
        set
        {
            var wasNew=HasNewPayment;if(!Set(ref payment,value)||wasNew==HasNewPayment)return;
            if(!HasNewPayment){Set(ref date,paymentToCorrect?.Date??DateTime.Today,nameof(PaymentDate));paymentDateEdited=false;}
            else if(!paymentDateEdited)Set(ref date,DateTime.Today,nameof(PaymentDate));
            Changed(nameof(PaymentDateLabel));Changed(nameof(PaymentDateHint));
        }
    }
    public DateTime? PaymentDate {get=>date;set{if(Set(ref date,value))paymentDateEdited=true;}}
    public string PaymentDateLabel=>HasNewPayment?"Fecha del nuevo pago":paymentToCorrect!=null?"Fecha del último pago":"Fecha del pago";
    public string PaymentDateHint=>HasNewPayment?"Se agrega un pago independiente con esta fecha.":paymentToCorrect!=null?$"Sin un nuevo monto, esta fecha corrige el último pago de {Money.Format(paymentToCorrect.Amount,entry.Currency)}. Otros pagos: Historial.":"Ingresá un nuevo monto para registrar un pago.";
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
        ResetPaymentInput();
        HistoryCommand=new AsyncCommand(async()=>{if(Busy)return;dialogs.Show(new ServicePaymentsEditor(store,this.entry,()=>Task.CompletedTask,dialogs));await Reload();},e=>Error=LocalConfig.Friendly(e));
        GeneralCommand=new AsyncCommand(async()=>{if(Busy)return;if(dialogs.Show(new ServiceEditor(store,await store.ReadAsync(),this.entry.Service)))Close();},e=>Error=LocalConfig.Friendly(e));
        DeleteCommand=new RelayCommand(_=>{if(Busy)return;if(dialogs.Show(new DeleteServiceEditor(store,this.entry.Service,dialogs))){Deleted=true;Close();}});
        RenewCommand=new RelayCommand(_=>{if(Busy)return;var renewal=this.entry.History.FirstOrDefault(r=>r.Obligation.Period==this.entry.Month&&r.Obligation.Id!=this.entry.Obligation?.Id&&r.Obligation.Currency==(this.entry.Service.Type=="Tarjeta de crédito"?"ARS":this.entry.Service.Currency));this.entry=this.entry with{Obligation=renewal?.Obligation,Payments=renewal?.Payments??[],CoverageStart=null,CoverageEnd=null,RenewalRequired=true};Amount=renewal?.Obligation.AmountConfirmed==true?renewal.Obligation.Amount.ToString("N2",Money.Ar):"";ResetPaymentInput();Due=null;token=Guid.NewGuid().ToString("N");Changed(nameof(Coverage));Changed(nameof(CanRenew));Subtitle="Renovación anual. Se conserva el pago anterior.";Changed(nameof(Subtitle));});
    }
    public void DateValidation(string field,bool valid){if(valid)invalidDates.Remove(field);else invalidDates.Add(field);}
    private void ResetPaymentInput()
    {
        paymentToCorrect=entry.RecordedPayments.Where(p=>!p.Voided).OrderByDescending(p=>p.Date).ThenByDescending(p=>p.Id).FirstOrDefault();
        payment="";Changed(nameof(NewPayment));Set(ref date,paymentToCorrect?.Date??DateTime.Today,nameof(PaymentDate));paymentDateEdited=false;
        Changed(nameof(PaymentDateLabel));Changed(nameof(PaymentDateHint));
    }
    private async Task Reload(){var s=await store.ReadAsync();entry=ServiceSheet.Rows(s.Services,entry.Month,DateTime.Today).Single(r=>r.ServiceId==entry.ServiceId);Amount=entry.Confirmed?entry.Obligation!.Amount.ToString("N2",Money.Ar):"";Due=entry.Obligation?.Due;ResetPaymentInput();Subtitle=$"{entry.Currency} · Abonado: {Money.Format(entry.RecordedPaid,entry.Currency)} · Saldo: {Money.Format(entry.RecordedBalance,entry.Currency)}";Changed(nameof(Subtitle));Changed(nameof(Coverage));Changed(nameof(CanRenew));}
    protected override async Task Save()
    {
        if(saved)return;
        if(invalidDates.Count>0)throw new RuleException("Revisá las fechas: usá día/mes/año o el calendario.");
        var o=entry.Obligation is {} original?original with{}:new ServiceObligation{ServiceId=entry.ServiceId,Period=entry.Month,Currency=entry.Currency};
        o.AmountConfirmed=!string.IsNullOrWhiteSpace(Amount);o.Amount=o.AmountConfirmed?ServiceEditor.ParseZero(Amount):0;o.Due=Due?.Date;
        decimal? newPayment=string.IsNullOrWhiteSpace(NewPayment)?null:Money.Parse(NewPayment);
        var correction=newPayment==null&&paymentToCorrect!=null&&PaymentDate?.Date!=paymentToCorrect.Date?paymentToCorrect.Id:(long?)null;
        await store.SaveServiceChangeAsync(new(o,newPayment,PaymentDate?.Date,ServiceSheet.PaymentStamp(entry.RecordedPayments),token,correction));
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
