using System.Text.Encodings.Web;
using System.Text.Json;
using AlfaHackaton.Result;
using ContextService.Common;
using ContextService.Contracts.Knowledge;
using ContextService.Contracts.Materials;
using ContextService.Models;

namespace ContextService.Services;

public static class ContextBudget
{
    // Budget the decoded JSON seen by the model, not six-character Cyrillic escapes.
    // Keep ContextJson.Options unchanged: it defines immutable source hashes.
    private static readonly JsonSerializerOptions DecodedJson = new(ContextJson.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    public static int Measure(MaterialVersion news, IEnumerable<MaterialVersion> documents) => JsonSerializer.Serialize(new { news, materials = documents.ToArray() }, DecodedJson).Length;
    public static Result<BudgetedMaterials> Pack(MaterialVersion news, MaterialVersion[] candidates, ISet<string> requiredMaterialIds, IReadOnlyDictionary<string, double> ranks, int maximum)
    {
        var groups = candidates.GroupBy(m => m.MaterialId).ToArray();
        var kept = groups.Where(g => requiredMaterialIds.Contains(g.Key)).SelectMany(g => g).ToList();
        if (Measure(news, kept) > maximum)
        {
            return new(new ApiError(422, "mandatory_context_budget_exceeded", "The news and mandatory complete originals exceed the context budget; no source was truncated or substituted."));
        }

        var omitted = new List<SourceReference>();
        foreach (var group in groups.Where(g => !requiredMaterialIds.Contains(g.Key)).OrderByDescending(g => ranks.GetValueOrDefault(g.Key)).ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            // Old hit, correction and equal-time forks travel together, or are all omitted.
            if (Measure(news, kept.Concat(group)) <= maximum)
            {
                kept.AddRange(group);
            }
            else
            {
                omitted.AddRange(group.Select(m => new SourceReference(m.MaterialId, m.VersionId, m.ContentHash)));
            }
        }

        return new(new(kept.ToArray(), new(maximum, Measure(news, kept), omitted.ToArray(), omitted.Count == 0 ? "within_budget" : "context_budget")), null);
    }
}
