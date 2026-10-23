using ContextService.Common;
using ContextService.Contracts.Materials;
using ContextService.Models;

namespace ContextService.Services;

public static class MaterialValidation
{
    public static MaterialVersionRequest Normalize(MaterialVersionRequest r)
    {
        IdentifierValidation.RequireId(r.MaterialId, "materialId");
        IdentifierValidation.RequireId(r.VersionId, "versionId");
        IdentifierValidation.RequireId(r.SourceId, "sourceId");
        if (r.Kind is not ("news" or "company" or "market"))
        {
            throw new ApiException(400, "invalid_kind", "kind must be news, company or market.");
        }

        var titleOnlyEvent = r.Kind == "news" && r.Provenance is { Acquisition: "event_payload", TextCompleteness: "lead" or "unknown" };
        if (string.IsNullOrWhiteSpace(r.Title) || r.Title.Length > 1000 || r.Text is null ||
            (!titleOnlyEvent && string.IsNullOrWhiteSpace(r.Text)) || r.Text.Length > 200000 || r.KnownAt == default)
        {
            throw new ApiException(400, "invalid_material", "title, bounded nonempty text and knownAt are required.");
        }

        if (r.CompanyIds is null || r.BankProfileIds is null || r.CompanyIds.Contains(Guid.Empty) ||
            r.CompanyIds.Length > 1000 || r.BankProfileIds.Length > 1000)
        {
            throw new ApiException(400, "invalid_scope", "Explicit bounded companyIds and bankProfileIds arrays are required.");
        }

        if (r.Kind == "company" && r.CompanyIds.Length == 0)
        {
            throw new ApiException(400, "invalid_scope", "Company material requires at least one company Guid.");
        }

        foreach (var id in r.BankProfileIds)
        {
            IdentifierValidation.RequireId(id, "bankProfileId");
        }

        if (r.Provenance is { } p)
        {
            if (p.Acquisition is not ("uploaded" or "event_payload" or "news_service") ||
                p.TextCompleteness is not ("full" or "lead" or "excerpt" or "unknown") ||
                p.RetrievedAt == default || p.RetrievedAt > r.KnownAt || p.Locator?.Length > 1000 ||
                (p.SourceUrl is not null && (!Uri.TryCreate(p.SourceUrl, UriKind.Absolute, out var url) ||
                    url.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(url.UserInfo))) ||
                (p.Acquisition == "news_service" && p.OriginVersionId != r.VersionId) ||
                (p.Acquisition != "news_service" && p.OriginVersionId is not null))
            {
                throw new ApiException(400, "invalid_provenance", "Explicit acquisition, completeness and actual retrieval time are required; news-service versions must retain their origin identity.");
            }
        }
        if (r.CorrectsVersionId is not null)
        {
            IdentifierValidation.RequireId(r.CorrectsVersionId, "correctsVersionId");
            if (r.CorrectsVersionId == r.VersionId)
            {
                throw new ApiException(400, "self_correction", "A version cannot correct itself.");
            }
        }
        return r with
        {
            KnownAt = r.KnownAt.ToUniversalTime(),
            PublishedAt = r.PublishedAt?.ToUniversalTime(),
            CompanyIds = r.CompanyIds.Distinct().Order().ToArray(),
            BankProfileIds = r.BankProfileIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Provenance = r.Provenance is { } provenance ? provenance with
            {
                RetrievedAt = provenance.RetrievedAt.ToUniversalTime()
            } : null
        };
    }
}
