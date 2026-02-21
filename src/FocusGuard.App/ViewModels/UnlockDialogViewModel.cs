using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FocusGuard.Core.Sessions;

namespace FocusGuard.App.ViewModels;

public partial class UnlockDialogViewModel : ObservableObject
{
    private readonly IFocusSessionManager _sessionManager;

    [ObservableProperty]
    private string _generatedPassword = string.Empty;

    [ObservableProperty]
    private bool _isPasswordRevealed;

    [ObservableProperty]
    private string _typedPassword = string.Empty;

    [ObservableProperty]
    private bool _isCorrect;

    [ObservableProperty]
    private string _masterKeyInput = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isEmergencyExpanded;

    public bool UnlockSucceeded { get; private set; }
    public bool EmergencyUnlockUsed { get; private set; }

    public int PasswordLength => GeneratedPassword.Length;
    public int TypedLength => TypedPassword?.Length ?? 0;

    public UnlockDialogViewModel(IFocusSessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    partial void OnTypedPasswordChanged(string value)
    {
        IsCorrect = string.Equals(value, GeneratedPassword, StringComparison.Ordinal);
        OnPropertyChanged(nameof(TypedLength));
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void RevealPassword()
    {
        IsPasswordRevealed = true;
    }

    [RelayCommand]
    private async Task TryUnlock(Window window)
    {
        if (string.IsNullOrEmpty(TypedPassword))
        {
            ErrorMessage = "Please type the unlock password.";
            return;
        }

        var success = await _sessionManager.TryUnlockAsync(TypedPassword);
        if (success)
        {
            UnlockSucceeded = true;
            EmergencyUnlockUsed = false;
            window.DialogResult = true;
            window.Close();
        }
        else
        {
            ErrorMessage = "Incorrect password. Please try again.";
        }
    }

    [RelayCommand]
    private async Task TryEmergencyUnlock(Window window)
    {
        if (string.IsNullOrEmpty(MasterKeyInput))
        {
            ErrorMessage = "Please enter the master recovery key.";
            return;
        }

        var success = await _sessionManager.EmergencyUnlockAsync(MasterKeyInput.Trim());
        if (success)
        {
            UnlockSucceeded = true;
            EmergencyUnlockUsed = true;
            window.DialogResult = true;
            window.Close();
        }
        else
        {
            ErrorMessage = "Invalid master key. Please try again.";
        }
    }
}
