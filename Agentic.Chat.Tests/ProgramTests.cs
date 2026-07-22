using System.Collections;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Agentic.Chat.Tests;

// Covers Program.cs top-level statements (the implicitly-generated Program.<Main>$).
// Each test constructs the actual host via WebApplicationFactory<Program> so the
// coverage engine records hits on the real Program.cs lines.
//
// Test responsibilities (mapped to Program.cs branches/lines):
//   - Host_InDevelopment_BuildsAndDefaultRouteRedirectsToChat
//       happy path: config-section present, api-key present, dev middleware branch
//       (skips HSTS / global exception handler), map endpoints, app.Run().
//   - Host_InProduction_RegistersHstsAndExceptionHandler
//       covers the !IsDevelopment() true branch (UseExceptionHandler, UseHsts).
//   - Host_ThrowsWhenOpenRouterSectionMissing
//       covers the `?? throw` on line 14 when Get<OpenRouterOptions>() returns null.
//   - Host_ThrowsWhenApiKeyMissing
//       covers the IsNullOrWhiteSpace(apiKey) true branch and the throw on lines 19-21.
public class ProgramTests
{
    // Synthetic API key used so the api-key guard is satisfied in tests that need to
    // reach later Program.cs lines. The real OPENROUTER_API_KEY never lives in files
    // (AGENTS.md hard rule); this string is not and never was a real key.
    private const string FakeApiKey = "test-only-fake-key-not-real-no-network";

    [Fact]
    public async Task Host_InDevelopment_BuildsAndDefaultRouteIsReachable()
    {
        using var apiKey = EnvVar.Set("OPENROUTER_API_KEY", FakeApiKey);
        using var factory = new AppFactory("Development");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/");

        // Reaching here means Program.cs fully constructed the dev host (config bound,
        // OpenRouter HttpClient registered, middleware wired, MapRazorComponents ran,
        // app.Run() handed control to TestServer) and the default route produced a
        // response. With Blazor Server interactive render mode (no prerender), Home.razor
        // does not throw a NavigationException server-side, so the App.razor shell comes
        // back as 200; with prerender enabled, NavigateTo("/chat") surfaces as 302.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK ||
            response.StatusCode == HttpStatusCode.Redirect ||
            response.StatusCode == HttpStatusCode.TemporaryRedirect,
            $"Expected a success-class response for '/', got {response.StatusCode}.");
    }

    [Fact]
    public void Host_InProduction_RegistersHstsAndExceptionHandler()
    {
        using var apiKey = EnvVar.Set("OPENROUTER_API_KEY", FakeApiKey);
        using var factory = new AppFactory("Production");
        using var client = factory.CreateClient();

        // Reaching this assertion means the host constructed without throwing, i.e.
        // the !IsDevelopment() branch body (UseExceptionHandler("/Error", ...),
        // UseHsts()) executed and the rest of Program.cs wired up successfully.
        Assert.NotNull(factory.Server);
    }

    [Fact]
    public void Host_ThrowsWhenOpenRouterSectionMissing()
    {
        using var apiKey = EnvVar.Set("OPENROUTER_API_KEY", FakeApiKey);
        // WebApplication.CreateBuilder's default environment-variable source binds
        // `OpenRouter__Key` to the OpenRouter:* config section, so any such var (CI,
        // dev shell, etc.) would populate the section and silently break the assertion.
        // Snapshot and clear them for the duration of the test.
        using var sectionEnvVars = EnvVarScope.ClearPrefix("OpenRouter__");
        using var factory = new SectionMissingFactory();

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("OpenRouter", ex.Message);
    }

    [Fact]
    public void Host_ThrowsWhenApiKeyMissing()
    {
        using var apiKey = EnvVar.Clear("OPENROUTER_API_KEY");
        using var factory = new AppFactory("Development");

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("OPENROUTER_API_KEY", ex.Message);
    }

    // ---------- helpers ----------

    private sealed class AppFactory : WebApplicationFactory<Program>
    {
        private readonly string _environment;

        public AppFactory(string environment) => _environment = environment;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(_environment);
            // Belt-and-suspenders: no real outbound HTTP can occur during Program.cs
            // host construction. The stub handler throws if anything ever calls it.
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient("OpenRouter")
                    .ConfigurePrimaryHttpMessageHandler(() => new NeverCallHandler());
            });
        }
    }

    /// <summary>
    /// Points the host at an empty content root so WebApplicationBuilder does not
    /// find an OpenRouter section in any appsettings*.json, exercising Program.cs's
    /// `?? throw new InvalidOperationException("Missing configuration section: OpenRouter")`.
    /// </summary>
    private sealed class SectionMissingFactory : WebApplicationFactory<Program>
    {
        private readonly string _emptyContentRoot;

        public SectionMissingFactory()
        {
            _emptyContentRoot = Path.Combine(
                Path.GetTempPath(),
                "AgenticChatEmpty_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_emptyContentRoot);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseContentRoot(_emptyContentRoot);
            // Defense in depth against inherited config sources. Program.cs's section-
            // missing throw fires before Build() so this ConfigureAppConfiguration
            // callback does not run for the assertion path, but clearing here means a
            // future refactor that delays the throw would still see an empty section.
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.Sources.Clear();
                cfg.AddInMemoryCollection();
            });
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient("OpenRouter")
                    .ConfigurePrimaryHttpMessageHandler(() => new NeverCallHandler());
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { Directory.Delete(_emptyContentRoot, recursive: true); } catch { }
            }
            base.Dispose(disposing);
        }
    }

    private sealed class NeverCallHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "Program.cs host-construction tests must not perform real OpenRouter HTTP calls.");
    }

    /// <summary>
    /// RAII env-var scope: restores the prior value (including unset) on Dispose.
    /// Process-wide state; tests in this class run sequentially (xUnit default).
    /// </summary>
    private readonly struct EnvVar : IDisposable
    {
        private readonly string _name;
        private readonly string? _original;

        private EnvVar(string name, string? original)
        {
            _name = name;
            _original = original;
        }

        public static EnvVar Set(string name, string value)
        {
            var original = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
            return new EnvVar(name, original);
        }

        public static EnvVar Clear(string name)
        {
            var original = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, null);
            return new EnvVar(name, original);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _original);
    }

    /// <summary>
    /// RAII scope for clearing every environment variable whose name starts with a
    /// given prefix (e.g. <c>OpenRouter__</c>), restoring each prior value (including
    /// unset) on Dispose. Used to neutralize config-source contamination from CI /
    /// dev shells that would otherwise bind into a config section under test.
    /// </summary>
    private sealed class EnvVarScope : IDisposable
    {
        private readonly Dictionary<string, string?> _snapshot = new();

        private EnvVarScope() { }

        public static EnvVarScope ClearPrefix(string prefix)
        {
            var scope = new EnvVarScope();
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                if (entry.Key is not string key) continue;
                if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                scope._snapshot[key] = Environment.GetEnvironmentVariable(key);
                Environment.SetEnvironmentVariable(key, null);
            }
            return scope;
        }

        public void Dispose()
        {
            foreach (var kv in _snapshot)
            {
                Environment.SetEnvironmentVariable(kv.Key, kv.Value);
            }
        }
    }
}
