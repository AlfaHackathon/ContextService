namespace ContextService.Contracts.News;

public class NewsAdmissionResponse
{
    public string NewsMaterialId { get; set; } = string.Empty;
    public string NewsVersionId { get; set; } = string.Empty;
    public DateTimeOffset AsOf { get; set; }
    public string TextCompleteness { get; set; } = "unknown";
}
