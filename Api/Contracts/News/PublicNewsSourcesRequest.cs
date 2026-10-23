using System.Text.Json.Serialization;
using ContextService.Contracts.Knowledge;

namespace ContextService.Contracts.News;

public class PublicNewsSourcesRequest
{
    [JsonRequired]
    public SourceReference[] SourceRefs { get; set; } = [];
    public DateTimeOffset AsOf { get; set; }
}
