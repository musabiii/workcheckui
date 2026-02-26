namespace WorkCheck.Helpers;

public static class TimeFormatter
{
    /// <summary>
    /// Упрощённый формат для StatusWindow: "N мин" или "H ч M мин"
    /// </summary>
    public static string FormatShort(TimeSpan ts)
    {
        if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;

        var totalMinutes = (int)ts.TotalMinutes;
        if (totalMinutes < 60)
            return $"{totalMinutes} мин";

        int hours = (int)ts.TotalHours;
        int mins = ts.Minutes;
        return mins > 0 ? $"{hours} ч {mins} мин" : $"{hours} ч";
    }

    /// <summary>
    /// Человекочитаемый русский формат с правильными склонениями.
    /// </summary>
    public static string FormatHuman(TimeSpan ts)
    {
        if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;

        if (ts.TotalSeconds < 60)
        {
            int sec = (int)ts.TotalSeconds;
            return $"{sec} {Decline(sec, "секунда", "секунды", "секунд")}";
        }

        if (ts.TotalMinutes < 60)
        {
            int min = (int)ts.TotalMinutes;
            return $"{min} {Decline(min, "минута", "минуты", "минут")}";
        }

        int hours = (int)ts.TotalHours;
        int mins = ts.Minutes;
        var result = $"{hours} {Decline(hours, "час", "часа", "часов")}";
        if (mins > 0)
            result += $" {mins} {Decline(mins, "минута", "минуты", "минут")}";
        return result;
    }

    private static string Decline(int n, string one, string few, string many)
    {
        var abs = Math.Abs(n) % 100;
        if (abs is >= 11 and <= 19) return many;
        return (abs % 10) switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many
        };
    }
}
