using Agentic.Chat.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Agentic.Chat.Tests;

public class ChatDatabaseTests
{
    [Fact]
    public void GetDbPath_UsesAppDataRelativeDirectory()
    {
        var path = ChatDatabase.GetDbPath(@"C:\app");
        Assert.EndsWith(Path.Combine("App_Data", "conversations.db"), path);
    }

    [Fact]
    public void GetConnectionString_IsLocalFileOnly_NoCredentials()
    {
        var cs = ChatDatabase.GetConnectionString(@"C:\tmp\agentic");
        Assert.StartsWith("Data Source=", cs);
        Assert.False(ChatDatabase.ConnectionStringLooksCredentialed(cs));
        Assert.Contains("conversations.db", cs);
    }

    [Fact]
    public void GetDbPath_ThrowsOnWhitespace()
    {
        Assert.Throws<ArgumentException>(() => ChatDatabase.GetDbPath("  "));
    }

    [Fact]
    public void ConnectionStringLooksCredentialed_DetectsPassword()
    {
        Assert.True(ChatDatabase.ConnectionStringLooksCredentialed("Data Source=x.db;Password=secret"));
        Assert.False(ChatDatabase.ConnectionStringLooksCredentialed("Data Source=x.db"));
    }

    [Fact]
    public async Task EnsureCreatedAndMigrated_AddsMissingImageDataUrl_OnLegacySchema()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "agentic-schema-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using (var raw = new SqliteConnection(ChatDatabase.ToConnectionString(dbPath)))
            {
                await raw.OpenAsync();
                await using var cmd = raw.CreateCommand();
                // Pre-ImageDataUrl Messages shape (mirrors an older EnsureCreated file).
                cmd.CommandText =
                    """
                    CREATE TABLE "Conversations" (
                        "Id" TEXT NOT NULL CONSTRAINT "PK_Conversations" PRIMARY KEY,
                        "Title" TEXT NOT NULL,
                        "Model" TEXT NOT NULL,
                        "CreatedAt" TEXT NOT NULL,
                        "UpdatedAt" TEXT NOT NULL
                    );
                    CREATE TABLE "Messages" (
                        "Id" TEXT NOT NULL CONSTRAINT "PK_Messages" PRIMARY KEY,
                        "ConversationId" TEXT NOT NULL,
                        "Role" TEXT NOT NULL,
                        "Content" TEXT NOT NULL,
                        "Reasoning" TEXT NULL,
                        "UsagePromptTokens" INTEGER NULL,
                        "UsageCompletionTokens" INTEGER NULL,
                        "UsageCost" TEXT NULL,
                        "UsageIsFree" INTEGER NOT NULL,
                        "CreatedAt" TEXT NOT NULL,
                        FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE CASCADE
                    );
                    """;
                await cmd.ExecuteNonQueryAsync();
            }

            var options = new DbContextOptionsBuilder<ChatDbContext>()
                .UseSqlite(ChatDatabase.ToConnectionString(dbPath))
                .Options;
            await using var db = new ChatDbContext(options);
            await ChatDatabase.EnsureCreatedAndMigratedAsync(db);

            var conversationId = Guid.NewGuid();
            db.Conversations.Add(new Conversation
            {
                Id = conversationId,
                Title = "test",
                Model = "test-model",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            db.Messages.Add(new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = conversationId,
                Role = "user",
                Content = "hi",
                ImageDataUrl = "data:image/png;base64,xx",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();

            var loaded = await db.Messages.SingleAsync(m => m.ConversationId == conversationId);
            Assert.Equal("data:image/png;base64,xx", loaded.ImageDataUrl);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* best-effort */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* best-effort */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task EnsureCreatedAndMigrated_IsIdempotent_OnCurrentSchema()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "agentic-schema-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var options = new DbContextOptionsBuilder<ChatDbContext>()
                .UseSqlite(ChatDatabase.ToConnectionString(dbPath))
                .Options;
            await using var db = new ChatDbContext(options);
            await ChatDatabase.EnsureCreatedAndMigratedAsync(db);
            await ChatDatabase.EnsureCreatedAndMigratedAsync(db);

            Assert.True(await db.Database.CanConnectAsync());
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* best-effort */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* best-effort */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void IsDuplicateColumnError_MatchesSqliteDuplicateColumnMessage()
    {
        var ex = new InvalidOperationException(
            "inner",
            new SqliteException("SQLite Error 1: 'duplicate column name: ImageDataUrl'.", 1));

        Assert.True(ChatDatabase.IsDuplicateColumnError(ex, "ImageDataUrl"));
        Assert.False(ChatDatabase.IsDuplicateColumnError(ex, "Reasoning"));
        Assert.False(ChatDatabase.IsDuplicateColumnError(
            new InvalidOperationException("other"), "ImageDataUrl"));
    }

    [Fact]
    public async Task EnsureCreatedAndMigrated_SwallowsDuplicateColumnRace()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "agentic-schema-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using (var raw = new SqliteConnection(ChatDatabase.ToConnectionString(dbPath)))
            {
                await raw.OpenAsync();
                await using var cmd = raw.CreateCommand();
                cmd.CommandText =
                    """
                    CREATE TABLE "Conversations" (
                        "Id" TEXT NOT NULL CONSTRAINT "PK_Conversations" PRIMARY KEY,
                        "Title" TEXT NOT NULL,
                        "Model" TEXT NOT NULL,
                        "CreatedAt" TEXT NOT NULL,
                        "UpdatedAt" TEXT NOT NULL
                    );
                    CREATE TABLE "Messages" (
                        "Id" TEXT NOT NULL CONSTRAINT "PK_Messages" PRIMARY KEY,
                        "ConversationId" TEXT NOT NULL,
                        "Role" TEXT NOT NULL,
                        "Content" TEXT NOT NULL,
                        "CreatedAt" TEXT NOT NULL,
                        FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE CASCADE
                    );
                    """;
                await cmd.ExecuteNonQueryAsync();
            }

            var options = new DbContextOptionsBuilder<ChatDbContext>()
                .UseSqlite(ChatDatabase.ToConnectionString(dbPath))
                .Options;

            // Two contexts racing the same ALTER path — loser may hit duplicate column.
            await Task.WhenAll(
                Task.Run(async () =>
                {
                    await using var db = new ChatDbContext(options);
                    await ChatDatabase.EnsureCreatedAndMigratedAsync(db);
                }),
                Task.Run(async () =>
                {
                    await using var db = new ChatDbContext(options);
                    await ChatDatabase.EnsureCreatedAndMigratedAsync(db);
                }));

            await using var verify = new ChatDbContext(options);
            await ChatDatabase.EnsureCreatedAndMigratedAsync(verify);
            Assert.True(await verify.Database.CanConnectAsync());
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* best-effort */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* best-effort */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* best-effort */ }
        }
    }
}
