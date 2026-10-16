namespace WorkCheck.Models;

// Stored as AwayPeriods.Kind
public enum AwayKind
{
    Away = 0,
    Break = 1
}

// ProjectId is the project current at the time; older rows have none
public record AwayPeriod(DateTime Start, DateTime End, bool IsWorkMode, int? ProjectId, AwayKind Kind);
