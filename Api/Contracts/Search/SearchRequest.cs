
namespace ContextService.Contracts.Search;

public sealed record SearchRequest(string Query, DateTimeOffset AsOf, Guid? CompanyId = null, int Limit = 12, bool PublicOnly = false);
