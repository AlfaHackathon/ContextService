using ContextService.Contracts.Materials;

namespace ContextService.Contracts.Retrieval;

public sealed record RetrievedContextBundle(MaterialVersion News, MaterialVersion[] Materials,
    ContextCoverage Coverage, RetrievalManifest Retrieval);
