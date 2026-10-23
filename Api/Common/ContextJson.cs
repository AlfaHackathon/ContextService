using System.Text.Json;

namespace ContextService.Common;

public static class ContextJson
{
    // These options define canonical immutable payloads and content hashes.
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
