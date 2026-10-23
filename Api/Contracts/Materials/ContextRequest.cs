
namespace ContextService.Contracts.Materials;

public sealed record ContextRequest(
    Guid CompanyId, string NewsMaterialId, string NewsVersionId, DateTimeOffset AsOf);
