using System.Text.Json;
using FocusGuard.Core.Data.Entities;
using FocusGuard.Core.Scheduling;
using Xunit;

namespace FocusGuard.Core.Tests.Scheduling;

public class OccurrenceExpanderTests
{
    private readonly OccurrenceExpander _expander = new();
    private readonly Guid _profileId = Guid.NewGuid();

    private ScheduledSessionEntity CreateSession(
        DateTime start, DateTime end,
        bool isRecurring = false,
        RecurrenceRule? rule = null,
        bool pomodoroEnabled = false)
    {
        return new ScheduledSessionEntity
        {
            Id = Guid.NewGuid(),
            ProfileId = _profileId,
            StartTime = start,
            EndTime = end,
            IsRecurring = isRecurring,
            RecurrenceRule = rule is not null ? JsonSerializer.Serialize(rule) : null,
            PomodoroEnabled = pomodoroEnabled
        };
    }

    // --- Non-recurring sessions ---

    [Fact]
    public void NonRecurring_InRange_ReturnsSingleOccurrence()
    {
        var start = new DateTime(2025, 6, 15, 9, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var session = CreateSession(start, end);

        var rangeStart = new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var rangeEnd = new DateTime(2025, 6, 16, 0, 0, 0, DateTimeKind.Utc);

        var result = _expander.Expand(session, rangeStart, rangeEnd);

        Assert.Single(result);
        Assert.Equal(start, result[0].StartTime);
        Assert.Equal(end, result[0].EndTime);
        Assert.Equal(_profileId, result[0].ProfileId);
        Assert.Equal(session.Id, result[0].ScheduledSessionId);
    }

    [Fact]
    public void NonRecurring_OutOfRange_ReturnsEmpty()
    {
        var start = new DateTime(2025, 6, 15, 9, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var session = CreateSession(start, end);

        var rangeStart = new DateTime(2025, 6, 16, 0, 0, 0, DateTimeKind.Utc);
        var rangeEnd = new DateTime(2025, 6, 17, 0, 0, 0, DateTimeKind.Utc);

        var result = _expander.Expand(session, rangeStart, rangeEnd);

        Assert.Empty(result);
    }

    [Fact]
    public void NonRecurring_PreservesPomodoro()
    {
        var start = new DateTime(2025, 6, 15, 9, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var session = CreateSession(start, end, pomodoroEnabled: true);

        var result = _expander.Expand(session, start.AddHours(-1), end.AddHours(1));

        Assert.Single(result);
        Assert.True(result[0].PomodoroEnabled);
    }

    [Fact]
    public void NonRecurring_DurationMinutes_Computed()
    {
        var start = new DateTime(2025, 6, 15, 9, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var session = CreateSession(start, end);

        var result = _expander.Expand(session, start.AddHours(-1), end.AddHours(1));

        Assert.Equal(90, result[0].DurationMinutes);
    }

    // --- Daily recurrence ---

    [Fact]
    public void Daily_Returns_OccurrenceEveryDay()
    {
        var start = new DateTime(2025, 6, 1, 9, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var rule = new RecurrenceRule { Type = RecurrenceType.Daily };
        var session = CreateSession(start, end, isRecurring: true, rule: rule);

        var rangeStart = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var rangeEnd = new DateTime(2025, 6, 4, 0, 0, 0, DateTimeKind.Utc);

        var result = _expander.Expand(session, rangeStart, rangeEnd);

        Assert.Equal(3, result.Count);
        Assert.Equal(new DateTime(2025, 6, 1, 9, 0, 0, DateTimeKind.Utc), result[0].StartTime);
        Assert.Equal(new DateTime(2025, 6, 2, 9, 0, 0, DateTimeKind.Utc), result[1].StartTime);
        Assert.Equal(new DateTime(2025, 6, 3, 9, 0, 0, DateTimeKind.Utc), result[2].StartTime);
    }

    [Fact]
    public void Daily_RangeStartsAfterSessionStart_SkipsEarlyDays()
    {
        var start = new DateTime(2025, 6, 1, 9, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var rule = new RecurrenceRule { Type = RecurrenceType.Daily };
        var session = CreateSession(start, end, isRecurring: true, rule: rule);

        var rangeStart = new DateTime(2025, 6, 5, 0, 0, 0, DateTimeKind.Utc);
        var rangeEnd = new DateTime(2025, 6, 7, 0, 0, 0, DateTimeKind.Utc);

        var result = _expander.Expand(session, rangeStart, rangeEnd);

        Assert.Equal(2, result.Count);
        Assert.Equal(new DateTime(2025, 6, 5, 9, 0, 0, DateTimeKind.Utc), result[0].StartTime);
        Assert.Equal(new DateTime(2025, 6, 6, 9, 0, 0, DateTimeKind.Utc), result[1].StartTime);
    }

    [Fact]
    public void Daily_WithEndDate_StopsAtEndDate()
    {
        var start = new DateTime(2025, 6, 1, 9, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var rule = new RecurrenceRule
        {
            Type = RecurrenceType.Daily,
            EndDate = new DateTime(2025, 6, 3, 0, 0, 0, DateTimeKind.Utc)
        };
        var session = CreateSession(start, end, isRecurring: true, rule: rule);

        var rangeStart = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var rangeEnd = new DateTime(2025, 6, 10, 0, 0, 0, DateTimeKind.Utc);

        var result = _expander.Expand(session, rangeStart, rangeEnd);

        Assert.Equal(3, result.Count); // June 1, 2, 3
    }

    // --- Weekdays recurrence ---

    [Fact]
    public void Weekdays_SkipsSaturdayAndSunday()
    {
        // June 2, 2025 is a Monday
        var start = new DateTime(2025, 6, 2, 9, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 6, 2, 10, 0, 0, DateTimeKind.Utc);
        var rule = new RecurrenceRule { Type = RecurrenceType.Weekdays };
        var session = CreateSession(start, end, isRecurring: true, rule: rule);

        // Mon-Sun range
        var rangeStart = new DateTime(2025, 6, 2, 0, 0, 0, DateTimeKind.Utc);
        var rangeEnd = new DateTime(2025, 6, 9, 0, 0, 0, DateTimeKind.Utc);

        var result = _expander.Expand(session, rangeStart, rangeEnd);

        Assert.Equal(5, result.Count); // Mon-Fri
        Assert.All(result, occ =>
        {
            Assert.NotEqual(DayOfWeek.Saturday, occ.StartTime.DayOfWeek);
            Assert.NotEqual(DayOfWeek.Sunday, occ.StartTime.DayOfWeek);
        });
    }

    // --- Weekly recurrence ---

    [Fact]
    public void Weekly_SameDayOfWeek()
    {
        // June 4, 2025 is a Wednesday
        var start = new DateTime(2025, 6, 4, 14, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 6, 4, 15, 0, 0, DateTimeKind.Utc);
        var rule = new RecurrenceRule { Type = RecurrenceType.Weekly, IntervalWeeks = 1 };
        var session = CreateSession(start, end, isRecurring: true, rule: rule);

        var rangeStart = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var rangeEnd = new DateTime(2025, 6, 22, 0, 0, 0, DateTimeKind.Utc);

        var result = _expander.Expand(session, rangeStart, rangeEnd);

        Assert.Equal(3, result.Count); // June 4, 11, 18
        Assert.All(result, occ => Assert.Equal(DayOfWeek.Wednesday, occ.StartTime.DayOfWeek));
    }

    [Fact]
    public void Weekly_BiWeekly_SkipsAlternateWeeks()
    {
        // June 4, 2025 is a Wednesday
        var start = new DateTime(2025, 6, 4, 14, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 6, 4, 15, 0, 0, DateTimeKind.Utc);
        var rule = new RecurrenceRule { Type = RecurrenceType.Weekly, IntervalWeeks = 2 };
        var session = CreateSession(start, end, isRecurring: true, rule: rule);

        var rangeStart = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var rangeEnd = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = _expander.Expand(session, rangeStart, rangeEnd);

        Assert.Equal(2, result.Count); // June 4 and June 18 (skip June 11 and 25)
        Assert.Equal(new DateTime(2025, 6, 4, 14, 0, 0, DateTimeKind.Utc), result[0].StartTime);
        Assert.Equal(new DateTime(2025, 6, 18, 14, 0, 0, DateTimeKind.Utc), result[1].StartTime);
    }

    // --- Custom recurrence ---

    [Fact]
    public void Custom_OnlySelectedDays()
    {
        var start = new DateTime(2025, 6, 2, 9, 0, 0, DateTimeKind.Utc); // Monday
        var end = new DateTime(2025, 6, 2, 10, 0, 0, DateTimeKind.Utc);
        var rule = new RecurrenceRule
        {
            Type = RecurrenceType.Custom,
            DaysOfWeek = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday]
        };
        var session = CreateSession(start, end, isRecurring: true, rule: rule);

        var rangeStart = new DateTime(2025, 6, 2, 0, 0, 0, DateTimeKind.Utc);
        var rangeEnd = new DateTime(2025, 6, 9, 0, 0, 0, DateTimeKind.Utc);

        var result = _expander.Expand(session, rangeStart, rangeEnd);

        Assert.Equal(3, result.Count); // Mon, Wed, Fri
        Assert.Equal(DayOfWeek.Monday, result[0].StartTime.DayOfWeek);
        Assert.Equal(DayOfWeek.Wednesday, result[1].StartTime.DayOfWeek);
        Assert.Equal(DayOfWeek.Friday, result[2].StartTime.DayOfWeek);
    }

    // --- Edge cases ---

    [Fact]
    public void Recurring_NullRecurrenceRule_TreatedAsNonRecurring()
    {
        var start = new DateTime(2025, 6, 15, 9, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var session = CreateSession(start, end, isRecurring: true, rule: null);

        var result = _expander.Expand(session, start.AddHours(-1), end.AddHours(1));

        Assert.Single(result);
    }

    [Fact]
    public void Recurring_EmptyRange_ReturnsEmpty()
    {
        var start = new DateTime(2025, 6, 1, 9, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var rule = new RecurrenceRule { Type = RecurrenceType.Daily };
        var session = CreateSession(start, end, isRecurring: true, rule: rule);

        // Range ends before it starts (empty range)
        var result = _expander.Expand(session,
            new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Empty(result);
    }

    [Fact]
    public void Daily_PreservesDuration()
    {
        var start = new DateTime(2025, 6, 1, 9, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 6, 1, 11, 30, 0, DateTimeKind.Utc); // 2.5 hours
        var rule = new RecurrenceRule { Type = RecurrenceType.Daily };
        var session = CreateSession(start, end, isRecurring: true, rule: rule);

        var rangeStart = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var rangeEnd = new DateTime(2025, 6, 3, 0, 0, 0, DateTimeKind.Utc);

        var result = _expander.Expand(session, rangeStart, rangeEnd);

        Assert.Equal(2, result.Count);
        Assert.All(result, occ =>
        {
            Assert.Equal(TimeSpan.FromHours(2.5), occ.EndTime - occ.StartTime);
            Assert.Equal(150, occ.DurationMinutes);
        });
    }

    [Fact]
    public void Daily_PreservesTimeOfDay()
    {
        var start = new DateTime(2025, 6, 1, 14, 30, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 6, 1, 15, 30, 0, DateTimeKind.Utc);
        var rule = new RecurrenceRule { Type = RecurrenceType.Daily };
        var session = CreateSession(start, end, isRecurring: true, rule: rule);

        var rangeStart = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var rangeEnd = new DateTime(2025, 6, 4, 0, 0, 0, DateTimeKind.Utc);

        var result = _expander.Expand(session, rangeStart, rangeEnd);

        Assert.All(result, occ =>
        {
            Assert.Equal(14, occ.StartTime.Hour);
            Assert.Equal(30, occ.StartTime.Minute);
        });
    }
}
