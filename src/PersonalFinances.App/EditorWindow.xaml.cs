using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PersonalFinances.App;
public partial class EditorWindow : Window
{
    public EditorWindow(){InitializeComponent();Loaded+=(_,_)=>{MaxHeight=SystemParameters.WorkArea.Height-32;MaxWidth=SystemParameters.WorkArea.Width-32;Height=Math.Min(Height,MaxHeight);Width=Math.Min(Width,MaxWidth);FocusInput();if(DataContext is EditorViewModel vm)vm.PropertyChanged+=VmChanged;};PreviewKeyDown+=Key;Closing+=ClosingEditor;}
    private void VmChanged(object? sender,PropertyChangedEventArgs e){if(e.PropertyName==nameof(EditorViewModel.Busy)&&sender is EditorViewModel {Busy:false})Dispatcher.BeginInvoke(FocusInput);}
    private void ClosingEditor(object? s,CancelEventArgs e){if(DataContext is EditorViewModel {Busy:true})e.Cancel=true;else if(DataContext is EditorViewModel vm)vm.PropertyChanged-=VmChanged;}
    private void Key(object s,KeyEventArgs e){if(e.Key==System.Windows.Input.Key.Escape&&DataContext is EditorViewModel vm){vm.CancelCommand.Execute(null);e.Handled=true;}}
    private void FocusInput(){var box=DataContext is SimpleServiceEditor {Existing:true}?FindPaidInput(EditorContent):Find<TextBox>(EditorContent);if(box!=null){box.Focus();box.SelectAll();}else Find<PasswordBox>(EditorContent)?.Focus();}
    private static TextBox? FindPaidInput(DependencyObject root){for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var c=VisualTreeHelper.GetChild(root,i);if(c is TextBox box&&System.Windows.Automation.AutomationProperties.GetAutomationId(box)=="SimpleServicePaid")return box;var found=FindPaidInput(c);if(found!=null)return found;}return null;}
    private static T? Find<T>(DependencyObject root) where T:DependencyObject {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var c=VisualTreeHelper.GetChild(root,i);if(c is T t&&c is UIElement {IsVisible:true})return t;var found=Find<T>(c);if(found!=null)return found;}return null;}
}
