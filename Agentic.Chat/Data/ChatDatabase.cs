using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Agentic.Chat.Data;

// Resolves the on-disk SQLite path for conversation persistence (issue #13).
// Connection string is a local file path only — never credentials.
public static class ChatDatabase
{
    public const string FileName = "conversations.db";

    public const string DataFolderName = "App_Data";

    // Columns added after the initial EnsureCreated schema. EnsureCreated never
    // alters an existing file, so startup must ADD COLUMN for any that are missing.
    // Full ALTER statements are fixed literals (EF1002 forbids interpolation into
    // ExecuteSqlRaw). Types match EF Core SQLite defaults (decimal → TEXT, bool → INTEGER).
    private static readonly (string Name, string AlterSql)[] MessageColumnUpgrades =
    [
        ("Reasoning", """ALTER TABLE "Messages" ADD COLUMN "Reasoning" TEXT NULL"""),
        ("ImageDataUrl", """ALTER TABLE "Messages" ADD COLUMN "ImageDataUrl" TEXT NULL"""),
        ("UsagePromptTokens", """ALTER TABLE "Messages" ADD COLUMN "UsagePromptTokens" INTEGER NULL"""),
        ("UsageCompletionTokens", """ALTER TABLE "Messages" ADD COLUMN "UsageCompletionTokens" INTEGER NULL"""),
        ("UsageCost", """ALTER TABLE "Messages" ADD COLUMN "UsageCost" TEXT NULL"""),
        ("UsageIsFree", """ALTER TABLE "Messages" ADD COLUMN "UsageIsFree" INTEGER NOT NULL DEFAULT 0"""),
    ];

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

    /// <summary>
    /// Creates the database when missing, then adds any Message columns that
    /// older on-disk files lack. Safe to call on every startup.
    /// </summary>
    public static async Task EnsureCreatedAndMigratedAsync(
        ChatDbContext db,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await EnsureMessageColumnsAsync(db, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task EnsureMessageColumnsAsync(
        ChatDbContext db,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var existing = await GetColumnNamesAsync(connection, "Messages", cancellationToken)
                .ConfigureAwait(false);
            if (existing.Count == 0)
            {
                // Table missing — EnsureCreated should have created it; nothing to alter.
                return;
            }

            foreach (var (name, alterSql) in MessageColumnUpgrades)
            {
                if (existing.Contains(name))
                {
                    continue;
                }

                await db.Database.ExecuteSqlRawAsync(alterSql, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            if (shouldClose)
            {
                await db.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<HashSet<string>> GetColumnNamesAsync(
        System.Data.Common.DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        // tableName is a fixed identifier from callers, not user input.
        command.CommandText = $"PRAGMA table_info(\"{tableName}\")";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // PRAGMA table_info columns: cid, name, type, notnull, dflt_value, pk
            names.Add(reader.GetString(1));
        }

        return names;
    }
}
