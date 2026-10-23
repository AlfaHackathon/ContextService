using ContextService.Contracts.Context;
using ContextService.Contracts.Materials;

namespace ContextService.Models;

public sealed record BudgetedMaterials(MaterialVersion[] Materials, ContextBudgetReport Budget);
