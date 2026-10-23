
namespace ContextService.Contracts.Retrieval;

public sealed record RetrievalManifest(string Mode, string Reranker, int CandidateCount,
    int SelectedMaterials, int AvailableMaterials, int OmittedMaterials, bool Exhaustive);
