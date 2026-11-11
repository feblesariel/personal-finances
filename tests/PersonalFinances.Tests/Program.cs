using System.Text;
using Dapper;
using MySqlConnector;
using PersonalFinances.Core;

var password=Environment.GetEnvironmentVariable("PF_ADMIN_PASSWORD")??throw new Exception("Definí PF_ADMIN_PASSWORD solo para este proceso.");
var admin=new ConnectionProfile("localhost",3306,Environment.GetEnvironmentVariable("PF_ADMIN_USER")??throw new Exception("Definí PF_ADMIN_USER para las pruebas."),password);
var db="personal_finances_tests_"+DateTime.UtcNow.ToString("yyyyMMddHHmmss");
await using var root=new MySqlConnection((admin with {Database=""}).ConnectionString);await root.OpenAsync();
await root.ExecuteAsync($"CREATE DATABASE `{db}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci");
var profile=admin with {Database=db};var storeTest=new FinanceStore(profile);var results=new List<string>();
var temp=Path.Combine(Path.GetTempPath(),"PersonalFinancesTests_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
var originalConfig=LocalConfig.Folder;LocalConfig.Folder=Path.Combine(temp,"profile");
void Check(bool condition,string label){if(!condition)throw new Exception("FAIL: "+label);results.Add(label);Console.WriteLine("PASS: "+label);}
async Task Reject(Func<Task> action,string label){try{await action();}catch(RuleException){Check(true,label);return;}throw new Exception("FAIL (accepted): "+label);}
async Task<Movement> Find(long id)=>(await storeTest.ReadAsync()).Movements.Single(m=>m.Id==id);
Movement New(int card,int cat,decimal amount,string currency,DateTime date,bool refund=false,long? original=null)=>new(){CardId=card,CategoryId=cat,Amount=amount,Currency=currency,Date=date,Refund=refund,OriginalId=original,Description="Prueba",Notes="Integración aislada"};
try
{
    await storeTest.InitializeAsync();await storeTest.InitializeAsync();var s=await storeTest.ReadAsync();
    Check(s.Cards.Count==2&&s.Categories.Count==12,"Inicialización y migraciones idempotentes");
    Check(LocalConfig.ResolveExportPath()==null&&!LocalConfig.Preferences().LegacyExportPathMigrated,"Leer una pantalla antes de conectar no pierde la migración de la carpeta anterior");
    var legacy=Path.Combine(temp,FinanceStore.ExportFileName);Check(LocalConfig.ResolveExportPath(legacy,true)==legacy&&LocalConfig.Preferences().LegacyExportPathMigrated,"Destino heredado adoptado una sola vez sin fijar una unidad en código");
    Check((await DatabaseSetup.ProbeAsync(profile)).DatabaseExists,"Prueba de conexión a una base configurada con usuario propio");
    await DatabaseSetup.PrepareAsync(profile,false);Check((await storeTest.ReadAsync()).Cards.Count==2,"Conectar una base existente no duplica semillas");
    LocalConfig.Save(profile);Check(LocalConfig.Load()==profile&&!File.ReadAllText(LocalConfig.FilePath).Contains("\"Password\""),"Credenciales persistidas y cifradas con DPAPI fuera del repositorio");
    var originalCredential=File.ReadAllBytes(LocalConfig.FilePath);
    try{await DatabaseSetup.ProbeAsync(profile with{Port=1});throw new Exception("Conexión inaccesible aceptada");}catch(MySqlException e){Check(LocalConfig.Friendly(e).Contains("reintentar")&&File.ReadAllBytes(LocalConfig.FilePath).SequenceEqual(originalCredential),"MySQL inaccesible: error controlado y configuración anterior conservada");}
    try{DatabaseSetup.Validate(profile with{Database="invalid;database"});throw new Exception("Nombre inválido aceptado");}catch(RuleException){Check(true,"Validación del nombre de base antes de ejecutar DDL");}
    await ExportDestination.TestAsync(temp);LocalConfig.SetExportFolder(temp);
    Check(LocalConfig.Preferences().ExportPath==Path.Combine(temp,FinanceStore.ExportFileName)&&!Directory.GetFiles(temp,".personal-finances-test-*").Any(),"Carpeta por instalación y prueba de escritura sin residuos");
    var alternate=Path.Combine(temp,"other");Directory.CreateDirectory(alternate);LocalConfig.SetExportFolder(alternate);Check(LocalConfig.Preferences().ExportPath==Path.Combine(alternate,FinanceStore.ExportFileName),"Cambio de destino persistido localmente");
    LocalConfig.SetExportFolder(temp);
    Check(Money.Parse("260.440,37")==260440.37m&&Money.Parse("12,99")==12.99m,"Formato argentino exacto");
    try{Money.Parse("12.99");throw new Exception("Accepted invalid amount");}catch(RuleException){Check(true,"Rechazo de importes ambiguos");}
    var card=await storeTest.SaveCardAsync(new(){Name="TEST CARD"});var cat=s.Categories.First(c=>c.Name=="Supermercado").Id;
    var p1=new Period{CardId=card,Name="Noviembre 2026",Start=new(2026,10,21),Closing=new(2026,11,20),Due=new(2026,12,1)};p1.Id=await storeTest.SavePeriodAsync(p1);
    var p2=new Period{CardId=card,Name="Diciembre 2026",Start=new(2026,11,21),Closing=new(2026,12,20),Due=new(2027,1,1)};p2.Id=await storeTest.SavePeriodAsync(p2);
    Check(p1.Id>0&&p2.Id>0,"Crear tarjeta y períodos independientes");
    await Reject(()=>storeTest.SavePeriodAsync(new(){CardId=card,Name="Superpuesto",Start=new(2026,11,20),Closing=new(2026,12,1),Due=new(2026,12,2)}),"Evitar períodos superpuestos");
    var a=New(card,cat,100.10m,"ARS",new(2026,11,20));a.Id=await storeTest.SaveMovementAsync(a);
    var u=New(card,cat,32.50m,"USD",new(2026,11,20));u.Id=await storeTest.SaveMovementAsync(u);
    var b=New(card,cat,50m,"ARS",new(2026,11,21));b.Id=await storeTest.SaveMovementAsync(b);
    Check((await Find(a.Id)).PeriodId==p1.Id&&(await Find(b.Id)).PeriodId==p2.Id,"Consumos antes y después del cierre, fechas inclusivas");
    s=await storeTest.ReadAsync();Check(s.Movements.Where(m=>m.PeriodId==p1.Id&&m.Currency=="ARS").Sum(m=>m.Signed)==100.10m&&s.Movements.Where(m=>m.Currency=="USD").Sum(m=>m.Signed)==32.50m,"ARS y USD separados");
    var r=New(card,cat,10m,"ARS",new(2026,11,20),true,a.Id);r.Id=await storeTest.SaveMovementAsync(r);s=await storeTest.ReadAsync();
    Check(s.Movements.Where(m=>m.PeriodId==p1.Id&&m.Currency=="ARS").Sum(m=>m.Signed)==90.10m,"Reintegro vinculado y total neto");
    var invalid=New(card,cat,1m,"USD",new(2026,11,20),true,a.Id);await Reject(()=>storeTest.SaveMovementAsync(invalid),"Rechazo de vínculo con moneda distinta");
    await Reject(()=>storeTest.DeleteMovementAsync(Find(a.Id).GetAwaiter().GetResult()),"Proteger consumo con reintegro vinculado");
    a=await Find(a.Id);var stale=a.Copy();a.Amount=110.10m;await storeTest.SaveMovementAsync(a);stale.Amount=120;
    await Reject(()=>storeTest.SaveMovementAsync(stale),"Ediciones concurrentes no pisan información");
    Check((await Find(a.Id)).Amount==110.10m,"Editar consumo");
    await storeTest.DeleteMovementAsync(await Find(r.Id));Check(!(await storeTest.ReadAsync()).Movements.Any(m=>m.Id==r.Id),"Eliminar con conservación de auditoría");
    p1.Closing=new(2026,11,19);await storeTest.SavePeriodAsync(p1);Check((await Find(a.Id)).PeriodId==null&&(await Find(u.Id)).PeriodId==null,"Cambio de cierre conserva movimientos pendientes");
    p1.Closing=new(2026,11,20);await storeTest.SavePeriodAsync(p1);Check((await Find(a.Id)).PeriodId==p1.Id,"Restaurar cierre reasigna automáticamente");
    var manual=New(card,cat,7m,"ARS",new(2026,11,20));manual.ManualPeriod=true;manual.PeriodId=p2.Id;manual.Id=await storeTest.SaveMovementAsync(manual);
    await storeTest.SavePeriodAsync(p1);Check((await Find(manual.Id)).PeriodId==p2.Id,"Respetar imputación manual fuera de fechas");
    var outside=New(card,cat,8m,"ARS",new(2026,10,1));outside.Id=await storeTest.SaveMovementAsync(outside);Check((await Find(outside.Id)).PeriodId==null,"Fuera de períodos no se oculta ni descarta");
    var once=New(card,cat,2.34m,"ARS",new(2026,11,10));var ids=await Task.WhenAll(storeTest.SaveMovementAsync(once),storeTest.SaveMovementAsync(once.Copy()));
    Check(ids[0]==ids[1]&&(await storeTest.ReadAsync()).Movements.Count(m=>m.Token==once.Token)==1,"Idempotencia ante doble guardado simultáneo");
    s=await storeTest.ReadAsync();Check(s.Movements.Where(m=>m.CategoryId==cat&&m.Currency=="ARS").Sum(m=>m.Signed)==177.44m,"Cálculos y agrupaciones por categoría");
    var import=TextImport.Parse(new[]{"22/10/2026 --- COTO --- 10.699,00 <-- VISA","23/10/2026 --- NETFLIX --- US$ 12,99 <-- MASTER --> REFUND","fecha inválida","22/10/2026 --- COTO --- 10.699,00 <-- VISA"},s);
    Check(import[0].Movement?.Amount==10699m&&import[0].Movement?.CategoryName=="Supermercado"&&import[1].Movement is {Currency:"USD",Refund:true,CardName:"MASTERCARD"},"Importar TXT, MASTER, categoría y REFUND");
    Check(import[2].Movement==null&&!import[2].Include&&import[3].Duplicate&&!import[3].Include,"Importación excluye ambiguos y duplicados");
    var rollbackGood=New(card,cat,55,"ARS",new(2026,11,10));var rollbackBad=New(card,cat,-1,"ARS",new(2026,11,10));await Reject(()=>storeTest.ImportAsync([rollbackGood,rollbackBad]),"Importación transaccional ante error");
    Check(!(await storeTest.ReadAsync()).Movements.Any(m=>m.Token==rollbackGood.Token),"Rollback de importación sin registros parciales");
    var export=new ExportService(storeTest);var report=Path.Combine(temp,"RESUMEN_TARJETAS.txt");LocalConfig.SetExportFolder(temp);
    Check(await export.TryAsync(true),"Exportación UTF-8 desde MySQL");var txt=await File.ReadAllTextAsync(report,Encoding.UTF8);
    Check(txt.Contains("US$ 32,50")&&txt.Contains("SIN PERÍODO CONFIGURADO")&&txt.Contains("PESOS:       $ 112,44")&&txt.Contains("Movimientos: 3"),"TXT compacto: totales exactos del período y monedas separadas");
    Check(!txt.Contains("GASTOS POR CATEGORÍA")&&!txt.Contains("MOVIMIENTOS")&&!txt.Contains("HISTORIAL")&&!txt.Contains("Integración aislada")&&!txt.Contains("Supermercado"),"TXT excluye categorías, detalle, observaciones e historial");
    var today=DateTime.Today;
    var compactSnapshot=new Snapshot([new(){Id=1,Name="VISA"},new(){Id=2,Name="MASTERCARD"}],[],
        [new(){Id=1,CardId=1,Name="ACTIVO",Start=today.AddDays(-10),Closing=today.AddDays(10),Due=today.AddDays(20)},new(){Id=2,CardId=1,Name="HISTORICO",Start=today.AddDays(-40),Closing=today.AddDays(-11),Due=today},new(){Id=3,CardId=2,Name="ACTIVO MASTER",Start=today.AddDays(-5),Closing=today.AddDays(15),Due=today.AddDays(25)}],
        [new(){CardId=1,PeriodId=1,Amount=123.45m,Currency="ARS"},new(){CardId=1,PeriodId=1,Amount=23.45m,Currency="ARS",Refund=true},new(){CardId=1,PeriodId=1,Amount=20m,Currency="USD"},new(){CardId=1,PeriodId=2,Amount=900m,Currency="ARS"},new(){CardId=1,Amount=800m,Currency="ARS"},new(){CardId=2,PeriodId=3,Amount=50m,Currency="ARS"}],new());
    var compact=TextReport.Render(compactSnapshot,new(2026,11,11,10,38,0));
    Check(compact.Contains("Actualizado: 11/11/2026 - 10:38")&&compact.Contains("PESOS:       $ 100,00")&&compact.Contains("PESOS:       $ 150,00")&&compact.Contains("DOLARES:     US$ 20,00"),"TXT dinámico: reintegros netos, dos tarjetas y total general exacto");
    Check(!compact.Contains("HISTORICO")&&!compact.Contains("900,00")&&!compact.Contains("800,00")&&compact.Contains("Cierre:      "+today.AddDays(10).ToString("dd/MM/yyyy")),"TXT solo incluye períodos mostrados y sus fechas reales");
    var beforeText=txt;LocalConfig.SetExportFolder(Path.Combine(temp,"offline"));
    var keep=New(card,cat,1.11m,"ARS",new(2026,11,10));keep.Id=await storeTest.SaveMovementAsync(keep);
    Check(!await export.TryAsync(true)&&(await Find(keep.Id)).Amount==1.11m,"Destino no disponible no afecta guardado MySQL");
    Check(File.ReadAllText(report)==beforeText,"Exportación fallida conserva archivo anterior completo");
    LocalConfig.SetExportFolder(temp);Check(await export.TryAsync()&&Directory.GetFiles(temp,"*.tmp").Length==0,"Reintento pendiente y reemplazo atómico sin temporales");
    var newStore=new FinanceStore(profile);await newStore.InitializeAsync();Check((await newStore.ReadAsync()).Movements.Any(m=>m.Id==keep.Id),"Persistencia tras nuevas conexiones e inicialización");
    var visa=s.Cards.First(c=>c.Name=="VISA");visa.Name="VISA RENOMBRADA";await storeTest.SaveCardAsync(visa);await storeTest.InitializeAsync();Check((await storeTest.ReadAsync()).Cards.Count==3,"Semillas no se duplican después de renombrar");
    var category=s.Categories.First(c=>c.Id==cat);category.Active=false;await storeTest.SaveCategoryAsync(category);await Reject(()=>storeTest.SaveMovementAsync(New(card,cat,5,"ARS",DateTime.Today)),"Categoría inactiva conserva historial y evita nuevas cargas");
    await using var sql=new MySqlConnection(profile.ConnectionString);await sql.OpenAsync();Check(await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit_log")>0,"Auditoría de ediciones y eliminaciones");
    Check(await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM movements WHERE deleted_at IS NOT NULL")==1,"Eliminación lógica conserva registro histórico");
    Console.WriteLine($"RESULT: {results.Count} passed, 0 failed; isolated database {db}");
    var output=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/test-results.txt"));Directory.CreateDirectory(Path.GetDirectoryName(output)!);await File.WriteAllLinesAsync(output,results.Prepend(DateTime.Now.ToString("O")).Append($"{results.Count} passed; 0 failed; test database removed after run"));
}
finally
{
    LocalConfig.Folder=originalConfig;MySqlConnection.ClearAllPools();
    if(!db.StartsWith("personal_finances_tests_")||!db[24..].All(char.IsDigit))throw new Exception("Invalid test database cleanup target");
    await root.ExecuteAsync($"DROP DATABASE `{db}`");
    if(Path.GetFullPath(temp).StartsWith(Path.GetTempPath(),StringComparison.OrdinalIgnoreCase))Directory.Delete(temp,true);
}
