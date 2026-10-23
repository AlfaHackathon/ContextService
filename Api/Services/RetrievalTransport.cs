using AlfaHackaton.HttpClient.Common.Interfaces;
using AlfaHackaton.HttpClient.Common.Models;
using ContextService.Common;
using System.Text.Json;

namespace ContextService.Services;

// The shared client executes HTTP. RawData preserves System.Text.Json bytes;
// optional Newtonsoft DTO conversion is not invoked for canonical contracts.
public sealed class RetrievalTransport<TSettings>(IHttpClient<TSettings> client, TSettings settings)
    where TSettings : IHttpClientSettings
{
    public Uri? BaseAddress => string.IsNullOrEmpty(settings.BaseAddress) ? null : new(settings.BaseAddress);
    public Task<HttpResponseMessage> GetAsync(string uri, CancellationToken ct) => Send(HttpMethod.Get, uri, null, null, ct);
    public Task<HttpResponseMessage> PutAsJsonAsync<T>(string uri, T body, CancellationToken ct) =>
        Send(HttpMethod.Put, uri, JsonSerializer.SerializeToUtf8Bytes(body, ContextJson.Options), null, ct);
    public Task<HttpResponseMessage> PostAsJsonAsync<T>(string uri, T body, CancellationToken ct) =>
        Send(HttpMethod.Post, uri, JsonSerializer.SerializeToUtf8Bytes(body, ContextJson.Options), null, ct);
    public Task<HttpResponseMessage> PostAsJsonAsync<T>(Uri uri, T body, CancellationToken ct) => PostAsJsonAsync(uri.ToString(), body, ct);
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        await Send(request.Method, request.RequestUri!.ToString(), request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(ct),
            (headers, contentHeaders) =>
            {
                foreach (var header in request.Headers) headers.TryAddWithoutValidation(header.Key, header.Value);
                if (request.Content is not null && contentHeaders is not null)
                    foreach (var header in request.Content.Headers)
                    { contentHeaders.Remove(header.Key); contentHeaders.TryAddWithoutValidation(header.Key, header.Value); }
            }, ct);
    private async Task<HttpResponseMessage> Send(HttpMethod method, string uri, byte[]? body, HeaderApplier? headers, CancellationToken ct)
    {
        var result = await client.Send<None, None>(new Request<None>
        {
            Method = method,
            Uri = uri,
            RawData = body,
            HeadersApplier = (requestHeaders, contentHeaders) =>
            {
                if (body is not null && contentHeaders is not null) contentHeaders.ContentType = new("application/json");
                headers?.Invoke(requestHeaders, contentHeaders);
            }
        }, ct);
        return new HttpResponseMessage(result.Status) { Content = new ByteArrayContent(result.RawData) };
    }
}
