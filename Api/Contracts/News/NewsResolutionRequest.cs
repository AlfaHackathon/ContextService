namespace ContextService.Contracts.News;

public class NewsResolutionRequest
{
    public required string NewsMaterialId { get; set; }
    public required string NewsVersionId { get; set; }
    public DateTimeOffset AsOf { get; set; }
}
