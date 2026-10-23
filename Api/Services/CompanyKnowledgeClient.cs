using System.Globalization;
using System.Net;
using System.Text.Json;
using AlfaHackaton.Result;
using AlfaHackaton.Result.Errors;
using ContextService.Common;
using ContextService.Contracts.Knowledge;
using ContextService.Settings;

namespace ContextService.Services;

// CompanyService owns these records and their rules. Context only reads them for retrieval
// and forwards the former knowledge routes for callers migrating to CompanyService.
public sealed class CompanyKnowledgeClient(RetrievalTransport<CompanyHttpSettings> transport)
{
    private const string Prefix = "company-service/internal/knowledge/";
    private static string At(DateTimeOffset at) => Uri.EscapeDataString(at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

    public Task<Result<CompanyProfile>> SaveCompany(CompanyProfile value, CancellationToken ct = default) => Send<CompanyProfile>("companies", value, ct: ct);
    public Task<Result<GroupProfile>> SaveGroup(GroupProfile value, CancellationToken ct = default) => Send<GroupProfile>("groups", value, ct: ct);
    public Task<Result<RoleProfile>> SaveRole(RoleProfile value, CancellationToken ct = default) => Send<RoleProfile>("roles", value, ct: ct);
    public Task<Result<PersonProfile>> SavePerson(PersonProfile value, CancellationToken ct = default) => Send<PersonProfile>("people", value, ct: ct);
    public Task<Result<CompanyProfile[]>> Companies(DateTimeOffset at, CancellationToken ct = default) => Send<CompanyProfile[]>($"companies?asOf={At(at)}", field: "companies", ct: ct);
    public Task<Result<CompanyProfile[]>> CompanyVersions(CancellationToken ct = default) => Send<CompanyProfile[]>("companies/versions", field: "companies", ct: ct);
    public Task<Result<GroupProfile[]>> Groups(DateTimeOffset at, CancellationToken ct = default) => Send<GroupProfile[]>($"groups?asOf={At(at)}", field: "groups", ct: ct);
    public Task<Result<RoleProfile[]>> Roles(DateTimeOffset at, CancellationToken ct = default) => Send<RoleProfile[]>($"roles?asOf={At(at)}", field: "roles", ct: ct);
    public Task<Result<PersonProfile>> Person(string id, DateTimeOffset at, CancellationToken ct = default) => Send<PersonProfile>($"people/{Uri.EscapeDataString(id)}?asOf={At(at)}", ct: ct);
    public Task<Result<GroupExpansion>> Expand(string id, DateTimeOffset at, CancellationToken ct = default) => Send<GroupExpansion>($"groups/{Uri.EscapeDataString(id)}?asOf={At(at)}", ct: ct);

    private async Task<Result<T>> Send<T>(string path, object? value = null, string? field = null, CancellationToken ct = default)
    {
        if (transport.BaseAddress is null)
        {
            return new(new Error("CompanyService:BaseUrl is required for company knowledge operations.", ErrorSource.External, ErrorType.Undefined) { HttpStatusCode = HttpStatusCode.ServiceUnavailable });
        }
        try
        {
            using var response = value is null
                ? await transport.GetAsync(Prefix + path, ct)
                : await transport.PostAsJsonAsync(Prefix + path, value, ct);
            if (!response.IsSuccessStatusCode)
            {
                var message = $"CompanyService returned HTTP {(int)response.StatusCode}.";
                try
                {
                    using var failure = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                    if (failure.RootElement.TryGetProperty("errorMessage", out var error) && error.ValueKind == JsonValueKind.String)
                    {
                        message = error.GetString()!;
                    }
                }
                catch (JsonException) { }
                var status = (int)response.StatusCode is >= 300 and < 400 ? HttpStatusCode.BadGateway : response.StatusCode;
                return new(new Error(message, ErrorSource.External, ErrorType.Undefined) { HttpStatusCode = status });
            }
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var content = field is null ? json.RootElement : json.RootElement.GetProperty(field);
            return new(content.Deserialize<T>(ContextJson.Options) ?? throw new JsonException("CompanyService returned null."), null);
        }
        catch (HttpRequestException)
        {
            return new(new Error("CompanyService is unavailable.", ErrorSource.External, ErrorType.Undefined) { HttpStatusCode = HttpStatusCode.ServiceUnavailable });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(new Error("CompanyService request timed out.", ErrorSource.External, ErrorType.Undefined) { HttpStatusCode = HttpStatusCode.GatewayTimeout });
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new(new Error("CompanyService returned an invalid knowledge response.", ErrorSource.External, ErrorType.Undefined) { HttpStatusCode = HttpStatusCode.BadGateway });
        }
    }
}
