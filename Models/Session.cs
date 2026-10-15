namespace WorkCheck.Models;

public class Session
{
    public int Id { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public bool IsWorkMode { get; set; }
    public string Description { get; set; } = string.Empty;
    public int? ProjectId { get; set; }
}
