using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;

namespace PersonalFinances.App;
public sealed class PageVisibilityConverter : IValueConverter
{
    public object Convert(object value,Type t,object p,CultureInfo c)=>Equals(value,p)?Visibility.Visible:Visibility.Collapsed;
    public object ConvertBack(object v,Type t,object p,CultureInfo c)=>Binding.DoNothing;
}
public sealed class BoolVisibilityConverter : IValueConverter
{
    public object Convert(object value,Type t,object p,CultureInfo c)=>value is true?Visibility.Visible:Visibility.Collapsed;
    public object ConvertBack(object v,Type t,object p,CultureInfo c)=>Binding.DoNothing;
}
public sealed class Dialogs : IDialogs
{
    public bool Show(EditorViewModel vm)
    {
        var editor=new EditorWindow{DataContext=vm,Title=vm.Title,Owner=Application.Current.Windows.OfType<Window>().LastOrDefault(w=>w.IsActive)??Application.Current.MainWindow};
        if(vm is ImportEditor)editor.Width=1080;
        if(vm is ServicePaymentsEditor){editor.Width=820;editor.Height=580;}
        if(vm is ServiceEditor)editor.Height=660;
        if(vm is ServiceEntryEditor){editor.Width=570;editor.Height=640;}
        if(vm is DeleteServiceEditor){editor.Width=580;editor.Height=510;}
        if(vm is ServiceMonthEditor){editor.Width=490;editor.Height=420;}
        bool? pending=null;
        void RequestClose(bool result){if(vm.Busy)pending=result;else editor.DialogResult=result;}
        void OnChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e)
        {
            if(e.PropertyName==nameof(EditorViewModel.Busy)&&!vm.Busy&&pending.HasValue){var result=pending.Value;pending=null;editor.DialogResult=result;}
        }
        vm.CloseRequested+=RequestClose;vm.PropertyChanged+=OnChanged;
        try{return editor.ShowDialog()==true;}finally{vm.CloseRequested-=RequestClose;vm.PropertyChanged-=OnChanged;}
    }
    public bool Confirm(string text)=>MessageBox.Show(Application.Current.Windows.OfType<Window>().LastOrDefault(w=>w.IsActive)??Application.Current.MainWindow,text,"Personal Finances",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes;
    public string? PickText(){var d=new OpenFileDialog{Filter="Archivos de texto (*.txt)|*.txt|Todos los archivos|*.*",Title="Seleccionar TXT de consumos"};return d.ShowDialog()==true?d.FileName:null;}
    public string? PickFolder(){var d=new OpenFolderDialog{Title="Carpeta del resumen TXT"};return d.ShowDialog()==true?d.FolderName:null;}
    public string? SaveBackup(){var d=new SaveFileDialog{Title="Exportar todos los datos",Filter="Respaldo de Personal Finances (*.pfinance)|*.pfinance",FileName="PersonalFinances-"+DateTime.Now.ToString("yyyy-MM-dd-HHmm")+".pfinance",DefaultExt=".pfinance",AddExtension=true,OverwritePrompt=true};return d.ShowDialog()==true?d.FileName:null;}
    public string? PickBackup(){var d=new OpenFileDialog{Title="Importar respaldo completo",Filter="Respaldo de Personal Finances (*.pfinance)|*.pfinance",CheckFileExists=true};return d.ShowDialog()==true?d.FileName:null;}
    public void OpenFolder(string path)=>Process.Start(new ProcessStartInfo("explorer.exe",path){UseShellExecute=true});
}
