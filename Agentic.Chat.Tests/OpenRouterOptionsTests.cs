using Agentic.Chat.Services;
using Microsoft.Extensions.Configuration;

namespace Agentic.Chat.Tests;

public class OpenRouterOptionsTests
{
    [Fact]
    public void Defaults_AreSane()
    {
        var options = new OpenRouterOptions();

        Assert.Equal("OpenRouter", OpenRouterOptions.SectionName);
        Assert.StartsWith("https://", options.BaseUrl);
        Assert.False(string.IsNullOrWhiteSpace(options.Model));
    }

    [Fact]
    public void AppSettings_ContainsBindableOpenRouterSection()
    {
        // Guards against config drift: Program.cs throws at startup if the
        // "OpenRouter" section is missing from appsettings.json.
        var config = new ConfigurationBuilder()
            .SetBasePath(FindRepoRoot())
            .AddJsonFile(Path.Combine("Agentic.Chat", "appsettings.json"), optional: false)
            .Build();

        var options = config.GetSection(OpenRouterOptions.SectionName).Get<OpenRouterOptions>();

        Assert.NotNull(options);
        Assert.StartsWith("https://", options.BaseUrl);
        Assert.False(string.IsNullOrWhiteSpace(options.Model));
        // The API key must stay out of config files (env var only).
        Assert.Null(config.GetSection(OpenRouterOptions.SectionName)["ApiKey"]);
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
