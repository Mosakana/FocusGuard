namespace FocusGuard.Core.Statistics;

public record ProfileFocusSummary(
    Guid ProfileId,
    string ProfileName,
    string ProfileColor,
    double TotalFocusMinutes,
    int SessionCount);
