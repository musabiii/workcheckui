namespace WorkCheck.Models;

public class Session
{
    public int Id { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTime Date => StartTime.Date;
    public bool IsWorkMode { get; set; }
}

public class AwayPeriod
{
    public int Id { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTime Date => StartTime.Date;
    public bool IsWorkMode { get; set; }
}
