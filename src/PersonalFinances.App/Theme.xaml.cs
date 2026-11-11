using System.Windows;
using System.Windows.Controls;
namespace PersonalFinances.App;
public partial class Theme : ResourceDictionary
{
    public Theme()=>InitializeComponent();
    private void ConnectionPasswordChanged(object sender,RoutedEventArgs e){if(sender is PasswordBox {DataContext:ConnectionEditor vm} p)vm.Password=p.Password;}
    private void BackupPasswordChanged(object sender,RoutedEventArgs e)
    {
        if(sender is not PasswordBox box)return;
        if(box.DataContext is BackupExportEditor export){if(box.Tag?.ToString()=="Confirm")export.Confirmation=box.Password;else export.Password=box.Password;}
        if(box.DataContext is BackupRestoreEditor restore)restore.Password=box.Password;
    }
}
