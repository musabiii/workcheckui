namespace WorkCheck.Models;

public class AppSettings
{
    public int PomodoroMinutes { get; set; } = 25;
    public int Pomodoro2Minutes { get; set; } = 40;
    public int ShortBreakMinutes { get; set; } = 5;
    public int InactivityMinutes { get; set; } = 15;
    public string TelegramBotToken { get; set; } = "";
    public string TelegramChatId { get; set; } = "";
    public bool TelegramEnabled { get; set; }

    public TimeSpan PomodoroTime => TimeSpan.FromMinutes(PomodoroMinutes);
    public TimeSpan Pomodoro2Time => TimeSpan.FromMinutes(Pomodoro2Minutes);
    public TimeSpan ShortBreakTime => TimeSpan.FromMinutes(ShortBreakMinutes);
    public TimeSpan InactivityTime => TimeSpan.FromMinutes(InactivityMinutes);
}
