using System.Text.Json;
using FocusGuard.Core.Data.Entities;

namespace FocusGuard.Core.Scheduling;

public class OccurrenceExpander
{
    /// <summary>
    /// Expands a scheduled session into concrete occurrences within the given date range.
    /// For non-recurring sessions, returns a single occurrence if it falls within range.
    /// For recurring sessions, generates all occurrences matching the recurrence rule.
    /// </summary>
    public List<ScheduledOccurrence> Expand(ScheduledSessionEntity session, DateTime rangeStart, DateTime rangeEnd)
    {
        var occurrences = new List<ScheduledOccurrence>();

        if (!session.IsRecurring || string.IsNullOrEmpty(session.RecurrenceRule))
        {
            // Non-recurring: check if the session falls within range
            if (session.StartTime < rangeEnd && session.EndTime > rangeStart)
            {
                occurrences.Add(ToOccurrence(session, session.StartTime, session.EndTime));
            }
            return occurrences;
        }

        var rule = JsonSerializer.Deserialize<RecurrenceRule>(session.RecurrenceRule);
        if (rule is null)
            return occurrences;

        var duration = session.EndTime - session.StartTime;
        var timeOfDay = session.StartTime.TimeOfDay;

        // Determine the effective end date for recurrence
        var effectiveEnd = rule.EndDate.HasValue && rule.EndDate.Value < rangeEnd
            ? rule.EndDate.Value
            : rangeEnd;

        // Start from the session's original date or the range start, whichever is later
        var currentDate = session.StartTime.Date;
        if (currentDate < rangeStart.Date)
            currentDate = rangeStart.Date;

        while (currentDate < effectiveEnd.Date.AddDays(1))
        {
            if (ShouldOccurOn(rule, currentDate, session.StartTime.Date))
            {
                var occurrenceStart = currentDate + timeOfDay;
                var occurrenceEnd = occurrenceStart + duration;

                if (occurrenceStart < rangeEnd && occurrenceEnd > rangeStart)
                {
                    occurrences.Add(ToOccurrence(session, occurrenceStart, occurrenceEnd));
                }
            }

            currentDate = currentDate.AddDays(1);
        }

        return occurrences;
    }

    private static bool ShouldOccurOn(RecurrenceRule rule, DateTime date, DateTime originalDate)
    {
        switch (rule.Type)
        {
            case RecurrenceType.Daily:
                return true;

            case RecurrenceType.Weekdays:
                return date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

            case RecurrenceType.Weekly:
                if (rule.IntervalWeeks <= 1)
                    return date.DayOfWeek == originalDate.DayOfWeek;

                // Check if we're in the right week cycle
                var weeksDiff = (int)((date.Date - originalDate.Date).TotalDays / 7);
                return weeksDiff % rule.IntervalWeeks == 0 && date.DayOfWeek == originalDate.DayOfWeek;

            case RecurrenceType.Custom:
                return rule.DaysOfWeek.Contains(date.DayOfWeek);

            default:
                return false;
        }
    }

    private static ScheduledOccurrence ToOccurrence(ScheduledSessionEntity session, DateTime start, DateTime end)
    {
        return new ScheduledOccurrence
        {
            ScheduledSessionId = session.Id,
            ProfileId = session.ProfileId,
            StartTime = start,
            EndTime = end,
            PomodoroEnabled = session.PomodoroEnabled
        };
    }
}
