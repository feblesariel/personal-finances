using System.Text.Json;
using PersonalFinances.Core;

internal static class ReportFormattingTests
{
    internal static void Run(Action<bool,string> check)
    {
        var month=new DateTime(2026,11,1);var now=new DateTime(2026,11,11,15,0,0);var longName="SERVICIO "+new string('X',80);
        var ledger=new ServiceLedger(
            [new(){Id=1,Name="VISA"},new(){Id=2,Name="MANTENIMIENTO DE CUENTA"},new(){Id=3,Name="GOOGLE ONE",Frequency="Anual",Currency="USD"},new(){Id=4,Name=longName},new(){Id=5,Name="PAGO CERO"}],
            [new(){Id=1,ServiceId=1,Period=month,AmountConfirmed=false,SheetRecorded=true,SheetPaid=2504268.44m,SheetDate=new(2026,11,10),SheetNotes="",SheetCurrency="ARS"},new(){Id=3,ServiceId=3,Period=new(2026,6,1),Amount=19.99m,Currency="USD"},new(){Id=5,ServiceId=5,Period=month,AmountConfirmed=false,SheetRecorded=true,SheetPaid=0,SheetDate=new(2026,11,10),SheetNotes="",SheetCurrency="ARS"}],
            [new(){Id=3,ObligationId=3,Amount=19.99m,Date=new(2026,6,2)}]){Months=[month,new(2026,6,1)]};
        var snapshot=new Snapshot([new(){Id=1,Name="VISA"}],[],[new(){Id=1,CardId=1,Name="Noviembre",Start=month,Closing=month.AddMonths(1).AddDays(-1),Due=month.AddMonths(1)}],
            [new(){Id=1,CardId=1,PeriodId=1,Amount=100,Currency="ARS"},new(){Id=2,CardId=1,PeriodId=1,Amount=20,Currency="USD"}],new()){Services=ledger};
        var before=JsonSerializer.Serialize(snapshot);var report=TextReport.Render(snapshot,now).ReplaceLineEndings("\n");var block=report[report.IndexOf("SERVICIOS -",StringComparison.Ordinal)..];
        var table=block.Split('\n').Where(line=>line.Contains(" | ")).ToList();
        string[] Cells(string name)=>table.Single(line=>line.Split('|')[0].TrimEnd()==name).Split('|').Select(cell=>cell.Trim()).ToArray();
        check(Cells("VISA").SequenceEqual(new[]{"VISA","$ 2.504.268,44","10/11/2026","PAGADO"}),"TXT: pago en ARS ocupa una sola fila con monto, fecha y estado");
        check(Cells("SERVICIO").SequenceEqual(new[]{"SERVICIO","MONTO ABONADO","FECHA PAGO","ESTADO"}),"TXT: encabezados claros para las cuatro columnas");
        var pipePositions=table[0].Select((c,i)=>(c,i)).Where(p=>p.c=='|').Select(p=>p.i).ToArray();
        check(table.All(line=>line.Select((c,i)=>(c,i)).Where(p=>p.c=='|').Select(p=>p.i).SequenceEqual(pipePositions)),"TXT: nombres largos y monedas distintas mantienen todas las columnas alineadas");
        check(Cells(longName)[0]==longName&&table.Count==ledger.Services.Count+1,"TXT: conserva nombres completos sin truncar y una fila por servicio");
        check(Cells("MANTENIMIENTO DE CUENTA").Skip(1).SequenceEqual(new[]{"—","—","PENDIENTE"}),"TXT: un pendiente sin monto no inventa un cero ni una fecha");
        check(Cells("GOOGLE ONE").Skip(1).SequenceEqual(new[]{"US$ 19,99","02/06/2026","PAGADO"}),"TXT: anual conserva USD y su fecha de pago original");
        check(Cells("PAGO CERO").Skip(1).SequenceEqual(new[]{"$ 0,00","10/11/2026","PAGADO"}),"TXT: un cero explícito pagado sí muestra su monto y fecha");
        var reverse=TextReport.Render(snapshot,now,"ServiceId:Descending");check(reverse.IndexOf("PAGO CERO",StringComparison.Ordinal)<reverse.IndexOf("MANTENIMIENTO DE CUENTA",StringComparison.Ordinal),"TXT: respeta el orden configurado de los servicios");
        var withoutServices=TextReport.Render(snapshot with{Services=ServiceLedger.Empty},now).ReplaceLineEndings("\n");
        check(report[..report.IndexOf("SERVICIOS -",StringComparison.Ordinal)]==withoutServices[..withoutServices.IndexOf("SERVICIOS -",StringComparison.Ordinal)]&&report.Contains("$ 100,00")&&report.Contains("US$ 20,00"),"TXT: resumen y totales de tarjetas permanecen independientes");
        check(!new[]{"VIGENTE","Exigible:","Saldo:","Historial","Vencimiento:"}.Any(block.Contains)&&JsonSerializer.Serialize(snapshot)==before,"TXT: no agrega complejidad ni modifica los datos originales");
    }
}
