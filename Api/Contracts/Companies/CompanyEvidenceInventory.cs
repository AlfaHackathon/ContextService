
namespace ContextService.Contracts.Companies;

public sealed record CompanyEvidenceInventory(Guid CompanyId, DateTimeOffset AsOf,
    CompanyEvidenceSource[] Sources, bool CompleteForLoadedScope, long ChangeSequence, long WatchAfterSequence);
