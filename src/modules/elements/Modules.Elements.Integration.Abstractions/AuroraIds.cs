using System.Security.Cryptography;
using System.Text;

namespace Starlights.Modules.Elements.Integration;

/// <summary>
/// The element id an Aurora id is imported as: the same id every time (a name-based UUID, version 5), so re-importing
/// an element keeps every character that uses it, and the id of an element can be known before it is imported.
/// </summary>
public static class AuroraIds
{
    private static readonly Guid Namespace = new("8a4c4f1e-2b7d-4d52-9d0e-6f3b2a9c7e11");

    /// <summary>The id for an Aurora id (or for anything named after one, as component ids are).</summary>
    public static Guid ToGuid(string name)
    {
        byte[] input = [.. Namespace.ToByteArray(bigEndian: true), .. Encoding.UTF8.GetBytes(name)];
        var hash = SHA1.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50); // version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}
