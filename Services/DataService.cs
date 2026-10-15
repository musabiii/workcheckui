using System.Diagnostics;
using System.IO;
using Microsoft.Data.Sqlite;
using WorkCheck.Models;

namespace WorkCheck.Services;

public class DataService : IDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;

    public string DbPath { get; }

    public DataService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkCheck");

        Directory.CreateDirectory(appDataPath);

        DbPath = Path.Combine(appDataPath, "statistics.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
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
                    DurationMinutes INTEGER NOT NULL,
                    IsWorkMode INTEGER NOT NULL DEFAULT 1,
                    Description TEXT
                )
                """;

            var createAwayTable = """
                CREATE TABLE IF NOT EXISTS AwayPeriods (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    StartTime TEXT NOT NULL,
                    EndTime TEXT NOT NULL,
                    DurationMinutes INTEGER NOT NULL,
                    IsWorkMode INTEGER NOT NULL DEFAULT 1
                )
                """;

            var createProjectsTable = """
                CREATE TABLE IF NOT EXISTS Projects (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Rate REAL NOT NULL DEFAULT 0
                )
                """;

            foreach (var sql in new[] { createSessionsTable, createAwayTable, createProjectsTable })
            {
                using var cmd = new SqliteCommand(sql, connection);
                cmd.ExecuteNonQuery();
            }

            string[] addColumns =
            [
                "ALTER TABLE Sessions ADD COLUMN ProjectId INTEGER",
                "ALTER TABLE Sessions ADD COLUMN IsWorkMode INTEGER NOT NULL DEFAULT 1",
                "ALTER TABLE Sessions ADD COLUMN Description TEXT",
                "ALTER TABLE AwayPeriods ADD COLUMN IsWorkMode INTEGER NOT NULL DEFAULT 1"
            ];

            foreach (var sql in addColumns)
            {
                using var cmd = new SqliteCommand(sql, connection);
                try { cmd.ExecuteNonQuery(); }
                catch (SqliteException) { /* Column already exists */ }
            }
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

    public void SaveSession(DateTime startTime, DateTime endTime, TimeSpan duration, bool isWorkMode, int? projectId = null, string description = "")
    {
        try
        {
            var connection = GetConnection();
            var insert = """
                INSERT INTO Sessions (StartTime, EndTime, DurationMinutes, IsWorkMode, ProjectId, Description)
                VALUES (@StartTime, @EndTime, @DurationMinutes, @IsWorkMode, @ProjectId, @Description)
                """;

            using var cmd = new SqliteCommand(insert, connection);
            cmd.Parameters.AddWithValue("@StartTime", startTime.ToString("O"));
            cmd.Parameters.AddWithValue("@EndTime", endTime.ToString("O"));
            cmd.Parameters.AddWithValue("@DurationMinutes", (int)duration.TotalMinutes);
            cmd.Parameters.AddWithValue("@IsWorkMode", isWorkMode ? 1 : 0);
            cmd.Parameters.AddWithValue("@ProjectId", projectId.HasValue ? projectId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@Description", description);

            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DataService] Ошибка сохранения сессии: {ex.Message}");
        }
    }

    public List<Session> GetSessionsByDate(DateTime date, int? projectId = null) =>
        GetSessionsInRange(date.Date, date.Date.AddDays(1), projectId);

    public List<Session> GetSessionsInRange(DateTime startDate, DateTime endDate, int? projectId = null)
    {
        var sessions = new List<Session>();
        try
        {
            var connection = GetConnection();

            var sql = """
                SELECT Id, StartTime, EndTime, DurationMinutes, IsWorkMode, Description, ProjectId
                FROM Sessions
                WHERE StartTime >= @StartDate AND StartTime < @EndDate
                """;

            if (projectId.HasValue)
                sql += " AND ProjectId = @ProjectId";
            
            sql += " ORDER BY StartTime DESC";

            using var cmd = new SqliteCommand(sql, connection);
            cmd.Parameters.AddWithValue("@StartDate", startDate.ToString("O"));
            cmd.Parameters.AddWithValue("@EndDate", endDate.ToString("O"));
            
            if (projectId.HasValue)
                cmd.Parameters.AddWithValue("@ProjectId", projectId.Value);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                sessions.Add(ReadSession(reader));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DataService] Ошибка получения сессий: {ex.Message}");
        }

        return sessions;
    }

    public Session? GetLastSession()
    {
        try
        {
            var connection = GetConnection();
            var sql = """
                SELECT Id, StartTime, EndTime, DurationMinutes, IsWorkMode, Description, ProjectId
                FROM Sessions
                ORDER BY StartTime DESC
                LIMIT 1
                """;

            using var cmd = new SqliteCommand(sql, connection);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return ReadSession(reader);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DataService] Ошибка получения последней сессии: {ex.Message}");
        }
        return null;
    }

    private static Session ReadSession(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        StartTime = DateTime.Parse(reader.GetString(1)),
        EndTime = DateTime.Parse(reader.GetString(2)),
        Duration = TimeSpan.FromMinutes(reader.GetInt32(3)),
        IsWorkMode = reader.GetInt32(4) == 1,
        Description = reader.IsDBNull(5) ? "" : reader.GetString(5),
        ProjectId = reader.IsDBNull(6) ? null : reader.GetInt32(6)
    };

    public TimeSpan GetTotalWorkTimeByDate(DateTime date, bool isWorkMode, int? projectId = null)
    {
        var sessions = GetSessionsByDate(date, projectId);
        return TimeSpan.FromMinutes(sessions.Where(s => s.IsWorkMode == isWorkMode).Sum(s => s.Duration.TotalMinutes));
    }

    public void SaveAwayPeriod(DateTime startTime, DateTime endTime, TimeSpan duration, bool isWorkMode)
    {
        try
        {
            var connection = GetConnection();
            var insert = """
                INSERT INTO AwayPeriods (StartTime, EndTime, DurationMinutes, IsWorkMode)
                VALUES (@StartTime, @EndTime, @DurationMinutes, @IsWorkMode)
                """;

            using var cmd = new SqliteCommand(insert, connection);
            cmd.Parameters.AddWithValue("@StartTime", startTime.ToString("O"));
            cmd.Parameters.AddWithValue("@EndTime", endTime.ToString("O"));
            cmd.Parameters.AddWithValue("@DurationMinutes", (int)duration.TotalMinutes);
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
                SELECT SUM(DurationMinutes)
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

            return TimeSpan.FromMinutes(Convert.ToDouble(result));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DataService] Ошибка получения времени вне работы: {ex.Message}");
            return TimeSpan.Zero;
        }
    }

    public List<Project> GetAllProjects()
    {
        var projects = new List<Project>();
        try
        {
            var connection = GetConnection();
            var select = "SELECT Id, Name, Rate FROM Projects ORDER BY Name";

            using var cmd = new SqliteCommand(select, connection);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                projects.Add(new Project
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Rate = (decimal)reader.GetDouble(2)
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DataService] Ошибка получения проектов: {ex.Message}");
        }
        return projects;
    }

    public int AddProject(Project project)
    {
        try
        {
            var connection = GetConnection();
            var insert = """
                INSERT INTO Projects (Name, Rate)
                VALUES (@Name, @Rate)
                """;

            using var cmd = new SqliteCommand(insert, connection);
            cmd.Parameters.AddWithValue("@Name", project.Name);
            cmd.Parameters.AddWithValue("@Rate", (double)project.Rate);
            cmd.ExecuteNonQuery();

            using var cmdId = new SqliteCommand("SELECT last_insert_rowid()", connection);
            return Convert.ToInt32(cmdId.ExecuteScalar());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DataService] Ошибка добавления проекта: {ex.Message}");
            return 0;
        }
    }

    public void DeleteProject(int id)
    {
        try
        {
            var connection = GetConnection();
            var delete = "DELETE FROM Projects WHERE Id = @Id";

            using var cmd = new SqliteCommand(delete, connection);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DataService] Ошибка удаления проекта: {ex.Message}");
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
