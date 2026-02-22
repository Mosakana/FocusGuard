using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FocusGuard.App.Models;
using FocusGuard.App.Services;
using FocusGuard.Core.Statistics;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace FocusGuard.App.ViewModels;

public enum StatsPeriod
{
    Day,
    Week,
    Month
}

public partial class StatisticsViewModel : ViewModelBase
{
    private readonly IStatisticsService _statisticsService;
    private readonly IGoalService _goalService;
    private readonly CsvExporter _csvExporter;
    private readonly IDialogService _dialogService;
    private readonly ILogger<StatisticsViewModel> _logger;

    [ObservableProperty]
    private StatsPeriod _selectedPeriod = StatsPeriod.Week;

    [ObservableProperty]
    private string _periodLabel = string.Empty;

    [ObservableProperty]
    private int _currentStreak;

    [ObservableProperty]
    private int _longestStreak;

    public ObservableCollection<StatsSummaryCard> SummaryCards { get; } = [];
    public ObservableCollection<ISeries> DailyFocusSeries { get; } = [];
    public ObservableCollection<ISeries> ProfilePieSeries { get; } = [];
    public ObservableCollection<HeatmapDay> HeatmapData { get; } = [];
    public ObservableCollection<GoalProgress> GoalProgressItems { get; } = [];

    public Axis[] XAxes { get; private set; } = [];
    public Axis[] YAxes { get; private set; } = [];

    private DateTime _periodStart;
    private DateTime _periodEnd;

    public StatisticsViewModel(
        IStatisticsService statisticsService,
        IGoalService goalService,
        CsvExporter csvExporter,
        IDialogService dialogService,
        ILogger<StatisticsViewModel> logger)
    {
        _statisticsService = statisticsService;
        _goalService = goalService;
        _csvExporter = csvExporter;
        _dialogService = dialogService;
        _logger = logger;

        SetPeriodRange(StatsPeriod.Week);
    }

