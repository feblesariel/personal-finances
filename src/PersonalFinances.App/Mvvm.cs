using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PersonalFinances.Core;

namespace PersonalFinances.App;
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
    protected bool Set<T>(ref T field,T value,[CallerMemberName]string? name=null) { if(EqualityComparer<T>.Default.Equals(field,value))return false;field=value;Changed(name);return true; }
}
public sealed class AsyncCommand(Func<Task> action,Action<Exception>? error=null) : ICommand
{
    private bool running;public bool CanExecute(object? p)=>!running;public event EventHandler? CanExecuteChanged;
    public async void Execute(object? p) { if(running)return;running=true;CanExecuteChanged?.Invoke(this,EventArgs.Empty);try{await action();}catch(Exception e){LocalConfig.Log(e);error?.Invoke(e);}finally{running=false;CanExecuteChanged?.Invoke(this,EventArgs.Empty);} }
}
public sealed class RelayCommand(Action<object?> action) : ICommand
{
    public bool CanExecute(object? p)=>true;public event EventHandler? CanExecuteChanged {add{}remove{}}public void Execute(object? p)=>action(p);
}
public interface IDialogs { bool Show(EditorViewModel vm); bool Confirm(string text); string? PickText(); string? PickFolder(); void OpenFolder(string path); }
public abstract class EditorViewModel : Observable
{
    private string error="";private bool busy;
    public string Title {get;protected set;}="";public string Subtitle {get;protected set;}="";
    public string SaveLabel {get;protected set;}="Guardar · Enter";
    public string Error {get=>error;set=>Set(ref error,value);} public bool Busy {get=>busy;set=>Set(ref busy,value);}
    public event Action<bool>? CloseRequested; public ICommand SaveCommand {get;} public ICommand CancelCommand {get;}
    protected EditorViewModel(){SaveCommand=new AsyncCommand(async()=>{if(Busy)return;Busy=true;Error="";try{await Save();}finally{Busy=false;}},e=>Error=LocalConfig.Friendly(e));CancelCommand=new RelayCommand(_=>{if(!Busy)CloseRequested?.Invoke(false);});}
    protected void Close(bool result=true)=>CloseRequested?.Invoke(result);
    protected abstract Task Save();
}
