using System.ComponentModel.DataAnnotations;

namespace ContextService.Contracts.Materials;

// Internal caller supplies the current membership's bank bridge, never an end-user grant.
public sealed class CompanyMaterialListRequest
{
    public string? AsOf { get; set; }
    [Range(1, 20)] public int Limit { get; set; } = 20;
    [MaxLength(2048)] public string? Cursor { get; set; }
    [MaxLength(200)] public string? BankProfileId { get; set; }
}
