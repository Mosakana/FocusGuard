namespace FocusGuard.App.Services;

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message);
    Task<string?> OpenFileAsync(string filter, string title = "Open File");
    Task<string?> SaveFileAsync(string filter, string defaultFileName = "", string title = "Save File");
}