    public override async void OnNavigatedTo()
    {
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task SelectPeriod(string period)
    {
        if (Enum.TryParse<StatsPeriod>(period, out var p))
        {
            SelectedPeriod = p;
            SetPeriodRange(p);
            await LoadDataAsync();
        }
    }

    [RelayCommand]
    private async Task PreviousPeriod()
    {
        ShiftPeriod(-1);
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task NextPeriod()
    {
        ShiftPeriod(1);
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task ExportCsv()
    {
        try
        {
            var path = await _dialogService.SaveFileAsync(
                "CSV Files (*.csv)|*.csv", "focusguard-export.csv", "Export Statistics");
            if (path is null) return;

            await _csvExporter.ExportSessionsAsync(path, _periodStart, _periodEnd);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export CSV");
        }
    }

    [RelayCommand]
    private async Task SetGoal()
    {
        try
        {
            var result = await _dialogService.ShowSetGoalDialogAsync();
            if (result is null) return;

            await _goalService.SetGoalAsync(result);
            await LoadGoalsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set goal");
        }
    }

    private async Task LoadDataAsync()
    {
        try
        {
            var stats = await _statisticsService.GetStatisticsAsync(_periodStart, _periodEnd);

            // Summary cards
            SummaryCards.Clear();
            var hours = stats.TotalFocusMinutes / 60;
            SummaryCards.Add(new StatsSummaryCard
            {
                Title = "Total Focus",
                Value = hours >= 1 ? $"{hours:F1}h" : $"{stats.TotalFocusMinutes:F0}m",
                Subtitle = $"{stats.TotalSessions} sessions",
                IconGlyph = "\u23F1"
            });
            SummaryCards.Add(new StatsSummaryCard
            {
                Title = "Sessions",
                Value = stats.TotalSessions.ToString(),
                Subtitle = $"{stats.TotalPomodoroCount} pomodoros",
                IconGlyph = "\u1F3AF"
            });
            SummaryCards.Add(new StatsSummaryCard
            {
                Title = "Blocked",
                Value = stats.TotalBlockedAttempts.ToString(),
                Subtitle = "distractions blocked",
                IconGlyph = "\u1F6E1"
            });
            SummaryCards.Add(new StatsSummaryCard
            {
                Title = "Streak",
                Value = $"{stats.Streak.CurrentStreak}d",
                Subtitle = $"Best: {stats.Streak.LongestStreak}d",
                IconGlyph = "\u1F525"
            });

            CurrentStreak = stats.Streak.CurrentStreak;
            LongestStreak = stats.Streak.LongestStreak;

            // Bar chart
            UpdateBarChart(stats.DailyBreakdown);

            // Pie chart
            UpdatePieChart(stats.ProfileBreakdown);

            // Heatmap
            await LoadHeatmapAsync();

            // Goals
            await LoadGoalsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load statistics");
        }
    }

    private void UpdateBarChart(List<DailyFocusSummary> daily)
    {
        var values = daily.Select(d => new DateTimePoint(d.Date, d.TotalFocusMinutes)).ToArray();

        DailyFocusSeries.Clear();
        DailyFocusSeries.Add(new ColumnSeries<DateTimePoint>
        {
            Values = values,
            Fill = new SolidColorPaint(SKColor.Parse("#4A90D9")),
            MaxBarWidth = 30,
            Padding = 2
        });

        XAxes =
        [
            new DateTimeAxis(TimeSpan.FromDays(1), date => date.ToString("MM/dd"))
            {
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#8890A0")),
                SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#3A3A50")) { StrokeThickness = 1 }
            }
        ];

        YAxes =
        [
            new Axis
            {
                Name = "Minutes",
                NamePaint = new SolidColorPaint(SKColor.Parse("#8890A0")),
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#8890A0")),
                SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#3A3A50")) { StrokeThickness = 1 },
                MinLimit = 0
            }
        ];

        OnPropertyChanged(nameof(XAxes));
        OnPropertyChanged(nameof(YAxes));
    }

    private void UpdatePieChart(List<ProfileFocusSummary> profiles)
    {
        ProfilePieSeries.Clear();

        foreach (var p in profiles)
        {
            ProfilePieSeries.Add(new PieSeries<double>
            {
                Values = [p.TotalFocusMinutes],
                Name = p.ProfileName,
                Fill = new SolidColorPaint(SKColor.Parse(p.ProfileColor))
            });
        }
    }

    private async Task LoadHeatmapAsync()
    {
        try
        {
            var data = await _statisticsService.GetHeatmapDataAsync(91);
            HeatmapData.Clear();

            foreach (var d in data)
            {
                var intensity = HeatmapDay.CalculateIntensity(d.TotalFocusMinutes);
                HeatmapData.Add(new HeatmapDay
                {
                    Date = d.Date,
                    FocusMinutes = d.TotalFocusMinutes,
                    IntensityLevel = intensity,
                    Color = HeatmapDay.IntensityToColor(intensity),
                    ToolTipText = $"{d.Date:MMM dd}: {d.TotalFocusMinutes:F0}m focus"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load heatmap data");
        }
    }

    private async Task LoadGoalsAsync()
    {
        try
        {
            var progress = await _goalService.GetAllProgressAsync();
            GoalProgressItems.Clear();
            foreach (var g in progress)
                GoalProgressItems.Add(g);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load goals");
        }
    }

    private void SetPeriodRange(StatsPeriod period)
    {
        var today = DateTime.UtcNow.Date;

        switch (period)
        {
            case StatsPeriod.Day:
                _periodStart = today;
                _periodEnd = today.AddDays(1);
                break;
            case StatsPeriod.Week:
                var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
                _periodStart = today.AddDays(-daysSinceMonday);
                _periodEnd = _periodStart.AddDays(7);
                break;
            case StatsPeriod.Month:
                _periodStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                _periodEnd = _periodStart.AddMonths(1);
                break;
        }

        UpdatePeriodLabel();
    }

    private void ShiftPeriod(int direction)
    {
        switch (SelectedPeriod)
        {
            case StatsPeriod.Day:
                _periodStart = _periodStart.AddDays(direction);
                _periodEnd = _periodEnd.AddDays(direction);
                break;
            case StatsPeriod.Week:
                _periodStart = _periodStart.AddDays(7 * direction);
                _periodEnd = _periodEnd.AddDays(7 * direction);
                break;
            case StatsPeriod.Month:
                _periodStart = _periodStart.AddMonths(direction);
                _periodEnd = _periodStart.AddMonths(1);
                break;
        }

        UpdatePeriodLabel();
    }

    private void UpdatePeriodLabel()
    {
        PeriodLabel = SelectedPeriod switch
        {
            StatsPeriod.Day => _periodStart.ToString("MMMM dd, yyyy"),
            StatsPeriod.Week => $"{_periodStart:MMM dd} — {_periodEnd.AddDays(-1):MMM dd, yyyy}",
            StatsPeriod.Month => _periodStart.ToString("MMMM yyyy"),
            _ => string.Empty
        };
    }
}
