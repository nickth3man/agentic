using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agentic.Chat.Models.MultiAgent;

namespace Agentic.Chat.Services.MultiAgent;

public record SearchResultItem(
    string Title,
    string Snippet,
    string Url,
    string SourceEngine
);

public interface ISearchProvider
{
    Task<List<SearchResultItem>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
