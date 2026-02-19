using System.Windows;
using Microsoft.Win32;

namespace FocusGuard.App.Services;

public class DialogService : IDialogService
{
    public Task<bool> ConfirmAsync(string title, string message)
    {
        var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    public Task<string?> OpenFileAsync(string filter, string title = "Open File")
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
            Title = title
        };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    public Task<string?> SaveFileAsync(string filter, string defaultFileName = "", string title = "Save File")
    {
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            Title = title,
            FileName = defaultFileName
        };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }
}
