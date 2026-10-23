using AlfaHackaton.Result;
using ContextService.Contracts.Changes;
using ContextService.Models;
using ContextService.Repositories;

namespace ContextService.Services;

public sealed class ContextChangeService(ContextChanges changes)
{
    public async Task<Result<ContextChange[]>> Read(long after)
    {
        if (after < 0)
        {
            return new(new ApiError(400, "invalid_change_cursor", "Change cursor must be nonnegative."));
        }

        return await changes.Read(after);
    }
}
