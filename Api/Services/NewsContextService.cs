using System.Net;
using AlfaHackaton.Result;
using AlfaHackaton.Result.Errors;
using ContextService.Contracts.Materials;
using ContextService.Contracts.News;
using ContextService.Services.Interfaces;

namespace ContextService.Services;

public class NewsContextService(IMaterialService materials)
{
    public async Task<Result<MaterialVersion>> ResolveAsync(NewsResolutionRequest request, CancellationToken ct)
    {
        if (request.AsOf == default)
        {
            return new(Invalid());
        }
        ct.ThrowIfCancellationRequested();
        var resolution = await materials.GetAsync(request.NewsMaterialId, request.NewsVersionId);
        if (resolution.Error is { } error)
        {
            return new(error);
        }
        var news = resolution.Content!;
        if (news.Kind != "news" || news.KnownAt > request.AsOf)
        {
            return new(new Error("The triggering news must be known at asOf", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.UnprocessableEntity });
        }
        if (news.CompanyIds.Length > 0)
        {
            return new(new Error("A global news job requires a public news material", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.UnprocessableEntity });
        }
        return new(news, null);
    }

    public async Task<Result<PublicNewsSourcesResponse>> PublicSourcesAsync(PublicNewsSourcesRequest request, CancellationToken ct)
    {
        if (request.AsOf == default || request.SourceRefs is null || request.SourceRefs.Length > 64
            || request.SourceRefs.Any(reference => reference is null))
        {
            return new(Invalid());
        }
        var result = new List<MaterialVersion>();
        foreach (var reference in request.SourceRefs)
        {
            ct.ThrowIfCancellationRequested();
            var resolution = await materials.GetAsync(reference.MaterialId, reference.VersionId);
            if (resolution.Error is { } error)
            {
                return new(error);
            }
            var source = resolution.Content!;
            if (source.Kind != "market" || source.CompanyIds.Length > 0
                || source.ContentHash != reference.ContentHash || source.KnownAt > request.AsOf)
            {
                return new(new Error("Public retrieval returned an unavailable source", ErrorSource.External, ErrorType.Undefined) { HttpStatusCode = HttpStatusCode.BadGateway });
            }
            result.Add(source);
        }
        return new(new PublicNewsSourcesResponse { Materials = result.ToArray() }, null);
    }

    private static Error Invalid()
    {
        return new("A bounded source scope and asOf are required", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest };
    }
}
