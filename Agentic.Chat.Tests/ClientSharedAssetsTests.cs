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

    [Theory]
    [InlineData("Pages", "Chat.razor.js", "chat.js")]
    [InlineData("", "ModelPicker.razor.js", "model-picker.js")]
    public void JavaScriptModule_MatchesServerVersion(
        string componentDirectory,
        string serverFile,
        string clientFile)
    {
        var root = FindRepoRoot();
        var serverModule = File.ReadAllText(
            Path.Combine(
                root,
                "Agentic.Chat",
                "Components",
                componentDirectory,
                serverFile));
        var clientModule = File.ReadAllText(
            Path.Combine(root, "Agentic.Chat.Client", "wwwroot", "js", clientFile));

        Assert.Equal(serverModule, clientModule);
    }

    [Fact]
    public void HomeRedirect_IsRelativeToTheConfiguredBasePath()
    {
        var root = FindRepoRoot();
        var homeComponent = File.ReadAllText(
            Path.Combine(
                root,
                "Agentic.Chat",
                "Components",
                "Pages",
                "Home.razor"));

        Assert.Contains("Navigation.NavigateTo(\"chat\", replace: true)", homeComponent);
        Assert.DoesNotContain("Navigation.NavigateTo(\"/chat\"", homeComponent);
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
