using System.Windows;
using System.Windows.Controls;
namespace PersonalFinances.App;
public partial class Theme : ResourceDictionary
{
    public Theme()=>InitializeComponent();
    private void ConnectionPasswordChanged(object sender,RoutedEventArgs e){if(sender is PasswordBox {DataContext:ConnectionEditor vm} p)vm.Password=p.Password;}
}
