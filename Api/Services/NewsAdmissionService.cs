using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using AlfaHackaton.Result;
using AlfaHackaton.Result.Errors;
using ContextService.Common;
using ContextService.Contracts.Materials;
using ContextService.Contracts.News;
using ContextService.Services.Interfaces;

namespace ContextService.Services;

public class NewsAdmissionService(IMaterialService materials)
{
    public async Task<Result<NewsAdmissionResponse>> AdmitAsync(NewsAdmissionRequest request, CancellationToken ct)
    {
        if (request.Source is null || request.ExternalId is null || request.Title is null || request.Text is null)
        {
            return new(Invalid());
        }
        var source = NewsIdentityEncoding.Trim(request.Source);
        var externalId = NewsIdentityEncoding.Trim(request.ExternalId);
        var title = NewsIdentityEncoding.Trim(request.Title);
        if (source.Length is < 1 or > 200 || externalId.Length is < 1 or > 1000
            || title.Length is < 1 or > 1000 || request.Text.Length > 200000
            || request.CorrectsVersionId is { Length: < 1 or > 200 }
            || request.TextCompleteness is not ("full" or "lead" or "unknown")
            || !TryTimestamp(request.PublishedAt, out var publishedAt)
            || request.KnownAt is not null && !TryTimestamp(request.KnownAt, out _)
            || request.SourceUrl is not null && !Uri.TryCreate(request.SourceUrl, UriKind.Absolute, out _))
        {
            return new(Invalid());
        }

        var materialId = "news-" + NewsIdentityEncoding.Hash(source, externalId)[..40];
        string?[] identity = [title, request.Text, request.PublishedAt, request.CorrectsVersionId];
        if (request.SourceUrl is not null)
        {
            identity = [.. identity, request.SourceUrl];
        }
        var versionId = "v-" + NewsIdentityEncoding.Hash(identity)[..40];

        ct.ThrowIfCancellationRequested();
        var resolution = await materials.GetAsync(materialId, versionId);
        if (resolution.Error is { HttpStatusCode: not HttpStatusCode.NotFound } readError)
        {
            return new(readError);
        }
        if (resolution.Error is not null)
        {
            var observedAt = request.KnownAt is not null
                ? DateTimeOffset.Parse(request.KnownAt, CultureInfo.InvariantCulture)
                : DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            ct.ThrowIfCancellationRequested();
            resolution = await materials.Save(new MaterialVersionRequest(
                materialId,
                versionId,
                source,
                "news",
                title,
                request.Text,
                observedAt,
                publishedAt,
                request.CorrectsVersionId,
                [],
                [],
                new MaterialProvenance("event_payload", request.SourceUrl, observedAt, request.TextCompleteness, Locator: externalId)));
            if (resolution.Error is { HttpStatusCode: HttpStatusCode.Conflict })
            {
                // Конкурирующий приём мог сохранить ту же версию с первым observedAt.
                ct.ThrowIfCancellationRequested();
                resolution = await materials.GetAsync(materialId, versionId);
            }
        }
        if (resolution.Error is { } saveError)
        {
            return new(saveError);
        }

        var saved = resolution.Content!;
        if (saved.MaterialId != materialId || saved.VersionId != versionId || saved.SourceId != source
            || saved.Kind != "news" || saved.CompanyIds.Length != 0 || saved.BankProfileIds.Length != 0
            || saved.Title != title || saved.Text != request.Text || saved.PublishedAt != publishedAt
            || saved.CorrectsVersionId != request.CorrectsVersionId)
        {
            return new(new Error("Stored material does not match the admitted publication", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.Conflict });
        }
        return new(new NewsAdmissionResponse
        {
            NewsMaterialId = materialId,
            NewsVersionId = versionId,
            AsOf = saved.KnownAt,
            TextCompleteness = saved.Provenance?.TextCompleteness is "excerpt" or null
                ? "unknown"
                : saved.Provenance.TextCompleteness
        }, null);
    }

    private static bool TryTimestamp(string? value, out DateTimeOffset instant)
    {
        instant = default;
        return value is not null && Regex.IsMatch(value,
            @"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|[+-][0-9]{2}:[0-9]{2})$",
            RegexOptions.CultureInvariant)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out instant);
    }

    private static Error Invalid()
    {
        return new("A bounded publication with explicit ISO timestamps and full/lead/unknown completeness is required", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest };
    }
}
