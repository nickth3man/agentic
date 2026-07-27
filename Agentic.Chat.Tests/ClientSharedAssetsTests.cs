namespace Agentic.Chat.Tests;

public sealed class ClientSharedAssetsTests
{
    [Fact]
    public void AppCss_MatchesServerVersion()
    {
        var root = FindRepoRoot();
        var serverCss = File.ReadAllText(
            Path.Combine(root, "Agentic.Chat", "wwwroot", "app.css"));
        var clientCss = File.ReadAllText(
            Path.Combine(root, "Agentic.Chat.Client", "wwwroot", "app.css"));

        Assert.Equal(serverCss, clientCss);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "agentic.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
