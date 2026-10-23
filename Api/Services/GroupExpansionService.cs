using System.Net;
using AlfaHackaton.Result;
using AlfaHackaton.Result.Errors;
using ContextService.Common;
using ContextService.Contracts.Knowledge;

namespace ContextService.Services;

public static class GroupExpansionService
{
    public static Result<GroupExpansion> Expand(string id, GroupProfile[] profiles)
    {
        if (IdentifierValidation.RequireId(id, "groupId").Error is { } groupIdError)
        {
            return new(groupIdError);
        }
        var groups = profiles.ToDictionary(x => x.GroupId, StringComparer.Ordinal);
        var paths = new List<GroupPath>();
        var missing = new HashSet<string>();
        var cycles = new List<string[]>();
        var refs = new HashSet<SourceReference>();
        var visited = 0;
        Result Visit(string current, string[] path)
        {
            if (++visited > 2048 ||
                path.Length >= 32)
            {
                return new(new Error("The explicit group graph exceeds the traversal budget.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.UnprocessableEntity });
            }

            if (path.Contains(current, StringComparer.Ordinal))
            {
                cycles.Add([.. path, current]);
                return new();
            }

            if (!groups.TryGetValue(current, out var group))
            {
                missing.Add(current);
                return new();
            }

            foreach (var r in group.SourceRefs)
            {
                refs.Add(r);
            }

            var next = path.Append(current).ToArray();
            foreach (var member in group.Members)
            {
                foreach (var r in member.SourceRefs)
                {
                    refs.Add(r);
                }

                if (member.Kind == "group")
                {
                    if (Visit(member.Id, next).Error is { } memberExpansionError)
                    {
                        return new(memberExpansionError);
                    }
                }
                else
                {
                    paths.Add(new GroupPath(Guid.Parse(member.Id), next));
                }
            }

            return new();
        }

        if (Visit(id, []).Error is { } expansionError)
        {
            return new(expansionError);
        }
        return new(new GroupExpansion(id,
            paths.Select(x => x.CompanyId).Distinct().Order().ToArray(),
            paths.ToArray(),
            missing.Order().ToArray(),
            cycles.ToArray(),
            missing.Count == 0 &&
            cycles.Count == 0,
            refs.ToArray(),
            groups.TryGetValue(id, out var root) ? root.AggregationRule : null),
            null);
    }
}
