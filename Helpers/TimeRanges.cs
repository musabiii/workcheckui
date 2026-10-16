using WorkCheck.Models;

namespace WorkCheck.Helpers;

public static class TimeRanges
{
    // Older versions saved the whole away total again on every mode switch, as one period ending at the
    // switch, so stored away periods overlap each other and real sessions. This keeps only time that is
    // plausibly away: overlaps merged, session time cut out, and each day limited to the span between its
    // first and last session, so a night left in work mode does not show up as hours away.
    public static List<TimeRange> CleanAway(IEnumerable<TimeRange> away, IReadOnlyList<Session> sessions,
        DateTime from, DateTime to, TimeSpan minLength)
    {
        var merged = Merge(away);
        var busy = Merge(sessions.Select(s => new TimeRange(s.StartTime, s.EndTime)));
        var result = new List<TimeRange>();

        for (var day = from.Date; day < to; day = day.AddDays(1))
        {
            var daySessions = sessions.Where(s => s.StartTime >= day && s.StartTime < day.AddDays(1)).ToList();
            if (daySessions.Count == 0) continue;

            var span = new TimeRange(daySessions.Min(s => s.StartTime), daySessions.Max(s => s.EndTime));
            foreach (var range in merged)
            {
                var clipped = Clip(range, span);
                if (clipped is not { } part) continue;

                foreach (var free in Subtract(part, busy))
                    if (free.Duration >= minLength)
                        result.Add(free);
            }
        }
        return result;
    }

    public static List<TimeRange> Merge(IEnumerable<TimeRange> ranges)
    {
        var result = new List<TimeRange>();
        foreach (var range in ranges.Where(r => r.End > r.Start).OrderBy(r => r.Start))
        {
            if (result.Count > 0 && range.Start <= result[^1].End)
            {
                if (range.End > result[^1].End)
                    result[^1] = result[^1] with { End = range.End };
            }
            else
            {
                result.Add(range);
            }
        }
        return result;
    }

    private static TimeRange? Clip(TimeRange range, TimeRange bounds)
    {
        var start = range.Start > bounds.Start ? range.Start : bounds.Start;
        var end = range.End < bounds.End ? range.End : bounds.End;
        return end > start ? new TimeRange(start, end) : null;
    }

    // busy must be merged and sorted
    private static IEnumerable<TimeRange> Subtract(TimeRange range, List<TimeRange> busy)
    {
        var cursor = range.Start;
        foreach (var b in busy)
        {
            if (b.End <= cursor) continue;
            if (b.Start >= range.End) break;
            if (b.Start > cursor) yield return new TimeRange(cursor, b.Start);
            if (b.End > cursor) cursor = b.End;
            if (cursor >= range.End) yield break;
        }
        if (cursor < range.End) yield return new TimeRange(cursor, range.End);
    }
}
