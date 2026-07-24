using Agentic.Chat.Data;

namespace Agentic.Chat.Tests;

public class ChatDbContextPathTests
{
    [Fact]
    public void BuildSqliteConnectionString_IsLocalFileOnly_NoCredentials()
    {
        var cs = ChatDbContext.BuildSqliteConnectionString(@"C:\data\conversations.db");
        Assert.Equal(@"Data Source=C:\data\conversations.db", cs);
        Assert.DoesNotContain("Password", cs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("User Id", cs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Uid=", cs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetDefaultDatabasePath_CreatesDirectoryUnderLocalAppData()
    {
        var path = ChatDbContext.GetDefaultDatabasePath();
        Assert.EndsWith("conversations.db", path);
        Assert.Contains("Agentic.Chat", path);
        Assert.True(Directory.Exists(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void AppSettings_HasNoCredentialedConnectionStrings()
    {
        var root = FindRepoRoot();
        foreach (var name in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            var path = Path.Combine(root, "Agentic.Chat", name);
            if (!File.Exists(path))
            {
                continue;
            }

            var json = File.ReadAllText(path);
            Assert.DoesNotContain("Password=", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("User Id=", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("User ID=", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "agentic.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }
}
