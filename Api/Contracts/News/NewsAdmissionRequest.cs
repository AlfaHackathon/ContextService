using System.Text.Json.Serialization;
using ContextService.Common;

namespace ContextService.Contracts.News;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class NewsAdmissionRequest
{
    public required string Source { get; set; }
    public required string ExternalId { get; set; }
    public required string Title { get; set; }
    public required string Text { get; set; }
    public required string PublishedAt { get; set; }
    [JsonConverter(typeof(OptionalStringConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? KnownAt { get; set; }
    [JsonConverter(typeof(OptionalStringConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CorrectsVersionId { get; set; }
    [JsonConverter(typeof(OptionalStringConverter))]
    public string TextCompleteness { get; set; } = "lead";
    [JsonConverter(typeof(OptionalStringConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceUrl { get; set; }
}
