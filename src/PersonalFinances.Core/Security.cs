using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Dapper;
using MySqlConnector;

namespace PersonalFinances.Core;
public sealed record ConnectionProfile(string Host,uint Port,string User,string Password,string Database="personal_finances")
{
    [JsonIgnore] public string ConnectionString=>new MySqlConnectionStringBuilder{Server=Host,Port=Port,UserID=User,Password=Password,Database=Database,ConnectionTimeout=5,DefaultCommandTimeout=30,AllowPublicKeyRetrieval=Host is "localhost" or "127.0.0.1",SslMode=MySqlSslMode.Preferred}.ConnectionString;
    [JsonIgnore] public string Identity=>Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{Host}|{Port}|{User}|{Database}")));
}
public sealed record AppPreferences(string? ExportPath=null,bool LegacyExportPathMigrated=false,long? ExportedRevision=null,string? ExportedConnection=null,string ServiceSort="ServiceId:Ascending",string? ExportedServiceSort=null);
public static class LocalConfig
{
    // In-process injection isolates test profiles; the installed app uses the Windows user profile.
    public static string Folder {get;set;}=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PersonalFinances");
    public static string FilePath=>Path.Combine(Folder,"connection.dpapi");
    public static string PreferencesPath=>Path.Combine(Folder,"preferences.json");
    private static readonly object Gate=new();
    public static ConnectionProfile? Load()=>File.Exists(FilePath)?JsonSerializer.Deserialize<ConnectionProfile>(ProtectedData.Unprotect(File.ReadAllBytes(FilePath),null,DataProtectionScope.CurrentUser)):null;
    public static void Save(ConnectionProfile p)
    {
        var bytes=ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(p),null,DataProtectionScope.CurrentUser);
        lock(Gate){Directory.CreateDirectory(Folder);var tmp=FilePath+".tmp";File.WriteAllBytes(tmp,bytes);File.Move(tmp,FilePath,true);}
    }
    public static AppPreferences Preferences(){lock(Gate)return File.Exists(PreferencesPath)?JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(PreferencesPath))??new():new();}
    public static void UpdatePreferences(Func<AppPreferences,AppPreferences> change)
    {
        lock(Gate){var next=change(Preferences());Directory.CreateDirectory(Folder);var temp=PreferencesPath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(next,new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,PreferencesPath,true);}
    }
    public static string? ResolveExportPath(string? legacyPath=null,bool finishMigration=false)
    {
        var p=Preferences();if(p.LegacyExportPathMigrated)return p.ExportPath;
        if(legacyPath==null&&!finishMigration)return p.ExportPath;
        // Adopt an old installation's database setting only once. Never assume a drive letter.
        UpdatePreferences(current=>current.LegacyExportPathMigrated?current:current with{ExportPath=current.ExportPath??(legacyPath!=null&&Path.IsPathFullyQualified(legacyPath)?legacyPath:null),LegacyExportPathMigrated=true});
        return Preferences().ExportPath;
    }
    public static void SetExportFolder(string folder)
    {
        var path=Path.Combine(Path.GetFullPath(folder),FinanceStore.ExportFileName);
        UpdatePreferences(p=>p with{ExportPath=path,LegacyExportPathMigrated=true,ExportedRevision=null,ExportedConnection=null});
    }
    public static void Log(Exception e)
    {
        try{Directory.CreateDirectory(Folder);var path=Path.Combine(Folder,"errors.log");if(File.Exists(path)&&new FileInfo(path).Length>1_000_000)File.Move(path,path+".old",true);File.AppendAllText(path,$"{DateTime.Now:O} {e.GetType().Name} {(e is MySqlException m?m.Number:e.HResult)}\n");}catch{}
    }
    public static string Friendly(Exception ex)=>ex is RuleException?ex.Message:ex is MySqlException m?m.Number switch
    {
        1045=>"MySQL rechazó el usuario o la contraseña. Revisá la conexión y volvé a probar.",
        1044 or 1142 or 1227=>"El usuario no tiene permisos suficientes. Pedí al administrador acceso a la base elegida y permisos SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX y REFERENCES. También podés conectar una base previamente creada.",
        1049=>"La base no existe. Probá la conexión y elegí crearla, o ingresá el nombre de una base previamente creada.",
        _=>"No se pudo conectar o completar la operación en MySQL. Verificá el servidor, el puerto y que el servicio esté iniciado. Podés reintentar o cambiar la conexión; los datos guardados se conservan."
    }:"No se pudo completar la operación. Revisá los datos y volvé a intentar. El registro técnico está en Configuración.";
}
public sealed record ConnectionProbe(bool DatabaseExists,string Message);
public static class DatabaseSetup
{
    public static void Validate(ConnectionProfile p)
    {
        if(string.IsNullOrWhiteSpace(p.Host)||p.Host.Length>255||p.Port is 0 or >65535||string.IsNullOrWhiteSpace(p.User))throw new RuleException("Completá servidor, puerto y usuario de MySQL.");
        if(!Regex.IsMatch(p.Database,@"\A[a-zA-Z0-9_]{1,64}\z"))throw new RuleException("El nombre de la base debe tener de 1 a 64 letras, números o guiones bajos.");
    }
    public static async Task<ConnectionProbe> ProbeAsync(ConnectionProfile p)
    {
        Validate(p);
        try{await using var c=new MySqlConnection(p.ConnectionString);await c.OpenAsync();await c.ExecuteScalarAsync<int>("SELECT 1");return new(true,"Conexión correcta. La base está disponible.");}
        catch(MySqlException e) when(e.Number==1049)
        {
            await using var c=new MySqlConnection((p with{Database=""}).ConnectionString);await c.OpenAsync();
            return new(false,"El servidor responde, pero la base no existe. Podés crearla si tu usuario tiene permiso o indicar una base previamente creada.");
        }
    }
    public static async Task PrepareAsync(ConnectionProfile p,bool createIfMissing)
    {
        var probe=await ProbeAsync(p);
        if(!probe.DatabaseExists)
        {
            if(!createIfMissing)throw new RuleException("La base no existe. Marcá Crear la base si no existe o indicá una ya creada.");
            await using var server=new MySqlConnection((p with{Database=""}).ConnectionString);await server.OpenAsync();
            await server.ExecuteAsync($"CREATE DATABASE IF NOT EXISTS `{p.Database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci");
        }
        await using var c=new MySqlConnection(p.ConnectionString);await c.OpenAsync();
        var tables=(await c.QueryAsync<string>("SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(tables.Count>0&&!tables.Contains("schema_migrations"))throw new RuleException("La base contiene tablas de otro sistema. Elegí una base vacía o una base existente de Personal Finances. No se modificó su contenido.");
        await new FinanceStore(p).InitializeAsync();await new FinanceStore(p).ReadAsync();
    }
}
public static class ExportDestination
{
    public static async Task TestAsync(string folder)
    {
        if(!Path.IsPathFullyQualified(folder)||!Directory.Exists(folder))throw new RuleException("Seleccioná una carpeta existente y accesible.");
        var test=Path.Combine(folder,".personal-finances-test-"+Guid.NewGuid().ToString("N")+".tmp");
        try{await using var file=new FileStream(test,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.Asynchronous|FileOptions.WriteThrough);await file.WriteAsync(new byte[]{80,70});await file.FlushAsync();}
        catch(UnauthorizedAccessException){throw new RuleException("No tenés permiso para escribir en esta carpeta. Elegí otra ubicación.");}
        catch(IOException){throw new RuleException("La carpeta no está disponible para escritura. Verificá la unidad y volvé a probar.");}
        finally{if(File.Exists(test))File.Delete(test);}
    }
}
