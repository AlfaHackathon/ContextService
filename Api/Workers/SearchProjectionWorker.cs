using ContextService.Services;

namespace ContextService.Workers;

public sealed partial class SearchProjectionWorker(SearchService search, ILogger<SearchProjectionWorker> logger) : BackgroundService
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Search projection pending ({ErrorType}); canonical data remains durable")]
    private static partial void LogProjectionPending(ILogger logger, string errorType);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await search.Sync(stoppingToken);
                if (result.Error is { } error)
                {
                    LogProjectionPending(logger, error.Type.ToString());
                }

            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                LogProjectionPending(logger, e.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

    }
}
