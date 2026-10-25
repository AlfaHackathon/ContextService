using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using AlfaHackaton.Result;
using AlfaHackaton.Result.Errors;
using ContextService.Contracts.News;
using ContextService.Repositories.Interfaces;

namespace ContextService.Services;

public sealed partial class NewsSearchService(INewsSearchRepository repository)
{
    public async Task<Result<NewsSearchResponse>> Search(NewsSearchRequest request, CancellationToken ct)
    {
        if (request.Query is null || request.Query.Length > 2000 || string.IsNullOrWhiteSpace(request.Query)
            || request.Limit is < 1 or > 6 || !TryInstant(request.AsOf, out var asOf))
        {
            return new(new Error("query, explicit asOf and limit 1..6 are required.", ErrorSource.Client, ErrorType.CorruptedInfo)
            {
                HttpStatusCode = HttpStatusCode.BadRequest
            });
        }
        var hits = await repository.Search(request.Query.Trim(), asOf, request.Limit, ct);
        return new(new NewsSearchResponse(asOf, false, hits), null);
    }

    private static bool TryInstant(string? text, out DateTimeOffset instant)
    {
        instant = default;
        return text is { Length: <= 33 } && InstantPattern().IsMatch(text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out instant)
            && instant != default;
    }

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?(Z|[+-][0-9]{2}:[0-9]{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex InstantPattern();
}
