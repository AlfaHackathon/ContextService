using System.Net;
using AlfaHackaton.Result;
using AlfaHackaton.Result.Errors;
using ContextService.Contracts.Changes;
using ContextService.Repositories;

namespace ContextService.Services;

public sealed class ContextChangeService(ContextChanges changes)
{
    public async Task<Result<ContextChange[]>> Read(long after)
    {
        if (after < 0)
        {
            return new(new Error("Change cursor must be nonnegative.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
        }

        return await changes.Read(after);
    }
}
