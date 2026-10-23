using ContextService.Contracts.Retrieval;
using ContextService.Contracts.Search;
using ContextService.Models;
using ContextService.Repositories.Interfaces;

namespace ContextService.Services;

public sealed class RetrievedContextService(MaterialService materials, IMaterialRepository repository,
    KnowledgeService knowledge, SearchService search, IConfiguration config)
{
    public async Task<RetrievedContextBundle> GetAsync(RetrieveContextRequest r, CancellationToken ct)
    {
        if (r.CompanyId == Guid.Empty || r.AsOf == default || r.Queries is null || r.Queries.Length is < 1 or > 6)
        {
            throw new ApiException(400, "invalid_context_queries", "Company, asOf and 1–6 retrieval queries are required.");
        }

        if (r.Scope is not (null or "company" or "market") || r.RequiredMaterialIds?.Length > 6)
        {
            throw new ApiException(400, "invalid_followup_scope", "Follow-up scope and at most six material identifiers are required.");
        }

        if (r.MaxCharacters is < 1024 or > 120000)
        {
            throw new ApiException(400, "invalid_context_budget", "Requested decoded context budget must be between 1024 and 120000 characters.");
        }

        var news = await materials.GetAsync(r.NewsMaterialId, r.NewsVersionId);
        if (news.Kind != "news" || news.KnownAt > r.AsOf || (news.CompanyIds.Length > 0 && !news.CompanyIds.Contains(r.CompanyId)))
        {
            throw new ApiException(403, "news_scope", "The news is unavailable for this company at asOf.");
        }

        var allowed = (await repository.GetContextMaterials(r.CompanyId, r.NewsMaterialId, r.AsOf))
            .Where(x => x.MaterialId != news.MaterialId || x.VersionId != news.VersionId).ToArray();
        var selected = new HashSet<(string, string)>();
        var candidates = 0;
        var mode = "";
        var reranker = "";
        var ranks = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var id in r.RequiredMaterialIds ?? [])
        {
            var matching = allowed.Where(m => m.MaterialId == id && (r.Scope is null || m.Kind == r.Scope)).ToArray();
            if (matching.Length > 0)
            {
                foreach (var original in matching.Where(m => m.KnownAt == matching.Max(x => x.KnownAt)))
                {
                    selected.Add((original.MaterialId, original.VersionId));
                }
            }
        }
        foreach (var query in r.Queries.Distinct(StringComparer.Ordinal))
        {
            var result = await search.Search(new SearchRequest(query, r.AsOf, r.CompanyId, r.Limit), ct);
            candidates += result.CandidatesExamined;
            mode = result.Mode;
            reranker = result.Reranker;
            foreach (var (h, rank) in result.Hits.Select((hit, rank) => (hit, rank)).Where(x => x.hit.MaterialId is not null && x.hit.VersionId is not null))
            {
                selected.Add((h.MaterialId!, h.VersionId!));
                ranks[h.MaterialId!] = ranks.GetValueOrDefault(h.MaterialId!) + 1.0 / (60 + rank + 1);
            }
        }
        var profile = (await knowledge.Companies(r.AsOf)).SingleOrDefault(x => x.CompanyId == r.CompanyId);
        if (profile is not null)
        {
            foreach (var source in profile.SourceRefs)
            {
                selected.Add((source.MaterialId, source.VersionId));
            }
        }
        // An old chunk can rank above its correction. Supply the newest known
        // original of each selected material as well, without replacing history
        // or claiming that knownAt establishes contractual effective dates.
        foreach (var id in selected.Select(x => x.Item1).Distinct().ToArray())
        {
            var matching = allowed.Where(m => m.MaterialId == id).ToArray();
            // Version IDs are opaque. Preserve equal-time corrections/forks for
            // explicit comparison instead of silently choosing a lexical winner.
            if (matching.Length > 0)
            {
                foreach (var newest in matching.Where(m => m.KnownAt == matching.Max(x => x.KnownAt)))
                {
                    selected.Add((newest.MaterialId, newest.VersionId));
                }
            }
        }
        foreach (var m in allowed.Where(x => x.Kind == "news"))
        {
            selected.Add((m.MaterialId, m.VersionId));
        }

        var documents = allowed.Where(x => selected.Contains((x.MaterialId, x.VersionId)) &&
            (r.Scope is null || x.Kind == r.Scope || x.Kind == "news" || profile?.SourceRefs.Any(s => s.MaterialId == x.MaterialId && s.VersionId == x.VersionId) == true)).ToArray();
        var maximum = Math.Min(config.GetValue<int?>("Context:MaxCharacters") ?? 120000, r.MaxCharacters ?? 120000);
        var required = new HashSet<string>(r.RequiredMaterialIds ?? [], StringComparer.Ordinal);
        if (profile is not null)
        {
            required.UnionWith(profile.SourceRefs.Select(s => s.MaterialId));
        }

        required.UnionWith(documents.Where(m => m.Kind == "news").Select(m => m.MaterialId));
        var packed = ContextBudget.Pack(news, documents, required, ranks, maximum);
        documents = packed.Materials;
        var size = packed.Budget.UsedCharacters;
        var omitted = allowed.Length - documents.Length;
        return new(news, documents, new(omitted == 0, documents.Length + 1, documents.Count(x => x.Kind == "company"),
            documents.Count(x => x.Kind == "market"), documents.Count(x => x.Kind == "news"), size, r.AsOf, packed.Budget),
            new(mode, reranker, candidates, documents.Length, allowed.Length, omitted, omitted == 0));
    }
}
