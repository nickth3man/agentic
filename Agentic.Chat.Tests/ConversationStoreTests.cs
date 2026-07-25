using Agentic.Chat.Data;
using Agentic.Chat.Models;
using Agentic.Chat.Services;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Agentic.Chat.Tests;

// Issue #13 — SQLite conversation store round-trip (incl. reasoning) and auto-titling.
// Temp-file SQLite; no network.
public class ConversationStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ChatDbContext _db;
    private readonly TestSupport.ProtectedJSRuntime _js;
    private readonly ConversationPersistence _persistence;
    private readonly ConversationService _conversations;
    private readonly ChatAgentService _chat;

    public ConversationStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "agentic-conv-" + Guid.NewGuid().ToString("N") + ".db");
        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseSqlite(ChatDatabase.ToConnectionString(_dbPath))
            .Options;
        _db = new ChatDbContext(options);
        _db.Database.EnsureCreated();

        _js = TestSupport.NewProtectedJSRuntime();
        var storage = new ProtectedLocalStorage(_js, new EphemeralDataProtectionProvider());
        var openRouter = Options.Create(new OpenRouterOptions
        {
            BaseUrl = "https://test.local/",
            Model = "test-model"
        });
        var selection = new SelectedModelService(storage);
        selection.SetCurrentModelIdForTest("test-model");

        _persistence = new ConversationPersistence(_db);
        var fake = new FakeOpenRouterClient(
            FakeOpenRouterClient.FakeResponse.Ok(
                new StreamDelta("Mass attracts mass.", "Consider Newton then Einstein.")));
        var catalog = new ModelCatalogService(new UnusedHttpClientFactory());
        catalog.SeedForTest(
        [
            new OpenRouterModel(
                "test-model",
                "test-model",
                128_000L,
                DateTimeOffset.UtcNow,
                "text->text",
                new OpenRouterPricing(0.0000025m, 0.00001m),
                ["tools", "reasoning"])
        ]);
        _chat = new ChatAgentService(
            fake,
            openRouter,
            NullLogger<ChatAgentService>.Instance,
            selection,
            catalog,
            new SystemPromptService(storage, NullLogger<SystemPromptService>.Instance),
            TestSupport.NewChatSettings(storage),
            _persistence);
        _conversations = new ConversationService(_db, _chat, _persistence, storage);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch { /* best-effort */ }
        try { File.Delete(_dbPath + "-shm"); } catch { /* best-effort */ }
        try { File.Delete(_dbPath + "-wal"); } catch { /* best-effort */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task RoundTrip_SaveConversation_Reload_IdenticalMessagesIncludingReasoning()
    {
        await _conversations.InitializeAsync();
        Assert.Null(_conversations.ActiveConversationId);

        await foreach (var _ in _chat.SendStreamingAsync("Explain gravity"))
        {
        }

        await _conversations.RefreshAfterTurnAsync();

        Assert.NotNull(_conversations.ActiveConversationId);
        Assert.Equal(
            ConversationTitle.FromFirstUserMessage("Explain gravity"),
            _conversations.Conversations[0].Title);

        var storage = new ProtectedLocalStorage(_js, new EphemeralDataProtectionProvider());
        var selection = new SelectedModelService(storage);
        selection.SetCurrentModelIdForTest("test-model");
        var persistence = new ConversationPersistence(_db);
        var freshChat = new ChatAgentService(
            new FakeOpenRouterClient(),
            Options.Create(new OpenRouterOptions { BaseUrl = "https://test.local/", Model = "test-model" }),
            NullLogger<ChatAgentService>.Instance,
            selection,
            new ModelCatalogService(new UnusedHttpClientFactory()),
            new SystemPromptService(storage, NullLogger<SystemPromptService>.Instance),
            TestSupport.NewChatSettings(storage),
            persistence);
        var freshConversations = new ConversationService(_db, freshChat, persistence, storage);
        await freshConversations.InitializeAsync();

        Assert.Equal(2, freshChat.Messages.Count);
        Assert.Equal("user", freshChat.Messages[0].Role);
        Assert.Equal("Explain gravity", freshChat.Messages[0].Content);
        Assert.Equal("assistant", freshChat.Messages[1].Role);
        Assert.Equal("Mass attracts mass.", freshChat.Messages[1].Content);
        Assert.Equal("Consider Newton then Einstein.", freshChat.Messages[1].Reasoning);
    }

    [Fact]
    public async Task RoundTrip_PersistsAssistantUsage()
    {
        var usageClient = new FakeOpenRouterClient(
            FakeOpenRouterClient.FakeResponse.Ok(
                new StreamDelta("Mass attracts mass.", "Consider Newton then Einstein."),
                new StreamDelta(null, null, new MessageUsage(1200, 340, 0.0041m))));
        var storage = new ProtectedLocalStorage(_js, new EphemeralDataProtectionProvider());
        var openRouter = Options.Create(new OpenRouterOptions
        {
            BaseUrl = "https://test.local/",
            Model = "test-model"
        });
        var selection = new SelectedModelService(storage);
        selection.SetCurrentModelIdForTest("test-model");
        var persistence = new ConversationPersistence(_db);
        var catalog = new ModelCatalogService(new UnusedHttpClientFactory());
        catalog.SeedForTest(
        [
            new OpenRouterModel(
                "test-model",
                "test-model",
                128_000L,
                DateTimeOffset.UtcNow,
                "text->text",
                new OpenRouterPricing(0.0000025m, 0.00001m),
                ["tools", "reasoning"])
        ]);
        var chat = new ChatAgentService(
            usageClient,
            openRouter,
            NullLogger<ChatAgentService>.Instance,
            selection,
            catalog,
            new SystemPromptService(storage, NullLogger<SystemPromptService>.Instance),
            persistence);
        var conversations = new ConversationService(_db, chat, persistence, storage);

        await conversations.InitializeAsync();
        await foreach (var _ in chat.SendStreamingAsync("Explain gravity"))
        {
        }

        await conversations.RefreshAfterTurnAsync();
        var conversationId = conversations.ActiveConversationId!.Value;

        var reloadPersistence = new ConversationPersistence(_db);
        var reloadChat = new ChatAgentService(
            new FakeOpenRouterClient(),
            openRouter,
            NullLogger<ChatAgentService>.Instance,
            selection,
            catalog,
            new SystemPromptService(storage, NullLogger<SystemPromptService>.Instance),
            reloadPersistence);
        var reloadConversations = new ConversationService(_db, reloadChat, reloadPersistence, storage);
        await reloadConversations.SwitchAsync(conversationId);

        var assistant = reloadChat.Messages.Single(m => m.Role == "assistant");
        Assert.NotNull(assistant.Usage);
        Assert.Equal(1200, assistant.Usage!.PromptTokens);
        Assert.Equal(340, assistant.Usage.CompletionTokens);
        Assert.Equal(0.0041m, assistant.Usage.Cost);
    }

    [Fact]
    public async Task RoundTrip_PersistsUserImage()
    {
        await _conversations.InitializeAsync();

        await foreach (var _ in _chat.SendStreamingAsync(
            "What is this?",
            "data:image/jpeg;base64,abc123"))
        {
        }

        await _conversations.RefreshAfterTurnAsync();
        var conversationId = _conversations.ActiveConversationId!.Value;

        var reloadPersistence = new ConversationPersistence(_db);
        var reloadChat = new ChatAgentService(
            new FakeOpenRouterClient(),
            Options.Create(new OpenRouterOptions { BaseUrl = "https://test.local/", Model = "test-model" }),
            NullLogger<ChatAgentService>.Instance,
            new SelectedModelService(new ProtectedLocalStorage(_js, new EphemeralDataProtectionProvider())),
            new ModelCatalogService(new UnusedHttpClientFactory()),
            new SystemPromptService(new ProtectedLocalStorage(_js, new EphemeralDataProtectionProvider()), NullLogger<SystemPromptService>.Instance),
            reloadPersistence);
        var reloadConversations = new ConversationService(
            _db,
            reloadChat,
            reloadPersistence,
            new ProtectedLocalStorage(_js, new EphemeralDataProtectionProvider()));
        await reloadConversations.SwitchAsync(conversationId);

        var user = reloadChat.Messages.Single(m => m.Role == "user");
        Assert.Equal("data:image/jpeg;base64,abc123", user.ImageDataUrl);
        var apiUser = reloadChat.ApiMessagesForTest.Single(m => m.Role == "user");
        Assert.False(apiUser.Content.IsText);
        Assert.Equal("data:image/jpeg;base64,abc123", apiUser.Content.Parts[1].ImageUrl!.Url);
    }

    [Fact]
    public void AutoTitle_ShortMessage_ReturnsTrimmedFullText()
    {
        Assert.Equal("Hello world", ConversationTitle.FromFirstUserMessage("  Hello world  "));
    }

    [Fact]
    public void AutoTitle_LongMessage_TruncatesToFortyCharsWithEllipsis()
    {
        var longText = new string('a', 50);
        var title = ConversationTitle.FromFirstUserMessage(longText);
        Assert.Equal(41, title.Length);
        Assert.Equal(new string('a', 40) + "…", title);
    }

    [Fact]
    public void AutoTitle_NullOrWhitespace_ReturnsDefault()
    {
        Assert.Equal(ConversationTitle.Default, ConversationTitle.FromFirstUserMessage(null));
        Assert.Equal(ConversationTitle.Default, ConversationTitle.FromFirstUserMessage("   "));
    }

    [Fact]
    public async Task SwitchRenameDelete_Works()
    {
        await _conversations.InitializeAsync();

        await foreach (var _ in _chat.SendStreamingAsync("first chat"))
        {
        }

        var firstId = _conversations.ActiveConversationId!.Value;
        await _conversations.RefreshAfterTurnAsync();

        await _conversations.NewChatAsync();
        await _persistence.OnUserMessageCommittedAsync("second chat", "test-model");
        await _persistence.OnAssistantFinalizedAsync("two", null);
        await _conversations.RefreshAfterTurnAsync();
        var secondId = _persistence.ActiveConversationId!.Value;

        await _conversations.SwitchAsync(firstId);
        Assert.Equal(firstId, _conversations.ActiveConversationId);
        Assert.Equal("first chat", _chat.Messages[0].Content);

        await _conversations.RenameAsync(firstId, "  Renamed  ");
        Assert.Contains(_conversations.Conversations, c => c.Id == firstId && c.Title == "Renamed");

        await _conversations.DeleteAsync(secondId);
        Assert.DoesNotContain(_conversations.Conversations, c => c.Id == secondId);

        await _conversations.DeleteAsync(firstId);
        Assert.Empty(_conversations.Conversations);
        Assert.Null(_conversations.ActiveConversationId);
        Assert.Empty(_chat.Messages);
    }

    [Fact]
    public void ConnectionString_IsLocalFileOnly()
    {
        var cs = ChatDatabase.ToConnectionString(_dbPath);
        Assert.False(ChatDatabase.ConnectionStringLooksCredentialed(cs));
        Assert.True(ChatDatabase.ConnectionStringLooksCredentialed("Data Source=x;Password=secret"));
        Assert.Equal(ChatDatabase.FileName, Path.GetFileName(ChatDatabase.GetDefaultFilePath()));
        Assert.Contains("App_Data", ChatDatabase.GetDbPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new InvalidOperationException("Catalog must not fetch in this test.");
    }
}
