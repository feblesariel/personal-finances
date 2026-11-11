using System.Text;

namespace PersonalFinances.Core;
public static class TextReport
{
    public static string Render(Snapshot s,DateTime now,string serviceSort="ServiceId:Ascending")
    {
        var b=new StringBuilder(); b.AppendLine("PERSONAL FINANCES").AppendLine("Actualizado: "+now.ToString("dd/MM/yyyy - HH:mm",Money.Ar));
        var current=new List<Movement>();
        foreach(var card in s.Cards)
        {
            var period=s.Current(card.Id);
            b.AppendLine().AppendLine("====================================").AppendLine(card.Name+" - "+(period?.Name.ToUpper(Money.Ar)??"SIN PERÍODO CONFIGURADO")).AppendLine("====================================");
            if(period!=null)
            {
                b.AppendLine($"Periodo:     {period.Start:dd/MM/yyyy} al {period.Closing:dd/MM/yyyy}")
                    .AppendLine($"Cierre:      {period.Closing:dd/MM/yyyy}").AppendLine($"Vencimiento: {period.Due:dd/MM/yyyy}");
                var rows=s.Movements.Where(m=>m.CardId==card.Id&&m.PeriodId==period.Id).ToList();current.AddRange(rows);
                b.AppendLine();WriteTotals(b,rows);b.AppendLine($"Movimientos: {rows.Count}");
            }
            else {b.AppendLine("Periodo:     Sin configurar").AppendLine("Cierre:      Sin configurar").AppendLine("Vencimiento: Sin configurar").AppendLine();WriteTotals(b,[]);b.AppendLine("Movimientos: 0");}
        }
        b.AppendLine().AppendLine("====================================").AppendLine("TOTAL GENERAL").AppendLine("===================================="); WriteTotals(b,current);b.AppendLine("====================================");
        var serviceRows=ServiceSheet.Order(ServiceSheet.Rows(s.Services,now,now),serviceSort).ToList();
        b.AppendLine().AppendLine("====================================").AppendLine("SERVICIOS - "+now.ToString("MMMM yyyy",Money.Ar).ToUpper(Money.Ar)).AppendLine("====================================");
        foreach(var r in serviceRows)
        {
            b.AppendLine().AppendLine(r.Name+" · "+r.Status.ToUpper(Money.Ar));
            if(!r.Applicable)continue;
            b.AppendLine("Exigible: "+(r.Confirmed?Money.Format(r.Obligation!.Amount,r.Currency):"SIN CARGAR"))
                .AppendLine("Abonado:  "+Money.Format(r.Paid,r.Currency));
            if(r.Confirmed&&r.Balance>0)b.AppendLine("Saldo:    "+Money.Format(r.Balance,r.Currency));
            if(r.PaymentDate is {} date)b.AppendLine("Pago:     "+date.ToString("dd/MM/yyyy"));
            if(r.Indicator.Length>0)b.AppendLine(r.Indicator.ToUpper(Money.Ar));
            if(r.Obligation?.Notes is {Length:>0} notes)b.AppendLine(notes);
        }
        b.AppendLine().AppendLine($"Pagados: {serviceRows.Count(r=>r.Status=="Pagado")} · Parciales: {serviceRows.Count(r=>r.Status=="Parcial")} · Pendientes: {serviceRows.Count(r=>r.Status is "Pendiente" or "Sin cargar")}");
        return b.ToString();
    }
    private static void WriteTotals(StringBuilder b,List<Movement> rows)
    {
        b.AppendLine("PESOS:       "+Money.Format(rows.Where(m=>m.Currency=="ARS").Sum(m=>m.Signed)));
        b.AppendLine("DOLARES:     "+Money.Format(rows.Where(m=>m.Currency=="USD").Sum(m=>m.Signed),"USD"));
    }
}
public sealed class ExportService(FinanceStore store)
{
    private static readonly SemaphoreSlim gate=new(1,1);
    public string Status {get;private set;}="Resumen pendiente";
    public async Task<bool> TryAsync(bool force=false)
    {
        if(!await gate.WaitAsync(0))return false;
        string? temp=null;
        try
        {
            var s=await store.ReadAsync();var path=LocalConfig.ResolveExportPath(s.Settings.GetValueOrDefault("export_path"),true);
            if(string.IsNullOrWhiteSpace(path)){Status="Elegí una carpeta en Configuración → Exportación de resumen";return false;}
            var preferences=LocalConfig.Preferences();
            if(!force&&preferences.ExportedRevision==s.Revision&&preferences.ExportedConnection==store.Profile.Identity&&preferences.ExportedServiceSort==preferences.ServiceSort&&File.Exists(path)&&File.GetLastWriteTime(path).Date==DateTime.Today){Status="Resumen actualizado";return true;}
            var folder=Path.GetDirectoryName(path);
            if(!Path.IsPathFullyQualified(path) || string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) throw new IOException("Destino no disponible");
            temp=Path.Combine(folder,".personal-finances-"+Guid.NewGuid().ToString("N")+".tmp");
            await File.WriteAllTextAsync(temp,TextReport.Render(s,DateTime.Now,preferences.ServiceSort),new UTF8Encoding(false));
            if(File.Exists(path)) File.Replace(temp,path,null);else File.Move(temp,path);
            temp=null; await store.MarkExportedAsync(s.Revision);
            LocalConfig.UpdatePreferences(p=>p.ExportPath==path?p with{ExportedRevision=s.Revision,ExportedConnection=store.Profile.Identity,ExportedServiceSort=preferences.ServiceSort}:p);
            Status="Resumen actualizado · "+DateTime.Now.ToString("HH:mm");return true;
        }
        catch(Exception e) { LocalConfig.Log(e); Status="Resumen pendiente · se reintentará automáticamente"; return false; }
        finally { if(temp!=null)try{File.Delete(temp);}catch{} gate.Release(); }
    }
}
