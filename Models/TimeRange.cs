namespace WorkCheck.Models;

public readonly record struct TimeRange(DateTime Start, DateTime End)
{
    public TimeSpan Duration => End - Start;
}
