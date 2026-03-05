using System.Diagnostics;
using System.IO;
using Microsoft.Data.Sqlite;
using WorkCheck.Models;

namespace WorkCheck.Services;

public class DataService : IDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;

    public DataService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkCheck");

        Directory.CreateDirectory(appDataPath);

        var dbPath = Path.Combine(appDataPath, "statistics.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var createSessionsTable = """
                CREATE TABLE IF NOT EXISTS Sessions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    StartTime TEXT NOT NULL,
                    EndTime TEXT NOT NULL,
                    DurationTicks INTEGER NOT NULL,
                    IsWorkMode INTEGER NOT NULL DEFAULT 1,
                    Description TEXT
                )
                """;

            using var cmd = new SqliteCommand(createSessionsTable, connection);
            cmd.ExecuteNonQuery();

            var createAwayTable = """
                CREATE TABLE IF NOT EXISTS AwayPeriods (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    StartTime TEXT NOT NULL,
                    EndTime TEXT NOT NULL,
                    DurationTicks INTEGER NOT NULL,
                    IsWorkMode INTEGER NOT NULL DEFAULT 1
                )
                """;

            using var cmd2 = new SqliteCommand(createAwayTable, connection);
            cmd2.ExecuteNonQuery();

            var addSessionColumn = """
                ALTER TABLE Sessions ADD COLUMN IsWorkMode INTEGER NOT NULL DEFAULT 1
                """;

            using var cmd3 = new SqliteCommand(addSessionColumn, connection);
            try { cmd3.ExecuteNonQuery(); }
            catch { /* Column already exists */ }

            var addDescriptionColumn = """
                ALTER TABLE Sessions ADD COLUMN Description TEXT
                """;

            using var cmd5 = new SqliteCommand(addDescriptionColumn, connection);
            try { cmd5.ExecuteNonQuery(); }
            catch { /* Column already exists */ }

            var addAwayColumn = """
                ALTER TABLE AwayPeriods ADD COLUMN IsWorkMode INTEGER NOT NULL DEFAULT 1
                """;

            using var cmd4 = new SqliteCommand(addAwayColumn, connection);
            try { cmd4.ExecuteNonQuery(); }
            catch { /* Column already exists */ }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DataService] Ошибка инициализации БД: {ex.Message}");
        }
    }

    private SqliteConnection GetConnection()
    {
        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
        {
            _connection = new SqliteConnection(_connectionString);
            _connection.Open();
        }
        return _connection;
    }

    public void SaveSession(DateTime startTime, DateTime endTime, TimeSpan duration, bool isWorkMode, string description = "")
    {
        try
        {
            var connection = GetConnection();
            var insert = """
                INSERT INTO Sessions (StartTime, EndTime, DurationTicks, IsWorkMode, Description)
                VALUES (@StartTime, @EndTime, @DurationTicks, @IsWorkMode, @Description)
                """;

            using var cmd = new SqliteCommand(insert, connection);
            cmd.Parameters.AddWithValue("@StartTime", startTime.ToString("O"));
            cmd.Parameters.AddWithValue("@EndTime", endTime.ToString("O"));
            cmd.Parameters.AddWithValue("@DurationTicks", duration.Ticks);
            cmd.Parameters.AddWithValue("@IsWorkMode", isWorkMode ? 1 : 0);
            cmd.Parameters.AddWithValue("@Description", description);

            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DataService] Ошибка сохранения сессии: {ex.Message}");
        }
    }

    public List<Session> GetSessionsByDate(DateTime date)
    {
        var sessions = new List<Session>();

        try
        {
            var connection = GetConnection();
            var startDate = date.Date;
            var endDate = startDate.AddDays(1);

            var select = """
                SELECT Id, StartTime, EndTime, DurationTicks, IsWorkMode, Description
                FROM Sessions
                WHERE StartTime >= @StartDate AND StartTime < @EndDate
                ORDER BY StartTime DESC
                """;

            using var cmd = new SqliteCommand(select, connection);
            cmd.Parameters.AddWithValue("@StartDate", startDate.ToString("O"));
            cmd.Parameters.AddWithValue("@EndDate", endDate.ToString("O"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                sessions.Add(new Session
                {
                    Id = reader.GetInt32(0),
                    StartTime = DateTime.Parse(reader.GetString(1)),
                    EndTime = DateTime.Parse(reader.GetString(2)),
                    Duration = TimeSpan.FromTicks(reader.GetInt64(3)),
                    IsWorkMode = reader.GetInt32(4) == 1,
                    Description = reader.IsDBNull(5) ? string.Empty : reader.GetString(5)
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DataService] Ошибка получения сессий: {ex.Message}");
        }

        return sessions;
    }

    public TimeSpan GetTotalWorkTimeByDate(DateTime date, bool isWorkMode)
    {
        var sessions = GetSessionsByDate(date);
        return TimeSpan.FromTicks(sessions.Where(s => s.IsWorkMode == isWorkMode).Sum(s => s.Duration.Ticks));
    }

    public void SaveAwayPeriod(DateTime startTime, DateTime endTime, TimeSpan duration, bool isWorkMode)
    {
        try
        {
            var connection = GetConnection();
            var insert = """
                INSERT INTO AwayPeriods (StartTime, EndTime, DurationTicks, IsWorkMode)
                VALUES (@StartTime, @EndTime, @DurationTicks, @IsWorkMode)
                """;

            using var cmd = new SqliteCommand(insert, connection);
            cmd.Parameters.AddWithValue("@StartTime", startTime.ToString("O"));
            cmd.Parameters.AddWithValue("@EndTime", endTime.ToString("O"));
            cmd.Parameters.AddWithValue("@DurationTicks", duration.Ticks);
            cmd.Parameters.AddWithValue("@IsWorkMode", isWorkMode ? 1 : 0);

            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DataService] Ошибка сохранения периода неактивности: {ex.Message}");
        }
    }

    public TimeSpan GetTotalAwayTimeByDate(DateTime date, bool isWorkMode)
    {
        try
        {
            var connection = GetConnection();
            var startDate = date.Date;
            var endDate = startDate.AddDays(1);

            var select = """
                SELECT SUM(DurationTicks)
                FROM AwayPeriods
                WHERE StartTime >= @StartDate AND StartTime < @EndDate
                AND IsWorkMode = @IsWorkMode
                """;

            using var cmd = new SqliteCommand(select, connection);
            cmd.Parameters.AddWithValue("@StartDate", startDate.ToString("O"));
            cmd.Parameters.AddWithValue("@EndDate", endDate.ToString("O"));
            cmd.Parameters.AddWithValue("@IsWorkMode", isWorkMode ? 1 : 0);

            var result = cmd.ExecuteScalar();
            if (result == DBNull.Value || result == null)
                return TimeSpan.Zero;

            return TimeSpan.FromTicks(Convert.ToInt64(result));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DataService] Ошибка получения времени вне работы: {ex.Message}");
            return TimeSpan.Zero;
        }
    }

    public int GetSessionCountByDate(DateTime date, bool isWorkMode)
    {
        try
        {
            var connection = GetConnection();
            var startDate = date.Date;
            var endDate = startDate.AddDays(1);

            var count = """
                SELECT COUNT(*)
                FROM Sessions
                WHERE StartTime >= @StartDate AND StartTime < @EndDate
                AND IsWorkMode = @IsWorkMode
                """;

            using var cmd = new SqliteCommand(count, connection);
            cmd.Parameters.AddWithValue("@StartDate", startDate.ToString("O"));
            cmd.Parameters.AddWithValue("@EndDate", endDate.ToString("O"));
            cmd.Parameters.AddWithValue("@IsWorkMode", isWorkMode ? 1 : 0);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DataService] Ошибка подсчёта сессий: {ex.Message}");
            return 0;
        }
    }

    public void Dispose()
    {
        if (_connection != null)
        {
            _connection.Close();
            _connection.Dispose();
            _connection = null;
        }
    }
}
