using System.Security.Cryptography;
using System.Text;

namespace SysmlStudio.Interchange;

/// <summary>
/// Hands out element ids that are a function of where an element sits in the
/// model, so that exporting the same text twice gives the same file and a
/// diff between two exports shows what changed in the model, not new ids.
/// </summary>
/// <remarks>
/// An id is a name-based UUID (version 8, RFC 9562) over a SHA-256 of a key
/// such as "Sample::Vehicle::engine" or "Sample::Vehicle::engine/typing". Keys
/// the model repeats — two anonymous elements side by side — get "#2", "#3"
/// appended in the order they are asked for, which is file order.
/// </remarks>
internal sealed class StableIds
{
    /// <summary>Mixed into every key, so these ids do not collide with other name-based schemes.</summary>
    private const string Scope = "sysml-studio/interchange/";

    private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);

    /// <summary>A key not handed out before: <paramref name="key"/> itself, or it with "#n" appended.</summary>
    public string Claim(string key)
    {
        if (!_seen.TryGetValue(key, out var count))
        {
            _seen[key] = 1;
            return key;
        }

        // "a#2" may itself be a key the model uses; keep counting until one is free.
        string candidate;
        do
        {
            count++;
            candidate = key + "#" + count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        while (_seen.ContainsKey(candidate));

        _seen[key] = count;
        _seen[candidate] = 1;
        return candidate;
    }

    /// <summary>The id for a key already claimed.</summary>
    public static Guid For(string key)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(Scope + key), hash);

        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80); // version 8
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 9562 variant
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>Claims <paramref name="key"/> and returns the key it got and its id.</summary>
    public (string Key, Guid Id) Next(string key)
    {
        var claimed = Claim(key);
        return (claimed, For(claimed));
    }
}
