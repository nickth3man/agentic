using Agentic.Chat.Services;

namespace Agentic.Chat.Tests;

// Coverage for the assistant-message Markdown pipeline. The pipeline is built
// once (UseAdvancedExtensions + DisableHtml) and reused; model output is
// untrusted, so raw HTML must be escaped rather than passed through to the
// Blazor DOM. These tests pin that contract:
//   * Raw HTML (script, img-onerror, bare < / >) is escaped.
//   * Standard Markdown constructs render as expected HTML elements.
//   * Unterminated fenced code blocks — the mid-stream snapshot — render
//     without throwing and still produce a usable <pre><code> wrapper.
public class MarkdownRendererTests
{
    // ── raw HTML: model output is untrusted ────────────────────────────────

    [Fact]
    public void Render_ScriptTag_IsEscapedSoItCannotExecute()
    {
        var html = MarkdownRenderer.Render("Hello <script>alert(1)</script> world");

        // No raw <script> tag reaches the DOM.
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("</script", html);
        // And the literal characters are entity-encoded.
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;/script&gt;", html);
    }

    [Fact]
    public void Render_ImgTagWithOnerror_IsEscapedSoAttributeCannotRun()
    {
        var html = MarkdownRenderer.Render("<img src=x onerror=alert(1)>");

        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;img", html);
    }

    [Fact]
    public void Render_BareLtAndGt_AreEscapedToEntities()
    {
        var html = MarkdownRenderer.Render("a < b && b > c");

        Assert.Contains("&lt;", html);
        Assert.Contains("&gt;", html);
        // The raw angle brackets must not survive in output.
        Assert.DoesNotContain("a < b", html);
        Assert.DoesNotContain("b > c", html);
    }

    // ── standard Markdown ─────────────────────────────────────────────────

    [Fact]
    public void Render_BoldAndItalic_ProduceStrongAndEm()
    {
        var html = MarkdownRenderer.Render("**bold** and _italic_");

        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<em>italic</em>", html);
    }

    [Fact]
    public void Render_UnorderedList_ProducesUlWithLi()
    {
        var html = MarkdownRenderer.Render("- a\n- b\n");

        Assert.Contains("<ul>", html);
        Assert.Contains("<li>a</li>", html);
        Assert.Contains("<li>b</li>", html);
    }

    [Fact]
    public void Render_Heading_ProducesH1()
    {
        var html = MarkdownRenderer.Render("# Title");

        Assert.Contains("<h1", html);
        Assert.Contains("Title", html);
    }

    [Fact]
    public void Render_PipeTable_ProducesTableHeadAndBody()
    {
        var md = "| a | b |\n| - | - |\n| 1 | 2 |\n";

        var html = MarkdownRenderer.Render(md);

        Assert.Contains("<table>", html);
        Assert.Contains("<thead>", html);
        Assert.Contains("<tbody>", html);
        Assert.Contains("<th>a</th>", html);
        Assert.Contains("<td>1</td>", html);
        Assert.Contains("<td>2</td>", html);
    }

    [Fact]
    public void Render_FencedCodeBlockWithLanguage_ProducesPreCodeWithLanguageClass()
    {
        var md = "```csharp\nvar x = 1;\n```\n";

        var html = MarkdownRenderer.Render(md);

        Assert.Contains("<pre>", html);
        Assert.Contains("<code", html);
        Assert.Contains("language-csharp", html);
        Assert.Contains("var x = 1;", html);
    }

    // ── streaming tolerance ───────────────────────────────────────────────

    [Fact]
    public void Render_UnterminatedFencedCodeBlock_DoesNotThrow_RendersPreCode()
    {
        // Mid-stream snapshot — the closing fence hasn't arrived yet. The
        // render must not throw and must still produce a usable code wrapper
        // so the eventual JS highlighter / copy-code affordances have a stable
        // hook point.
        var md = "```csharp\nvar x = 1;\n";

        var html = MarkdownRenderer.Render(md);

        Assert.Contains("<pre>", html);
        Assert.Contains("<code", html);
        Assert.Contains("var x = 1;", html);
        Assert.Contains("language-csharp", html);
    }

    // ── edge cases ───────────────────────────────────────────────────────

    [Fact]
    public void Render_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MarkdownRenderer.Render(null!));
    }

    [Fact]
    public void Render_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, MarkdownRenderer.Render(string.Empty));
    }

    // ── link/image URL sanitization (XSS via Markdown link schemes) ─────────
    // DisableHtml() strips raw HTML but Markdig still generates <a href="...">
    // from [x](url). Dangerous schemes must be neutralized; safe schemes and
    // relative/fragment URLs must be preserved.

    [Theory]
    [InlineData("javascript")]
    [InlineData("vbscript")]
    [InlineData("data")]
    [InlineData("file")]
    public void Render_DangerousSchemeLink_IsNeutralized(string scheme)
    {
        var html = MarkdownRenderer.Render($"[click]({scheme}:alert(1))");

        Assert.DoesNotContain($"{scheme}:", html, StringComparison.OrdinalIgnoreCase);
        // The href value is blanked, not the whole link removed.
        Assert.Contains("href=\"\"", html);
        Assert.Contains("click", html);
    }

    [Fact]
    public void Render_DangerousSchemeImage_IsNeutralized()
    {
        var html = MarkdownRenderer.Render("![alt](data:text/html,basic)");

        Assert.DoesNotContain("data:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("src=\"\"", html);
    }

    [Fact]
    public void Render_JavascriptScheme_CaseAndSpaceInvariant()
    {
        // attackers mix case / wedge whitespace past naive filters
        var html = MarkdownRenderer.Render("[x](JaVaScRiPt:alert(1))");

        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_AllowedSchemeLinks_ArePreserved()
    {
        var md = "[a](https://e.com/x) [b](http://e.com) [c](mailto:x@y.com) [d](tel:+1555)";

        var html = MarkdownRenderer.Render(md);

        Assert.Contains("https://e.com/x", html);
        Assert.Contains("http://e.com", html);
        Assert.Contains("mailto:x@y.com", html);
        Assert.Contains("tel:+1555", html);
    }

    [Fact]
    public void Render_RelativeAndFragmentLinks_ArePreserved()
    {
        var md = "[top](#section) [home](/home) [rel](page.html)";

        var html = MarkdownRenderer.Render(md);

        Assert.Contains("\"#section\"", html);
        Assert.Contains("\"/home\"", html);
        Assert.Contains("\"page.html\"", html);
    }
}
