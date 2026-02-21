namespace FocusGuard.Core.Sessions;

public class PomodoroIntervalCalculator
{
    /// <summary>
    /// Calculates the full sequence of Pomodoro intervals that fit within the given total duration.
    /// </summary>
    public List<PomodoroInterval> CalculateIntervals(PomodoroConfiguration config, int totalDurationMinutes)
    {
        var intervals = new List<PomodoroInterval>();
        var remainingMinutes = totalDurationMinutes;
        var sequence = 0;
        var workCount = 0;

        while (remainingMinutes > 0)
        {
            // Always start with a work interval
            var workDuration = Math.Min(config.WorkMinutes, remainingMinutes);
            intervals.Add(new PomodoroInterval
            {
                Type = FocusSessionState.Working,
                DurationMinutes = workDuration,
                SequenceNumber = sequence++
            });
            remainingMinutes -= workDuration;
            workCount++;

            if (remainingMinutes <= 0)
                break;

            // Determine break type
            var isLongBreak = workCount % config.LongBreakInterval == 0;
            var breakDuration = isLongBreak ? config.LongBreakMinutes : config.ShortBreakMinutes;
            breakDuration = Math.Min(breakDuration, remainingMinutes);

            intervals.Add(new PomodoroInterval
            {
                Type = isLongBreak ? FocusSessionState.LongBreak : FocusSessionState.ShortBreak,
                DurationMinutes = breakDuration,
                SequenceNumber = sequence++
            });
            remainingMinutes -= breakDuration;
        }

        return intervals;
    }

    /// <summary>
    /// Gets the next interval given the current state and completed work count.
    /// </summary>
    public PomodoroInterval GetNextInterval(PomodoroConfiguration config, FocusSessionState currentState, int completedWorkIntervals)
    {
        if (currentState == FocusSessionState.Working)
        {
            var newCompleted = completedWorkIntervals + 1;
            var isLongBreak = newCompleted % config.LongBreakInterval == 0;
            return new PomodoroInterval
            {
                Type = isLongBreak ? FocusSessionState.LongBreak : FocusSessionState.ShortBreak,
                DurationMinutes = isLongBreak ? config.LongBreakMinutes : config.ShortBreakMinutes,
                SequenceNumber = 0
            };
        }

        // After any break, go back to work
        return new PomodoroInterval
        {
            Type = FocusSessionState.Working,
            DurationMinutes = config.WorkMinutes,
            SequenceNumber = 0
        };
    }
}
