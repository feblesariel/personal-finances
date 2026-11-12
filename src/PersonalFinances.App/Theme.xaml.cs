using System.Windows;
using System.Windows.Controls;
namespace PersonalFinances.App;
public partial class Theme : ResourceDictionary
{
    public Theme()=>InitializeComponent();
    private void SimpleServiceDateError(object sender,DatePickerDateValidationErrorEventArgs e){e.ThrowException=false;if(sender is DatePicker {DataContext:SimpleServiceEditor vm})vm.DateValidation(false);}
    private void SimpleServiceDateFocus(object sender,RoutedEventArgs e){if(sender is DatePicker {DataContext:SimpleServiceEditor vm} picker)vm.DateValidation(string.IsNullOrWhiteSpace(picker.Text)||DateTime.TryParse(picker.Text,PersonalFinances.Core.Money.Ar,System.Globalization.DateTimeStyles.None,out _));}
    private void ConnectionPasswordChanged(object sender,RoutedEventArgs e){if(sender is PasswordBox {DataContext:ConnectionEditor vm} p)vm.Password=p.Password;}
    private void BackupPasswordChanged(object sender,RoutedEventArgs e)
    {
        if(sender is not PasswordBox box)return;
        if(box.DataContext is BackupExportEditor export){if(box.Tag?.ToString()=="Confirm")export.Confirmation=box.Password;else export.Password=box.Password;}
        if(box.DataContext is BackupRestoreEditor restore)restore.Password=box.Password;
    }
    private void ServiceDateError(object sender,DatePickerDateValidationErrorEventArgs e){e.ThrowException=false;if(sender is DatePicker {DataContext:ServiceEntryEditor vm} picker)vm.DateValidation(picker.Tag.ToString()!,false);}
    private void ServiceDateFocus(object sender,RoutedEventArgs e){if(sender is DatePicker {DataContext:ServiceEntryEditor vm} picker)vm.DateValidation(picker.Tag.ToString()!,string.IsNullOrWhiteSpace(picker.Text)||DateTime.TryParse(picker.Text,PersonalFinances.Core.Money.Ar,System.Globalization.DateTimeStyles.None,out _));}}
