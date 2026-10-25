namespace ContextService.Resources;

public static class ContextSql
{
    private static readonly System.Resources.ResourceManager Manager = new("ContextService.Resources.ContextSql", typeof(ContextSql).Assembly);
    private static string Get(string key) => Manager.GetString(key, System.Globalization.CultureInfo.InvariantCulture)
        ?? throw new InvalidOperationException($"Missing embedded SQL resource: {key}");
    public static string ChangeSchema => Get(nameof(ChangeSchema));
    public static string AppendChange => Get(nameof(AppendChange));
    public static string ReadNextChange => Get(nameof(ReadNextChange));
    public static string KnowledgeSchema => Get(nameof(KnowledgeSchema));
    public static string MaterialSchema => Get(nameof(MaterialSchema));
    public static string FindMaterialIdentity => Get(nameof(FindMaterialIdentity));
    public static string InsertMaterial => Get(nameof(InsertMaterial));
    public static string InsertMaterialCompany => Get(nameof(InsertMaterialCompany));
    public static string ContextMaterials => Get(nameof(ContextMaterials));
    public static string NewsSearch => Get(nameof(NewsSearch));
    public static string NewsSearchDeadline => Get(nameof(NewsSearchDeadline));
    public static string KnownMaterials => Get(nameof(KnownMaterials));
    public static string FindMaterial => Get(nameof(FindMaterial));
    public static string ChangeWatermark => Get(nameof(ChangeWatermark));
    public static string EvidenceWatchCursor => Get(nameof(EvidenceWatchCursor));
    public static string CompanyEvidence => Get(nameof(CompanyEvidence));
    public static string ProjectionSchema => Get(nameof(ProjectionSchema));
    public static string ProjectionMarks => Get(nameof(ProjectionMarks));
    public static string UpsertProjectionMark => Get(nameof(UpsertProjectionMark));
    public static string WriteLock => Get(nameof(WriteLock));
    public static string ReadLock => Get(nameof(ReadLock));
}
