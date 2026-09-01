using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MpcConverter.Core.Acvs;

/// <summary>
/// An AKAI MPC project container. The on-disk layout is a small text header
/// followed immediately by the JSON project document:
/// <code>
/// ACVS\n&lt;format-version&gt;\nSerialisableProjectData\njson\nLinux\n{ ...JSON... }
/// </code>
/// The JSON is preserved as a mutable <see cref="JsonObject"/> so unknown fields
/// survive a read/modify/write cycle untouched.
/// </summary>
public sealed class AcvsDocument
{
    private const string Magic = "ACVS";

    public string FormatVersion { get; set; }
    public string Payload { get; }
    public string Encoding { get; }
    public string Platform { get; }
    public JsonObject Root { get; }

    private AcvsDocument(string formatVersion, string payload, string encoding,
        string platform, JsonObject root)
    {
        FormatVersion = formatVersion;
        Payload = payload;
        Encoding = encoding;
        Platform = platform;
        Root = root;
    }

    /// <summary>Convenience accessor for the project payload (<c>Root["data"]</c>).</summary>
    public JsonObject Data => Root["data"]!.AsObject();

    /// <summary>Deep-copies this document so conversion never mutates the source.</summary>
    public AcvsDocument Clone()
        => new(FormatVersion, Payload, Encoding, Platform, (JsonObject)Root.DeepClone());

    public static AcvsDocument Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        // The header is five '\n'-terminated lines. Find each newline in order.
        int Newline(int from)
        {
            for (int i = from; i < bytes.Length; i++)
                if (bytes[i] == (byte)'\n') return i;
            return -1;
        }

        int n1 = Newline(0);
        if (n1 < 0) throw new InvalidDataException("Not an ACVS file: no header.");
        string magic = Encoding_UTF8(bytes, 0, n1);
        if (magic != Magic)
            throw new InvalidDataException($"Not an ACVS file: expected magic 'ACVS', got '{magic}'.");

        int n2 = Newline(n1 + 1);
        int n3 = Newline(n2 + 1);
        int n4 = Newline(n3 + 1);
        int n5 = Newline(n4 + 1);
        if (n2 < 0 || n3 < 0 || n4 < 0 || n5 < 0)
            throw new InvalidDataException("Not an ACVS file: truncated header.");

        string formatVersion = Encoding_UTF8(bytes, n1 + 1, n2);
        string payload = Encoding_UTF8(bytes, n2 + 1, n3);
        string encoding = Encoding_UTF8(bytes, n3 + 1, n4);
        string platform = Encoding_UTF8(bytes, n4 + 1, n5);

        int jsonStart = n5 + 1;
        string json = System.Text.Encoding.UTF8.GetString(bytes, jsonStart, bytes.Length - jsonStart);
        if (JsonNode.Parse(json) is not JsonObject root)
            throw new InvalidDataException("ACVS payload is not a JSON object.");

        return new AcvsDocument(formatVersion, payload, encoding, platform, root);
    }

    public byte[] ToBytes()
    {
        var sb = new StringBuilder();
        sb.Append(Magic).Append('\n');
        sb.Append(FormatVersion).Append('\n');
        sb.Append(Payload).Append('\n');
        sb.Append(Encoding).Append('\n');
        sb.Append(Platform).Append('\n');

        // MPC writes each property on its own '\n'-terminated line with ZERO
        // indentation (e.g. `{\n"data": {\n"version": 28,`). WriteIndented with
        // IndentSize = 0 reproduces this exactly (newline structure, space after
        // colon, no per-level indent). No BOM.
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            IndentCharacter = ' ',
            IndentSize = 0,
            NewLine = "\n",
            // MPC escapes only what JSON strictly requires (" \ and control chars)
            // and writes '+', '<', '>', '&', and UTF-8 literally.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        string json = Root.ToJsonString(options);
        sb.Append(json);

        // UTF-8 without BOM.
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(sb.ToString());
    }

    private static string Encoding_UTF8(byte[] bytes, int start, int endExclusive)
        => System.Text.Encoding.UTF8.GetString(bytes, start, endExclusive - start);
}
