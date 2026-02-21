using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FocusGuard.Core.Security;

namespace FocusGuard.App.ViewModels;

public partial class MasterKeySetupViewModel : ObservableObject
{
    private readonly MasterKeyService _masterKeyService;

    [ObservableProperty]
    private string _masterKey = string.Empty;

    [ObservableProperty]
    private bool _hasSavedKey;

    [ObservableProperty]
    private bool _isCopied;

    public MasterKeySetupViewModel(MasterKeyService masterKeyService)
    {
        _masterKeyService = masterKeyService;
    }

    public async Task GenerateKeyAsync()
    {
        MasterKey = await _masterKeyService.GenerateMasterKeyAsync();
    }

    [RelayCommand]
    private void CopyToClipboard()
    {
        Clipboard.SetText(MasterKey);
        IsCopied = true;
    }

    [RelayCommand]
    private void Continue(Window window)
    {
        window.DialogResult = true;
        window.Close();
    }
}
