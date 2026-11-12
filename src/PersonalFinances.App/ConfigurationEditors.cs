using System.Windows.Input;
using PersonalFinances.Core;
namespace PersonalFinances.App;
public sealed class ConnectionEditor : EditorViewModel
{
    private string host="localhost",port="3306",user="",password="",database="personal_finances",status="";
    private readonly ConnectionProfile? previous;private ConnectionProfile? tested;private bool passwordEdited,missing;
    public string Host {get=>host;set{if(Set(ref host,value))Invalidate();}}public string Port {get=>port;set{if(Set(ref port,value))Invalidate();}}
    public string User {get=>user;set{if(Set(ref user,value))Invalidate();}}public string Password {get=>password;set{passwordEdited=true;if(Set(ref password,value))Invalidate();}}
    public string Database {get=>database;set{if(Set(ref database,value))Invalidate();}}public string Status {get=>status;private set=>Set(ref status,value);}
    public bool MissingDatabase {get=>missing;private set=>Set(ref missing,value);}public bool CreateDatabase {get;set;}
    public string PasswordHelp=>previous==null?"Ingresá la contraseña del usuario MySQL de esta instalación.":"La contraseña actual está guardada. Ingresá una nueva solo si querés cambiarla.";
    public ConnectionProfile? Result {get;private set;}public ICommand TestCommand {get;}
    public ConnectionEditor(ConnectionProfile? source=null)
    {
        previous=source;Title="Conexión a base de datos";SaveLabel="Guardar configuración";Subtitle="Probá la conexión y guardá. Cambiar de base no modifica los datos de la anterior.";
        if(source!=null){host=source.Host;port=source.Port.ToString();user=source.User;database=source.Database;}
        TestCommand=new AsyncCommand(async()=>{if(Busy)return;Busy=true;Error="";try{await Test();}finally{Busy=false;}},e=>Error=LocalConfig.Friendly(e));
    }
    private void Invalidate(){tested=null;Status="";MissingDatabase=false;}
    private ConnectionProfile Profile()
    {
        if(!uint.TryParse(Port,out var number))throw new RuleException("Ingresá un puerto válido de MySQL.");
        var p=new ConnectionProfile(Host.Trim(),number,User.Trim(),!passwordEdited&&previous!=null?previous.Password:Password,Database.Trim());DatabaseSetup.Validate(p);return p;
    }
    private async Task Test()
    {
        var p=Profile();var result=await DatabaseSetup.ProbeAsync(p);tested=p;MissingDatabase=!result.DatabaseExists;Status=result.Message;
    }
    protected override async Task Save()
    {
        var p=Profile();if(tested!=p)await Test();
        if(MissingDatabase&&!CreateDatabase)throw new RuleException("La base no existe. Marcá Crear la base si no existe o elegí una base previamente creada por tu administrador.");
        await DatabaseSetup.PrepareAsync(p,CreateDatabase);
        if(previous==null)LocalConfig.UpdatePreferences(preferences=>preferences with{LegacyExportPathMigrated=true});
        LocalConfig.Save(p);Result=p;password="";Close();
    }
}
public sealed class ExportEditor : EditorViewModel
{
    private readonly IDialogs dialogs;private string folder="",status="",fileName=FinanceStore.ExportFileName;
    public string Folder {get=>folder;set{if(Set(ref folder,value)){Status="";Changed(nameof(Destination));}}}
    public string FileName {get=>fileName;set{if(Set(ref fileName,value)){Status="";Changed(nameof(Destination));}}}
    public string Destination {get{try{var name=ExportDestination.NormalizeFileName(FileName);return string.IsNullOrWhiteSpace(Folder)?name:System.IO.Path.Combine(Folder,name);}catch(Exception e)when(e is RuleException or ArgumentException){return "Revisá el nombre del archivo y la carpeta de destino.";}}}
    public string Status {get=>status;private set=>Set(ref status,value);}public ICommand PickCommand {get;}public ICommand TestCommand {get;}
    public ExportEditor(IDialogs dialogs,string? path)
    {
        this.dialogs=dialogs;folder=path==null?"":System.IO.Path.GetDirectoryName(path)??"";fileName=path==null?FinanceStore.ExportFileName:System.IO.Path.GetFileName(path);
        Title="Exportación de resumen";SaveLabel="Guardar configuración";Subtitle="Elegí una carpeta local o sincronizada con Drive. Solo se exporta el TXT de consulta.";
        PickCommand=new RelayCommand(_=>{if(!Busy&&this.dialogs.PickFolder() is {} selected)Folder=selected;});
        TestCommand=new AsyncCommand(async()=>{if(Busy)return;Busy=true;Error="";try{FileName=ExportDestination.NormalizeFileName(FileName);await ExportDestination.TestAsync(Folder);Status="Carpeta accesible. Prueba de escritura correcta.";}finally{Busy=false;}},e=>Error=LocalConfig.Friendly(e));
    }
    protected override async Task Save(){var name=ExportDestination.NormalizeFileName(FileName);await ExportDestination.TestAsync(Folder);LocalConfig.SetExportDestination(Folder,name);FileName=name;Close();}
}
