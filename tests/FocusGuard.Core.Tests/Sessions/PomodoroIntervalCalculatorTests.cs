using FocusGuard.Core.Sessions;
using Xunit;

namespace FocusGuard.Core.Tests.Sessions;

public class PomodoroIntervalCalculatorTests
{
    private readonly PomodoroIntervalCalculator _calculator = new();

    private static PomodoroConfiguration DefaultConfig => new()
    {
        WorkMinutes = 25,
        ShortBreakMinutes = 5,
        LongBreakMinutes = 15,
        LongBreakInterval = 4
    };

    [Fact]
    public void CalculateIntervals_SingleWorkInterval_WhenDurationEqualsWorkMinutes()
    {
        var intervals = _calculator.CalculateIntervals(DefaultConfig, 25);

        Assert.Single(intervals);
        Assert.Equal(FocusSessionState.Working, intervals[0].Type);
        Assert.Equal(25, intervals[0].DurationMinutes);
    }

    [Fact]
    public void CalculateIntervals_WorkAndShortBreak_WhenDurationFitsBoth()
    {
        var intervals = _calculator.CalculateIntervals(DefaultConfig, 30);

        Assert.Equal(2, intervals.Count);
        Assert.Equal(FocusSessionState.Working, intervals[0].Type);
        Assert.Equal(25, intervals[0].DurationMinutes);
        Assert.Equal(FocusSessionState.ShortBreak, intervals[1].Type);
        Assert.Equal(5, intervals[1].DurationMinutes);
    }

    [Fact]
    public void CalculateIntervals_LongBreakAfterFourWorkIntervals()
    {
        // 25+5 + 25+5 + 25+5 + 25+15 = 130 minutes total
        var intervals = _calculator.CalculateIntervals(DefaultConfig, 130);

        // 8 intervals: W S W S W S W LB
        Assert.Equal(8, intervals.Count);
        Assert.Equal(FocusSessionState.Working, intervals[0].Type);
        Assert.Equal(FocusSessionState.ShortBreak, intervals[1].Type);
        Assert.Equal(FocusSessionState.Working, intervals[2].Type);
        Assert.Equal(FocusSessionState.ShortBreak, intervals[3].Type);
        Assert.Equal(FocusSessionState.Working, intervals[4].Type);
        Assert.Equal(FocusSessionState.ShortBreak, intervals[5].Type);
        Assert.Equal(FocusSessionState.Working, intervals[6].Type);
        Assert.Equal(FocusSessionState.LongBreak, intervals[7].Type);
        Assert.Equal(15, intervals[7].DurationMinutes);
    }

    [Fact]
    public void CalculateIntervals_TruncatesLastInterval_WhenDurationDoesNotFitEvenly()
    {
        // 27 minutes: 25min work + 2min of short break (truncated from 5)
        var intervals = _calculator.CalculateIntervals(DefaultConfig, 27);

        Assert.Equal(2, intervals.Count);
        Assert.Equal(FocusSessionState.Working, intervals[0].Type);
        Assert.Equal(25, intervals[0].DurationMinutes);
        Assert.Equal(FocusSessionState.ShortBreak, intervals[1].Type);
        Assert.Equal(2, intervals[1].DurationMinutes);
    }

    [Fact]
    public void CalculateIntervals_ShortDuration_TruncatesFirstWork()
    {
        var intervals = _calculator.CalculateIntervals(DefaultConfig, 10);

        Assert.Single(intervals);
        Assert.Equal(FocusSessionState.Working, intervals[0].Type);
        Assert.Equal(10, intervals[0].DurationMinutes);
    }

    [Fact]
    public void CalculateIntervals_SequenceNumbersAreSequential()
    {
        var intervals = _calculator.CalculateIntervals(DefaultConfig, 60);

        for (int i = 0; i < intervals.Count; i++)
        {
            Assert.Equal(i, intervals[i].SequenceNumber);
        }
    }

    [Fact]
    public void CalculateIntervals_ZeroDuration_ReturnsEmpty()
    {
        var intervals = _calculator.CalculateIntervals(DefaultConfig, 0);
        Assert.Empty(intervals);
    }

    [Fact]
    public void GetNextInterval_FromWorking_ReturnsShortBreak()
    {
        var next = _calculator.GetNextInterval(DefaultConfig, FocusSessionState.Working, completedWorkIntervals: 0);

        Assert.Equal(FocusSessionState.ShortBreak, next.Type);
        Assert.Equal(5, next.DurationMinutes);
    }

    [Fact]
    public void GetNextInterval_FromWorking_AfterThreeCompleted_ReturnsLongBreak()
    {
        var next = _calculator.GetNextInterval(DefaultConfig, FocusSessionState.Working, completedWorkIntervals: 3);

        Assert.Equal(FocusSessionState.LongBreak, next.Type);
        Assert.Equal(15, next.DurationMinutes);
    }

    [Fact]
    public void GetNextInterval_FromShortBreak_ReturnsWorking()
    {
        var next = _calculator.GetNextInterval(DefaultConfig, FocusSessionState.ShortBreak, completedWorkIntervals: 1);

        Assert.Equal(FocusSessionState.Working, next.Type);
        Assert.Equal(25, next.DurationMinutes);
    }

    [Fact]
    public void GetNextInterval_FromLongBreak_ReturnsWorking()
    {
        var next = _calculator.GetNextInterval(DefaultConfig, FocusSessionState.LongBreak, completedWorkIntervals: 4);

        Assert.Equal(FocusSessionState.Working, next.Type);
        Assert.Equal(25, next.DurationMinutes);
    }

    [Fact]
    public void GetNextInterval_CustomConfig_UsesCustomDurations()
    {
        var config = new PomodoroConfiguration
        {
            WorkMinutes = 30,
            ShortBreakMinutes = 10,
            LongBreakMinutes = 20,
            LongBreakInterval = 2
        };

        // After 1 work interval (completedWorkIntervals=1), next should be long break since 2 % 2 == 0
        var next = _calculator.GetNextInterval(config, FocusSessionState.Working, completedWorkIntervals: 1);
        Assert.Equal(FocusSessionState.LongBreak, next.Type);
        Assert.Equal(20, next.DurationMinutes);
    }

    [Fact]
    public void CalculateIntervals_CustomConfig_RespectsSettings()
    {
        var config = new PomodoroConfiguration
        {
            WorkMinutes = 30,
            ShortBreakMinutes = 10,
            LongBreakMinutes = 20,
            LongBreakInterval = 2
        };

        // 30+10+30+20 = 90 minutes: W(30) SB(10) W(30) LB(20)
        var intervals = _calculator.CalculateIntervals(config, 90);

        Assert.Equal(4, intervals.Count);
        Assert.Equal(FocusSessionState.Working, intervals[0].Type);
        Assert.Equal(30, intervals[0].DurationMinutes);
        Assert.Equal(FocusSessionState.ShortBreak, intervals[1].Type);
        Assert.Equal(10, intervals[1].DurationMinutes);
        Assert.Equal(FocusSessionState.Working, intervals[2].Type);
        Assert.Equal(30, intervals[2].DurationMinutes);
        Assert.Equal(FocusSessionState.LongBreak, intervals[3].Type);
        Assert.Equal(20, intervals[3].DurationMinutes);
    }
}
