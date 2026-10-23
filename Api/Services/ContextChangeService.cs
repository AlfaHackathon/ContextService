using ContextService.Contracts.Changes;
using ContextService.Models;
using ContextService.Repositories;

namespace ContextService.Services;

public sealed class ContextChangeService(ContextChanges changes)
{
    public Task<ContextChange[]> Read(long after)
    {
        if (after < 0)
        {
            throw new ApiException(400, "invalid_change_cursor", "Change cursor must be nonnegative.");
        }

        return changes.Read(after);
    }
}
