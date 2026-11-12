using System.Security.Cryptography;
using System.Windows.Input;
using PersonalFinances.Core;

namespace PersonalFinances.App;

public sealed class SimpleServiceEditor : EditorViewModel
{
    private readonly FinanceStore store;private readonly IDialogs dialogs;private readonly SimpleServiceEntry? entry;private readonly DateTime month;
    private string name,frequency,currency,amount,notes;private DateTime? date;private bool saved,invalidDate;
    public string Name {get=>name;set=>Set(ref name,value);}
    public string Frequency {get=>frequency;set=>Set(ref frequency,value);}
    public string Currency {get=>currency;set=>Set(ref currency,value);}
    public string Amount {get=>amount;set=>Set(ref amount,value);}
    public string Notes {get=>notes;set=>Set(ref notes,value);}
    public DateTime? PaymentDate {get=>date;set=>Set(ref date,value);}
    public string[] Frequencies {get;}=["Mensual","Anual"];
    public string[] Currencies=>entry?.Service.Type=="Tarjeta de crédito"?["ARS"]:["ARS","USD"];
    public bool Existing=>entry!=null;
    public bool Deleted {get;private set;}
    public ICommand DeleteCommand {get;}
    public SimpleServiceEditor(FinanceStore store,DateTime month,IDialogs dialogs,SimpleServiceEntry? entry=null)
    {
        this.store=store;this.month=ServiceSheet.Month(month);this.dialogs=dialogs;this.entry=entry;
        name=entry?.Name??"";frequency=entry?.Frequency??"Mensual";currency=entry?.Currency??"ARS";amount=entry?.Paid?.ToString("N2",Money.Ar)??"";notes=entry?.Notes??"";date=entry?.PaymentDate??DateTime.Today;
        Title=entry==null?"Agregar servicio":entry.Name+" — "+this.month.ToString("MMMM yyyy",Money.Ar);Subtitle="Monto y fecha: pagado. Monto vacío: pendiente.";SaveLabel="Guardar";
        DeleteCommand=new AsyncCommand(async()=>
        {
            if(Busy||entry==null||!dialogs.Confirm("¿Eliminar definitivamente "+entry.Name+" y sus datos de todos los períodos? Esta acción no afecta consumos de tarjetas."))return;
            Busy=true;Error="";
            try
            {
                var folder=System.IO.Path.Combine(LocalConfig.Folder,"Backups");System.IO.Directory.CreateDirectory(folder);var path=System.IO.Path.Combine(folder,"Antes-de-eliminar-servicio-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]+".pfinance");
                var password=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));var document=await new BackupService(store,BackupUi.Version).ExportAsync(path,password);
                await System.IO.File.WriteAllBytesAsync(System.IO.Path.ChangeExtension(path,".key.dpapi"),ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(password),null,DataProtectionScope.CurrentUser));
                await using var file=System.IO.File.OpenRead(path);var hash=Convert.ToHexString(await SHA256.HashDataAsync(file));
                await store.DeleteServiceWithRecoveryAsync(new(path,hash,BackupValidation.Fingerprint(document),entry.ServiceId,entry.Service.Version),true);Deleted=true;Close();
            }
            finally{Busy=false;}
        },e=>Error=LocalConfig.Friendly(e));
    }
    public void DateValidation(bool valid)=>invalidDate=!valid;
    protected override async Task Save()
    {
        if(saved)return;
        decimal? paid=string.IsNullOrWhiteSpace(Amount)?null:ServiceEditor.ParseZero(Amount);
        if(paid!=null&&invalidDate)throw new RuleException("Ingresá una fecha válida: día/mes/año.");
        var service=(entry?.Service??new RecurringService()) with{Name=Name.Trim(),Frequency=Frequency,Currency=Currency};
        await store.SaveSimpleServiceAsync(new(service,month,paid,paid==null?null:PaymentDate?.Date,Notes,Currency,entry?.Stamp??""));saved=true;Close();
    }
}
