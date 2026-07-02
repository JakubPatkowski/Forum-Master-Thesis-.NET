using System.Buffers.Text;
using System.Text;

namespace Forum.Modules.Content.Application.Paging;

/// <summary>
/// URL-safe transport encoding for keyset cursors: a pipe-delimited invariant payload wrapped in Base64Url.
/// Cursors are opaque to clients; a malformed one decodes to null and maps to a 422 at the edge.
/// </summary>
internal static class CursorCodec
{
    public static string Encode(string payload) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));

    public static string? TryDecode(string value)
    {
        try
        {
            return Encoding.UTF8.GetString(Base64Url.DecodeFromChars(value));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
