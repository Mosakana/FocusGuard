using CommunityToolkit.Mvvm.ComponentModel;

namespace FocusGuard.App.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    public virtual void OnNavigatedTo() { }
}
