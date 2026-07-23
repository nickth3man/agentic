using Markdig;

namespace Agentic.Chat.Services;

// Internal Markdown pipeline used to render assistant chat content for Blazor.
// The pipeline is built once with advanced extensions (tables, fenced code,
// task lists, auto-identifiers, etc.) and DisableHtml() so raw HTML in model
// output is escaped rather than passed through — model responses are untrusted
// input, and we don't want them able to inject executable markup into the DOM.
internal static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public static string Render(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return Markdown.ToHtml(markdown, Pipeline);
    }
}