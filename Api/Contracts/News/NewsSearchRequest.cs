using System.Text.Json.Serialization;

namespace ContextService.Contracts.News;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NewsSearchRequest(string? Query, string? AsOf, int Limit = 6);
