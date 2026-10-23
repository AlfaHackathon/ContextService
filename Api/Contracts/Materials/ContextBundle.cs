
namespace ContextService.Contracts.Materials;

public sealed record ContextBundle(
    MaterialVersion News, IReadOnlyList<MaterialVersion> Materials, ContextCoverage Coverage);
