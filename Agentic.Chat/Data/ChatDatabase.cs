using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Agentic.Chat.Data;

// Resolves the on-disk SQLite path for conversation persistence (issue #13).
// Connection string is a local file path only — never credentials.
public static class ChatDatabase
{
    public const string FileName = "conversations.db";

    public const string DataFolderName = "App_Data";

    public static string GetDefaultFilePath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Agentic.Chat");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, FileName);
    }

    public static string GetDbPath(string contentRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        var dataDir = Path.Combine(contentRootPath, DataFolderName);
        Directory.CreateDirectory(dataDir);
        return Path.Combine(dataDir, FileName);
    }

    public static string GetConnectionString(string contentRootPath)
        => ToConnectionString(GetDbPath(contentRootPath));

    public static string ToConnectionString(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return new SqliteConnectionStringBuilder { DataSource = filePath }.ConnectionString;
    }

    public static void ConfigureSqlite(DbContextOptionsBuilder options, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        options.UseSqlite(ToConnectionString(filePath));
    }

    public static bool ConnectionStringLooksCredentialed(string connectionString)
    {
        ArgumentNullException.ThrowIfNull(connectionString);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return true;
        }

        // Require a SQLite Data Source= path. Anything else (Server=, Host=, etc.)
        // is treated as credentialed / non-local.
        if (!connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase)
            && !connectionString.Contains("DataSource=", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return connectionString.Contains("Password=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Pwd=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("User ID=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("User Id=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Uid=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Username=", StringComparison.OrdinalIgnoreCase);
    }
}
