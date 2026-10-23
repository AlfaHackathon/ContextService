
namespace ContextService.Contracts.Retrieval;

public sealed record RetrieveContextRequest(Guid CompanyId, string NewsMaterialId, string NewsVersionId,
    DateTimeOffset AsOf, string[] Queries, int Limit = 12, string? Scope = null, string[]? RequiredMaterialIds = null,
    int? MaxCharacters = null);
