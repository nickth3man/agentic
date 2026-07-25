using Agentic.Chat.Data;

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
}
