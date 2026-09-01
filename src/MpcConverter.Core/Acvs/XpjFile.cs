using System.IO;
using System.IO.Compression;

namespace MpcConverter.Core.Acvs;

/// <summary>
/// The sibling <c>&lt;name&gt;.xpj</c> file is a gzip-compressed copy of the
/// project's inner ACVS file. MPC reads the folder's inner file; the .xpj is
/// written alongside for completeness. We do not attempt to reproduce AKAI's
/// exact gzip byte stream — any valid gzip of the same bytes is fine.
/// </summary>
public static class XpjFile
{
    public static byte[] Decompress(byte[] gz)
    {
        using var input = new MemoryStream(gz);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    public static byte[] Compress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(raw, 0, raw.Length);
        }
        return output.ToArray();
    }
}
