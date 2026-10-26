using System.Text.Json.Serialization;

namespace ContextService.Contracts.Knowledge;

public sealed record CompanyProfile(Guid CompanyId, string RevisionId, string? BankProfileId,
    string Name, string Description, string[] IndustryCodes, string[] Exposures,
    DateTimeOffset KnownAt, SourceReference[] SourceRefs,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DeclaredCompanyProfile? DeclaredProfile = null);
