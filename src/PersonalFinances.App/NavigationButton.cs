using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PersonalFinances.App;
public sealed class NavigationButton : Button
{
    public static readonly DependencyProperty IconProperty=DependencyProperty.Register(nameof(Icon),typeof(Geometry),typeof(NavigationButton));
    public static readonly DependencyProperty RouteProperty=DependencyProperty.Register(nameof(Route),typeof(string),typeof(NavigationButton),new PropertyMetadata("",Changed));
    public static readonly DependencyProperty CurrentPageProperty=DependencyProperty.Register(nameof(CurrentPage),typeof(string),typeof(NavigationButton),new PropertyMetadata("",Changed));
    private static readonly DependencyPropertyKey IsActiveKey=DependencyProperty.RegisterReadOnly(nameof(IsActive),typeof(bool),typeof(NavigationButton),new PropertyMetadata(false));
    public static readonly DependencyProperty IsActiveProperty=IsActiveKey.DependencyProperty;
    public Geometry? Icon {get=>(Geometry?)GetValue(IconProperty);set=>SetValue(IconProperty,value);}
    public string Route {get=>(string)GetValue(RouteProperty);set=>SetValue(RouteProperty,value);}
    public string CurrentPage {get=>(string)GetValue(CurrentPageProperty);set=>SetValue(CurrentPageProperty,value);}
    public bool IsActive=>(bool)GetValue(IsActiveProperty);
    private static void Changed(DependencyObject source,DependencyPropertyChangedEventArgs _){var button=(NavigationButton)source;button.SetValue(IsActiveKey,button.Route==button.CurrentPage);}
}
public static class InputAssist
{
    public static readonly DependencyProperty HintProperty=DependencyProperty.RegisterAttached("Hint",typeof(string),typeof(InputAssist),new PropertyMetadata(""));
    public static string GetHint(DependencyObject source)=>(string)source.GetValue(HintProperty);
    public static void SetHint(DependencyObject source,string value)=>source.SetValue(HintProperty,value);
}
