using FocusGuard.App.Models;
using FocusGuard.Core.Statistics;

namespace FocusGuard.App.Services;

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message);
    Task<string?> OpenFileAsync(string filter, string title = "Open File");
    Task<string?> SaveFileAsync(string filter, string defaultFileName = "", string title = "Save File");
    Task<StartSessionDialogResult?> ShowStartSessionDialogAsync(Guid profileId, string profileName, string profileColor);
    Task<UnlockDialogResult?> ShowUnlockDialogAsync(string generatedPassword);
    Task<ScheduleSessionDialogResult?> ShowScheduleSessionDialogAsync(List<ProfileSummary> profiles, DateTime defaultDate);
    Task<FocusGoal?> ShowSetGoalDialogAsync();
}
