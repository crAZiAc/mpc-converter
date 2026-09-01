using System.Text.Json.Nodes;
using MpcConverter.Core.Acvs;

namespace MpcConverter.Core.ProjectIo;

/// <summary>An MPC project loaded into memory.</summary>
public sealed class MpcProject
{
    public required string Name { get; init; }
    public required AcvsDocument Document { get; init; }

    /// <summary>Absolute path to the <c>&lt;Name&gt;_[ProjectData]</c> sample folder, or null.</summary>
    public string? ProjectDataDir { get; init; }

    /// <summary>The project payload object (<c>Document.Root["data"]</c>).</summary>
    public JsonObject Data => Document.Data;
}
