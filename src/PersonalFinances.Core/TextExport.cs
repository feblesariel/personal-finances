using System.Text;

namespace PersonalFinances.Core;
public static class TextReport
{
    public static string Render(Snapshot s,DateTime now)
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
        var serviceRows=s.Services.Rows(now).Where(r=>r.Obligation.Period.Year==now.Year&&r.Obligation.Period.Month==now.Month).OrderBy(r=>r.Balance==0).ThenByDescending(r=>r.Overdue).ThenBy(r=>r.Obligation.Due??DateTime.MaxValue).ToList();
        b.AppendLine().AppendLine("====================================").AppendLine("SERVICIOS - "+now.ToString("MMMM yyyy",Money.Ar).ToUpper(Money.Ar)).AppendLine("====================================");
        foreach(var r in serviceRows)
        {
            b.AppendLine().AppendLine(r.Name+" · "+r.PeriodLabel).AppendLine("Vencimiento: "+r.DueLabel)
                .AppendLine("Total:       "+r.AmountLabel).AppendLine("Abonado:     "+r.PaidLabel).AppendLine("Pendiente:   "+r.BalanceLabel).AppendLine("Estado:      "+r.Status.ToUpper(Money.Ar));
            if(r.Obligation.Minimum.HasValue)b.AppendLine("Mínimo:      "+Money.Format(r.Obligation.Minimum.Value,r.Obligation.Currency));
            foreach(var p in r.Payments.Where(p=>!p.Voided))b.AppendLine($"Pago: {p.Date:dd/MM/yyyy} · {Money.Format(p.Amount,r.Obligation.Currency)} · {p.Method} · {p.Notes}");
            if(r.Obligation.Notes.Length>0)b.AppendLine(r.Obligation.Notes);
        }
        b.AppendLine().AppendLine("RESUMEN DE SERVICIOS")
            .AppendLine("Pendientes: "+serviceRows.Count(r=>r.BaseStatus=="Pendiente"))
            .AppendLine("Parciales:  "+serviceRows.Count(r=>r.BaseStatus=="Pago parcial"))
            .AppendLine("Pagados:    "+serviceRows.Count(r=>r.BaseStatus=="Pagado"))
            .AppendLine("Vencidos:   "+serviceRows.Count(r=>r.Overdue))
            .AppendLine("Pendiente ARS: "+Money.Format(serviceRows.Where(r=>r.Obligation.Currency=="ARS").Sum(r=>r.Balance)))
            .AppendLine("Pendiente USD: "+Money.Format(serviceRows.Where(r=>r.Obligation.Currency=="USD").Sum(r=>r.Balance),"USD"));
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
            if(!force&&preferences.ExportedRevision==s.Revision&&preferences.ExportedConnection==store.Profile.Identity&&File.Exists(path)&&File.GetLastWriteTime(path).Date==DateTime.Today){Status="Resumen actualizado";return true;}
            var folder=Path.GetDirectoryName(path);
            if(!Path.IsPathFullyQualified(path) || string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) throw new IOException("Destino no disponible");
            temp=Path.Combine(folder,".personal-finances-"+Guid.NewGuid().ToString("N")+".tmp");
            await File.WriteAllTextAsync(temp,TextReport.Render(s,DateTime.Now),new UTF8Encoding(false));
            if(File.Exists(path)) File.Replace(temp,path,null);else File.Move(temp,path);
            temp=null; await store.MarkExportedAsync(s.Revision);
            LocalConfig.UpdatePreferences(p=>p.ExportPath==path?p with{ExportedRevision=s.Revision,ExportedConnection=store.Profile.Identity}:p);
            Status="Resumen actualizado · "+DateTime.Now.ToString("HH:mm");return true;
        }
        catch(Exception e) { LocalConfig.Log(e); Status="Resumen pendiente · se reintentará automáticamente"; return false; }
        finally { if(temp!=null)try{File.Delete(temp);}catch{} gate.Release(); }
    }
}
