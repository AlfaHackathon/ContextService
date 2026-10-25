using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ContextService.Common;
using ContextService.Repositories.Interfaces;

namespace ContextService.Services;

internal static partial class CompanyMaterialCursor
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record Payload(int Version, Guid CompanyId, string AsOfTicks, string? BankProfileId,
        string SnapshotSequence, string KnownTicks, string MaterialId, string VersionId);

    internal static string Encode(Guid company, DateTimeOffset asOf, string? bank, long sequence, CompanyMaterialPosition position)
    {
        var payload = new Payload(1, company, asOf.UtcTicks.ToString(CultureInfo.InvariantCulture), bank,
            sequence.ToString(CultureInfo.InvariantCulture), position.KnownTicks.ToString(CultureInfo.InvariantCulture), position.MaterialId, position.VersionId);
        return Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload, ContextJson.Options))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static bool TryRead(string text, Guid company, DateTimeOffset asOf, string? bank,
        out CompanyMaterialContinuation? continuation)
    {
        continuation = null;
        if (text.Length is 0 or > 2048 || !CursorPattern().IsMatch(text))
        {
            return false;
        }
        try
        {
            var encoded = text.Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
            var value = JsonSerializer.Deserialize<Payload>(Convert.FromBase64String(encoded), ContextJson.Options);
            if (value is null || value.Version != 1 || value.CompanyId != company || value.BankProfileId != bank ||
                !long.TryParse(value.AsOfTicks, NumberStyles.None, CultureInfo.InvariantCulture, out var at) || at != asOf.UtcTicks ||
                !long.TryParse(value.SnapshotSequence, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) || sequence < 0 ||
                !long.TryParse(value.KnownTicks, NumberStyles.None, CultureInfo.InvariantCulture, out var known) || known <= 0 || known > at ||
                IdentifierValidation.RequireId(value.MaterialId, "cursor materialId").Error is not null ||
                IdentifierValidation.RequireId(value.VersionId, "cursor versionId").Error is not null)
            {
                return false;
            }
            continuation = new(sequence, new(known, value.MaterialId, value.VersionId));
            return true;
        }
        catch (Exception error) when (error is FormatException or JsonException)
        {
            return false;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex CursorPattern();
}

