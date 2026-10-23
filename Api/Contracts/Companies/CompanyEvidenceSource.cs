
namespace ContextService.Contracts.Companies;

public sealed record CompanyEvidenceSource(string MaterialId, string VersionId, string ContentHash,
    string SourceId, DateTimeOffset KnownAt);
