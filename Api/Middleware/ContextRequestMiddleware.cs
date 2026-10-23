using System.Security.Cryptography;
using System.Text;
using ContextService.Models;
using Npgsql;

namespace ContextService.Middleware;

public static partial class ContextRequestMiddleware
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Context request failed")]
    private static partial void LogRequestFailed(ILogger logger, Exception exception);

    public static void UseContextRequestHandling(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            try
            {
                if (context.Request.Path.StartsWithSegments("/context-service/internal"))
                {
                    var expected = app.Configuration["Internal:Secret"];
                    var supplied = context.Request.Headers["X-Internal-Secret"].ToString();
                    if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(supplied) ||
                        !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
                            SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
                    {
                        context.Response.StatusCode = 403;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            code = "internal_access_denied",
                            message = "A configured internal credential is required."
                        });
                        return;
                    }
                }
                await next(context);
            }
            catch (ApiException e)
            {
                context.Response.StatusCode = e.StatusCode;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = e.Code,
                    message = e.Message
                });
            }
            catch (BadHttpRequestException)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "invalid_request",
                    message = "The request body or parameters are invalid."
                });
            }
            catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.QueryCanceled)
            {
                context.Response.StatusCode = 503;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "storage_busy",
                    message = "Local storage is busy; no successful result is implied."
                });
            }
            catch (Exception e)
            {
                LogRequestFailed(app.Logger, e);
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "internal_error",
                    message = "The request failed; no successful result is implied."
                });
            }
        });
    }
}
