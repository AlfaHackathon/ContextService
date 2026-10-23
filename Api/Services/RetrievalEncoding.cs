using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ContextService.Services;

public static partial class RetrievalEncoding
{
    public static object Sparse(string text)
    {
        var counts = Tokens().Matches(text.ToLowerInvariant()).Select(x => x.Value)
            .Select(x => BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(x)), 0))
            .GroupBy(x => x).OrderBy(x => x.Key).ToArray();
        return new
        {
            indices = counts.Select(x => x.Key).ToArray(),
            values = counts.Select(x => (float)(1 + Math.Log(x.Count()))).ToArray()
        };
    }
    [GeneratedRegex(@"[\p{L}\p{N}_]+", RegexOptions.CultureInvariant)] private static partial Regex Tokens();
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Stable(params string[] values) => new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', values))).AsSpan(0, 16)).ToString("D");

}
