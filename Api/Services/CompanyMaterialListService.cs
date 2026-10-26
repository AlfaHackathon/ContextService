using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using AlfaHackaton.Result;
using AlfaHackaton.Result.Errors;
using ContextService.Common;
using ContextService.Contracts.Materials;
using ContextService.Repositories.Interfaces;

namespace ContextService.Services;

public sealed partial class CompanyMaterialListService(ICompanyMaterialListRepository repository)
{
    public async Task<Result<CompanyMaterialPage>> List(Guid companyId, CompanyMaterialListRequest request, CancellationToken ct)
    {
        if (companyId == Guid.Empty || request.Limit is < 1 or > 20 || !TryInstant(request.AsOf, out var asOf) ||
            request.BankProfileId is not null && IdentifierValidation.RequireId(request.BankProfileId, "bankProfileId").Error is not null)
        {
            return new(Invalid("A company, explicit asOf, optional exact bank bridge and limit 1..20 are required."));
        }
        CompanyMaterialContinuation? continuation = null;
        if (request.Cursor is not null && !CompanyMaterialCursor.TryRead(request.Cursor, companyId, asOf, request.BankProfileId, out continuation))
        {
            return new(Invalid("The materials cursor does not match this company, bank bridge or knowledge time."));
        }
        ct.ThrowIfCancellationRequested();
        var slice = await repository.List(companyId, asOf, request.BankProfileId, request.Limit, continuation, ct);
        ct.ThrowIfCancellationRequested();
        var items = slice.Items.Take(request.Limit).ToArray();
        var last = items.LastOrDefault();
        var cursor = slice.Items.Count > request.Limit && last is not null
            ? CompanyMaterialCursor.Encode(companyId, asOf, request.BankProfileId, slice.SnapshotSequence,
                new(last.KnownAt.UtcTicks, last.MaterialId, last.VersionId)) : null;
        return new(new CompanyMaterialPage(companyId, asOf, items, cursor), null);
    }

    private static Error Invalid(string message) => new(message, ErrorSource.Client, ErrorType.CorruptedInfo)
    {
        HttpStatusCode = HttpStatusCode.BadRequest
    };

    private static bool TryInstant(string? text, out DateTimeOffset instant)
    {
        instant = default;
        if (text is not { Length: <= 33 } || !InstantPattern().IsMatch(text) ||
            !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out instant) || instant == default)
        {
            return false;
        }
        instant = instant.ToUniversalTime();
        return true;
    }

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?(Z|[+-][0-9]{2}:[0-9]{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex InstantPattern();
}
