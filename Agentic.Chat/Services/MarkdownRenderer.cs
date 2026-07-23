using System.Text.RegularExpressions;
using Markdig;

namespace Agentic.Chat.Services;

// Internal Markdown pipeline used to render assistant chat content for Blazor.
// The pipeline is built once with advanced extensions (tables, fenced code,
// task lists, auto-identifiers, etc.) and DisableHtml() so raw HTML in model
// output is escaped rather than passed through — model responses are untrusted
// input, and we don't want them able to inject executable markup into the DOM.
//
// DisableHtml() does NOT sanitize the URLs that Markdig *generates* from
// Markdown link/image syntax: `[x](javascript:alert(1))` would otherwise become
// a clickable `<a href="javascript:alert(1)">`. SanitizeUrls() runs after the
// parse and neutralizes any href/src whose scheme isn't on a safe allowlist
// (http/https/mailto/tel); relative + fragment URLs are kept.
internal static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    // Matches href="..." / src="..." attributes the way Markdig emits them
    // (double-quoted). Used to inspect every link/image destination.
    private static readonly Regex AttrUrlPattern = new(
        @"(?<attr>href|src)\s*=\s*""(?<val>[^""]*)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A URL "has a scheme" if it starts with a scheme like "https:" / "javascript:".
    // Anything not matching this (e.g. "/home", "#sec", "page", "//host") is
    // relative/protocol-relative and is kept — it can't carry an executable scheme.
    private static readonly Regex HasScheme = new(
        @"^\s*[a-zA-Z][a-zA-Z0-9+.\-]*\s*:",
        RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "mailto", "tel"
    };

    public static string Render(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var html = Markdown.ToHtml(markdown, Pipeline);
        return SanitizeUrls(html);
    }

    // Neutralize any href/src with a non-allowlisted scheme by blanking the
    // attribute value. An empty href/src renders as a no-op link / broken image
    // rather than an executable one. Allowlisted schemes and scheme-less
    // (relative) URLs are passed through untouched.
    private static string SanitizeUrls(string html) =>
        AttrUrlPattern.Replace(html, match =>
        {
            var val = match.Groups["val"].Value;
            var schemeMatch = HasScheme.Match(val);
            if (schemeMatch.Success)
            {
                var scheme = schemeMatch.Value.Trim().TrimEnd(':');
                if (!AllowedSchemes.Contains(scheme))
                {
                    return $"{match.Groups["attr"].Value}=\"\"";
                }
            }
            return match.Value;
        });
}
