# MPC Sample → MPC 3.9 Converter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A WPF desktop app that converts an AKAI MPC "Sample" project (ACVS 1.3.0.12, pads-on-one-track) into a project MPC X v3.9 can open (ACVS 3.10.0.23, track-based), splitting the Drum program's pads into tracks per a configurable map, across all sequences.

**Architecture:** A pure-logic `MpcConverter.Core` library (no WPF, no hard network dep) does all ACVS/JSON I/O and conversion via a "superset field-merge" onto embedded 3.10 templates. A WPF MVVM app (`MpcConverter.App`) drives it: open → analyze pads → map → convert. xUnit tests use the two real reference projects as fixtures.

**Tech Stack:** .NET 10, C#, WPF (net10.0-windows), System.Text.Json (JsonNode), xUnit, Anthropic .NET SDK (Anthropic), Windows DPAPI for key storage.

**Spec:** `docs/superpowers/specs/2026-08-31-mpc-sample-to-3.9-converter-design.md`

## Global Constraints

- Target frameworks: Core + Tests `net10.0`; App `net10.0-windows` with `<UseWPF>true</UseWPF>`.
- JSON: use `System.Text.Json.Nodes.JsonNode`. Preserve key order and unknown fields — never round-trip through a POCO that drops keys. Serialize with the same formatting the source uses (see Task 2/3 for the exact writer settings; MPC files are pretty-printed with 1-space indent and `\n` newlines, no BOM).
- ACVS header format, verbatim: `ACVS\n<version>\nSerialisableProjectData\njson\nLinux\n` immediately followed by the JSON (first byte `{`).
- Target output versions: ACVS `3.10.0.23`, `data.version` = 30, drum instrument `version` = 29, clip `version` = 3, `mixer.input.version` = 6. `originalCreatorProductIdentifier`/`lastSavedProductIdentifier` stay `"ACVS"`.
- Pad→note base: canonical slot 0 = MIDI note 36, slot 1 = 37, … (i.e. `note = 36 + slot`).
- Converter is non-destructive: never write into the source folder.
- **Git: never commit or push. The user commits. Every "Checkpoint" below means "stop; state what's ready; leave it for the user to commit."**
- Reference files live at `C:\Users\TomdeRidder\Downloads\mpc\House os 01\` and `...\Keys v1\`. Pretty-printed JSON extracts are in the session scratchpad (`house.json`, `keys.json`).

---

## File Structure

```
mpc-converter.sln
├─ src/
│  ├─ MpcConverter.Core/
│  │   ├─ Acvs/AcvsDocument.cs          container parse/serialize (header + JsonNode)
│  │   ├─ Acvs/XpjFile.cs               gzip read/write of the .xpj copy
│  │   ├─ ProjectIo/MpcProject.cs       in-memory project (doc + sample folder path)
│  │   ├─ ProjectIo/ProjectReader.cs    open a project folder
│  │   ├─ ProjectIo/ProjectWriter.cs    write a new project folder + copy samples
│  │   ├─ Model/PadInfo.cs              analyzed source pad
│  │   ├─ Model/PadTrackMap.cs          mapping model + presets
│  │   ├─ Analysis/PadAnalyzer.cs       build PadInfo list from a source doc
│  │   ├─ Templates/*.json             embedded 3.10 default fragments (from Keys)
│  │   ├─ Templates/TemplateStore.cs    load embedded templates as JsonNode
│  │   ├─ Conversion/JsonMerge.cs       superset field-merge
│  │   ├─ Conversion/ProgramBuilder.cs  build a per-track drum program
│  │   ├─ Conversion/SequenceRewriter.cs partition + renormalize events
│  │   ├─ Conversion/Converter.cs       orchestrator + self-check + report
│  │   ├─ Conversion/ConversionReport.cs
│  │   ├─ Classification/IPadClassifier.cs
│  │   ├─ Classification/PadSuggestion.cs
│  │   ├─ Classification/RuleBasedClassifier.cs
│  │   ├─ Classification/ClaudeClassifier.cs
│  │   └─ Settings/AppSettings.cs + DpapiKeyStore.cs
│  └─ MpcConverter.App/  (WPF MVVM: MainWindow, ViewModels, SettingsWindow)
└─ tests/MpcConverter.Core.Tests/  (xUnit; fixtures copied from the two projects)
```

---

## Task 1: Solution scaffolding + fixtures

**Files:**
- Create: `mpc-converter.sln`, `src/MpcConverter.Core/MpcConverter.Core.csproj`, `src/MpcConverter.App/MpcConverter.App.csproj`, `tests/MpcConverter.Core.Tests/MpcConverter.Core.Tests.csproj`
- Create: `tests/MpcConverter.Core.Tests/Fixtures/` with copies of both reference project folders.

**Interfaces:**
- Produces: three projects wired (`App`→`Core`, `Tests`→`Core`), fixtures on disk, `dotnet build` and `dotnet test` succeed (0 tests).

- [ ] **Step 1:** Create projects and solution:
```bash
cd "C:/Users/TomdeRidder/source/mpc-converter"
dotnet new classlib -n MpcConverter.Core -o src/MpcConverter.Core -f net10.0
dotnet new xunit -n MpcConverter.Core.Tests -o tests/MpcConverter.Core.Tests -f net10.0
dotnet new wpf -n MpcConverter.App -o src/MpcConverter.App -f net10.0-windows
dotnet new sln -n mpc-converter
dotnet sln add src/MpcConverter.Core src/MpcConverter.App tests/MpcConverter.Core.Tests
dotnet add tests/MpcConverter.Core.Tests reference src/MpcConverter.Core
dotnet add src/MpcConverter.App reference src/MpcConverter.Core
```
- [ ] **Step 2:** Delete the default `Class1.cs` / `UnitTest1.cs` stubs.
- [ ] **Step 3:** Copy fixtures: copy `Downloads/mpc/House os 01`, `House os 01_[ProjectData]`, `Keys v1`, `Keys v1_[ProjectData]` into `tests/.../Fixtures/`. Mark them `CopyToOutputDirectory=PreserveNewest` via a `<Content Include="Fixtures/**">` item, OR resolve fixture paths relative to the test source dir at runtime (preferred — avoids copying WAVs to bin). Use a `FixturePaths` helper that walks up from `AppContext.BaseDirectory` to the project dir.
- [ ] **Step 4:** `dotnet build` — expected success.
- [ ] **Checkpoint:** solution builds; ready to commit.

---

## Task 2: ACVS parse (header + JSON)

**Files:**
- Create: `src/MpcConverter.Core/Acvs/AcvsDocument.cs`
- Test: `tests/MpcConverter.Core.Tests/AcvsDocumentTests.cs`

**Interfaces:**
- Produces:
```csharp
public sealed class AcvsDocument {
    public string FormatVersion { get; }         // e.g. "1.3.0.12"
    public string Payload { get; }               // "SerialisableProjectData"
    public string Encoding { get; }              // "json"
    public string Platform { get; }              // "Linux"
    public JsonObject Root { get; }              // full document; Root["data"] is the project
    public static AcvsDocument Parse(byte[] bytes);
    public byte[] ToBytes();                      // Task 3
}
```

- [ ] **Step 1: failing test** — Parse the House fixture inner file, assert header + a known value:
```csharp
[Fact]
public void Parse_House_ReadsHeaderAndData() {
    var bytes = File.ReadAllBytes(FixturePaths.HouseInnerFile);
    var doc = AcvsDocument.Parse(bytes);
    Assert.Equal("1.3.0.12", doc.FormatVersion);
    Assert.Equal("SerialisableProjectData", doc.Payload);
    Assert.Equal("json", doc.Encoding);
    Assert.Equal("Linux", doc.Platform);
    Assert.Equal(28, (int)doc.Root["data"]!["version"]!);
    Assert.Equal("C Major", (string)doc.Root["data"]!["key"]!);
}
```
- [ ] **Step 2:** Run → FAIL (type doesn't exist).
- [ ] **Step 3: implement** — read bytes as UTF-8; the header is the first 5 `\n`-terminated lines (`ACVS`, version, payload, encoding, platform); JSON starts at the byte after the 5th `\n` (find index of first `{`). Parse JSON with `JsonNode.Parse`. Validate line 1 == `ACVS` else throw `InvalidDataException("Not an ACVS file")`.
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Add a guard test: parsing non-ACVS bytes throws `InvalidDataException`. Implement, run → PASS.
- [ ] **Checkpoint.**

---

## Task 3: ACVS serialize + round-trip

**Files:** Modify `AcvsDocument.cs`; Test `AcvsDocumentTests.cs`.

**Interfaces:** Produces `byte[] ToBytes()` and a `WithFormatVersion(string)` returning a new header value; round-trip is byte-stable for unchanged docs.

- [ ] **Step 1: failing test** — round-trip equality on House:
```csharp
[Fact]
public void RoundTrip_House_IsByteStable() {
    var bytes = File.ReadAllBytes(FixturePaths.HouseInnerFile);
    var outp = AcvsDocument.Parse(bytes).ToBytes();
    Assert.Equal(bytes, outp);
}
```
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3: implement** — serialize header lines + `\n`, then JSON. Match MPC formatting: `JsonSerializerOptions { WriteIndented = true, IndentSize = 1, IndentCharacter = ' ' }` and ensure `\n` (not `\r\n`) newlines and no trailing newline/BOM. If byte-equality proves brittle (float formatting, escaping), relax the assertion to *semantic* equality (re-parse both, deep-equal the JsonNodes) and record the decision in the report; keep the writer as close to source formatting as possible.
- [ ] **Step 4:** Run → PASS (or PASS with semantic-equality fallback).
- [ ] **Checkpoint.**

> **Decision note for executor:** MPC uses `System.Text.Json`-style output but exact float rendering may differ. Prefer byte-stability; if unattainable, semantic round-trip is the acceptance bar. Document which was used.

---

## Task 4: .xpj gzip read/write

**Files:** Create `src/MpcConverter.Core/Acvs/XpjFile.cs`; Test `XpjFileTests.cs`.

**Interfaces:**
```csharp
public static class XpjFile {
    public static byte[] Decompress(byte[] gz);   // gunzip
    public static byte[] Compress(byte[] raw);     // gzip
}
```

- [ ] **Step 1: failing test** — the House `.xpj` gunzips to the inner file bytes:
```csharp
[Fact]
public void Decompress_HouseXpj_EqualsInnerFile() {
    var gz = File.ReadAllBytes(FixturePaths.HouseXpj);
    var raw = XpjFile.Decompress(gz);
    Assert.Equal(File.ReadAllBytes(FixturePaths.HouseInnerFile), raw);
}
```
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3: implement** with `System.IO.Compression.GZipStream`.
- [ ] **Step 4:** Run → PASS. (Note: we do not require our re-compressed bytes to equal AKAI's gzip; MPC reads the folder inner file primarily. We still write a valid `.xpj`.)
- [ ] **Checkpoint.**

---

## Task 5: ProjectReader

**Files:** Create `ProjectIo/MpcProject.cs`, `ProjectIo/ProjectReader.cs`; Test `ProjectReaderTests.cs`.

**Interfaces:**
```csharp
public sealed class MpcProject {
    public string Name { get; init; }
    public AcvsDocument Document { get; init; }
    public string? ProjectDataDir { get; init; }   // absolute path or null
}
public static class ProjectReader {
    public static MpcProject Open(string projectFolder); // folder containing the inner file
}
```

- [ ] **Step 1: failing test** — open House folder; name, format version, ProjectData resolves, sample count 20:
```csharp
[Fact]
public void Open_House_ResolvesInnerFileAndSamples() {
    var p = ProjectReader.Open(FixturePaths.HouseFolder);
    Assert.Equal("House os 01", p.Name);
    Assert.Equal("1.3.0.12", p.Document.FormatVersion);
    Assert.True(Directory.Exists(p.ProjectDataDir));
    Assert.Equal(20, p.Document.Root["data"]!["samples"]!.AsArray().Count);
}
```
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3: implement** — the folder's inner file is the file whose name == folder leaf name (no extension). `Name` = that leaf. ProjectData dir = sibling `"<Name>_[ProjectData]"` next to the folder (one level up), if it exists. Accept being pointed at either the project folder OR the inner file path.
- [ ] **Step 4:** Run → PASS.
- [ ] **Checkpoint.**

---

## Task 6: ProjectWriter

**Files:** Create `ProjectIo/ProjectWriter.cs`; Test `ProjectWriterTests.cs`.

**Interfaces:**
```csharp
public static class ProjectWriter {
    // Writes <destParent>/<name>/<name> (inner), <destParent>/<name>.xpj,
    // and copies referenced samples into <destParent>/<name>_[ProjectData>].
    public static string Write(MpcProject project, string destParent, string name,
                               IEnumerable<string> sampleFileNames, bool overwrite);
}
```

- [ ] **Step 1: failing test** — write House unchanged to a temp dir; re-open; equals:
```csharp
[Fact]
public void Write_ThenReopen_PreservesData() {
    var src = ProjectReader.Open(FixturePaths.HouseFolder);
    var tmp = TempDir();
    var samples = src.Document.Root["data"]!["samples"]!.AsArray()
        .Select(s => (string)s!["path"]!);
    ProjectWriter.Write(src, tmp, "House copy", samples, overwrite:true);
    var back = ProjectReader.Open(Path.Combine(tmp, "House copy"));
    Assert.Equal(20, back.Document.Root["data"]!["samples"]!.AsArray().Count);
    Assert.True(File.Exists(Path.Combine(tmp, "House copy.xpj")));
}
```
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3: implement** — create folder, write inner file (`Document.ToBytes()`), write `.xpj` (`XpjFile.Compress`), create `<name>_[ProjectData]`, copy each sample from `project.ProjectDataDir` (skip missing, collect warnings). Respect `overwrite`.
- [ ] **Step 4:** Run → PASS.
- [ ] **Checkpoint.**

---

## Task 7: PadAnalyzer

**Files:** Create `Model/PadInfo.cs`, `Analysis/PadAnalyzer.cs`; Test `PadAnalyzerTests.cs`.

**Interfaces:**
```csharp
public sealed record PadInfo(
    int PadIndex, int SourceNote, IReadOnlyList<string> SampleNames,
    int EventCount, bool HasContent);
public static class PadAnalyzer {
    // Finds the (first) type==0 Drum program; returns 128 pad slots but callers
    // typically filter HasContent. EventCount summed across ALL sequences' clips
    // for that pad's SourceNote.
    public static IReadOnlyList<PadInfo> Analyze(JsonObject data);
    public static JsonObject FindDrumProgramTrack(JsonObject data); // helper
}
```

- [ ] **Step 1: failing test** — House has 20 pads with content; pad 0 = note 36, "BDRUM12"; and total events > 0:
```csharp
[Fact]
public void Analyze_House_Finds20SampledPads() {
    var data = ProjectReader.Open(FixturePaths.HouseFolder).Document.Root["data"]!.AsObject();
    var pads = PadAnalyzer.Analyze(data).Where(p => p.HasContent).ToList();
    Assert.Equal(20, pads.Count);
    var p0 = pads.First(p => p.PadIndex == 0);
    Assert.Equal(36, p0.SourceNote);
    Assert.Contains("BDRUM12", p0.SampleNames);
    Assert.True(p0.EventCount > 0);
}
```
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3: implement** — walk `program.drum.instruments[i].layersv[]`, collect non-empty `sampleName`; SourceNote = `program.padNoteMap.noteForPad["value"+i]`; EventCount = across every `data.sequences[].value.trackClipMaps[][].value.eventList.events` where `key`==drum track name and `event.type==3` and `event.note.note==SourceNote`. HasContent = any sample present.
- [ ] **Step 4:** Run → PASS.
- [ ] **Checkpoint.**

---

## Task 8: Extract & embed 3.10 templates

**Files:** Create `Templates/*.json` (generated), `Templates/TemplateStore.cs`; Test `TemplateStoreTests.cs`. Mark JSON as `<EmbeddedResource>`.

**Interfaces:**
```csharp
public static class TemplateStore {
    public static JsonObject Get(string name); // "document","track","drumProgram",
    // "instrument","layer","clip","submixTrack","returnTrack","outTrack"
}
```

- [ ] **Step 1:** Generate templates from Keys v1 with a throwaway script: parse `keys.json`, take a Drums track / its program / instrument[0] / layersv[0] / a clip / one Submix, Return, Out track, and the top-level doc **with `tracks`, `sequences`, `songs`, `samples` emptied**. Save each as `Templates/<name>.json`. (This is a build-time asset extraction, done once; commit the JSON.)
- [ ] **Step 2: failing test**:
```csharp
[Fact]
public void Get_Instrument_IsVersion29WithNewFields() {
    var ins = TemplateStore.Get("instrument");
    Assert.Equal(29, (int)ins["version"]!);
    Assert.True(ins.ContainsKey("emulationProfile"));
    Assert.True(ins.ContainsKey("velocityScale"));
}
```
- [ ] **Step 3:** Run → FAIL.
- [ ] **Step 4: implement** — load embedded resource by name, `JsonNode.Parse`, return a **deep clone** each call (`JsonNode.DeepClone`).
- [ ] **Step 5:** Run → PASS.
- [ ] **Checkpoint.**

---

## Task 9: JsonMerge (superset field-merge)

**Files:** Create `Conversion/JsonMerge.cs`; Test `JsonMergeTests.cs`.

**Interfaces:**
```csharp
public static class JsonMerge {
    // Returns a deep clone of `template` with, for every key present in BOTH
    // template and source (recursing into objects), the source value copied in.
    // Template-only keys keep template defaults. Source-only keys are dropped.
    // Arrays: copied wholesale from source when the key exists in both (callers
    // handle array element upgrades explicitly, e.g. instruments/layers).
    public static JsonObject UpgradeOnto(JsonObject template, JsonObject source);
}
```

- [ ] **Step 1: failing tests**:
```csharp
[Fact] public void Upgrade_KeepsTemplateOnlyKeys_AndTakesSharedFromSource() {
    var tmpl = (JsonObject)JsonNode.Parse("""{"version":29,"a":1,"newField":true}""")!;
    var src  = (JsonObject)JsonNode.Parse("""{"version":28,"a":42}""")!;
    var r = JsonMerge.UpgradeOnto(tmpl, src);
    Assert.Equal(42, (int)r["a"]!);        // shared → source
    Assert.Equal(28, (int)r["version"]!);  // shared → source (caller bumps after)
    Assert.True((bool)r["newField"]!);     // template-only kept
    Assert.False(r.ContainsKey("extra"));  // source-only dropped
}
[Fact] public void Upgrade_RecursesIntoObjects() {
    var tmpl = (JsonObject)JsonNode.Parse("""{"o":{"x":0,"newX":9}}""")!;
    var src  = (JsonObject)JsonNode.Parse("""{"o":{"x":5,"gone":1}}""")!;
    var r = JsonMerge.UpgradeOnto(tmpl, src);
    Assert.Equal(5, (int)r["o"]!["x"]!);
    Assert.Equal(9, (int)r["o"]!["newX"]!);
}
```
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3: implement** the recursion. If both values are objects, recurse; else copy source value (deep clone). Version-int bumping is done by callers after merge (they know the target ints).
- [ ] **Step 4:** Run → PASS.
- [ ] **Checkpoint.**

---

## Task 10: PadTrackMap + presets

**Files:** Create `Model/PadTrackMap.cs`; Test `PadTrackMapTests.cs`.

**Interfaces:**
```csharp
public sealed class PadTrackMap {
    // Ordered destination tracks; each holds source pad indices in slot order.
    public IReadOnlyList<DestTrack> Tracks { get; }
    public static PadTrackMap OneTrackPerPad(IEnumerable<PadInfo> pads); // name = first sample
    public static PadTrackMap AllToOne(IEnumerable<PadInfo> pads, string name);
    public static PadTrackMap FromAssignments(   // pad→trackName; order preserved
        IEnumerable<PadInfo> pads, IReadOnlyDictionary<int,string?> padToTrack);
    public int SlotOf(int padIndex);   // 0-based slot within its dest track
    public string? TrackOf(int padIndex);
    public void Validate(); // throws if any track >128 slots, or zero tracks
}
public sealed record DestTrack(string Name, IReadOnlyList<int> PadIndices);
```

- [ ] **Step 1: failing tests** — one-per-pad gives N tracks each 1 slot; all-to-one gives 1 track with slots 0..N-1; combine two pads → slots 0,1; validate throws on >128.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3: implement.** Skip (`null` trackName) excludes a pad. Slot = index within the dest track's PadIndices list.
- [ ] **Step 4:** Run → PASS.
- [ ] **Checkpoint.**

---

## Task 11: ProgramBuilder (per-track drum program, renormalized)

**Files:** Create `Conversion/ProgramBuilder.cs`; Test `ProgramBuilderTests.cs`.

**Interfaces:**
```csharp
public static class ProgramBuilder {
    // Builds a 3.10 drum-program track JsonObject for one DestTrack:
    // - starts from TemplateStore "track" + "drumProgram"
    // - for each slot s with source pad p: upgrade source instrument p onto
    //   "instrument" template (+ its layersv[] onto "layer" template), bump
    //   instrument version→29, place at instruments[s]; copy the source sample
    //   into program.samples; set padNoteMap.noteForPad["value"+s] = 36+s
    // - route program → Out 1/2 (mixable.audioRoute.destination=2, channelBitmap data=3)
    // - track name = dest.Name
    public static JsonObject BuildDrumTrack(
        JsonObject sourceData, JsonObject sourceDrumProgram,
        DestTrack dest, PadInfoLookup pads);
}
```

- [ ] **Step 1: failing test** — build a single-pad track for House pad 0; assert instrument version 29, instruments[0] has BDRUM12 layer, padNoteMap value0==36, program.type==0.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3: implement.** For empty slots beyond used ones, leave template's empty instruments (or reuse a blank instrument template). Copy sample objects referenced by kept pads into `program.samples` (dedupe by path).
- [ ] **Step 4:** Run → PASS.
- [ ] **Checkpoint.**

---

## Task 12: SequenceRewriter (partition + renormalize across all sequences)

**Files:** Create `Conversion/SequenceRewriter.cs`; Test `SequenceRewriterTests.cs`.

**Interfaces:**
```csharp
public static class SequenceRewriter {
    // For one sequence, replaces its trackClipMaps with one clip per DestTrack.
    // Each dest clip = clone of the source drum clip shell (Task-8 "clip" template
    // upgraded from source clip), keyed by dest.Name, version→3, containing only
    // the events whose source note maps into this dest track, with each note
    // event's note.note rewritten to 36 + slot(srcPad). type==1 automation events
    // whose automation.note maps in are likewise remapped. Events for skipped pads
    // are dropped.
    public static void RewriteSequence(JsonObject sequenceValue, JsonObject sourceData,
        string sourceDrumTrackName, PadTrackMap map, NoteToPad noteToPad);
}
```

- [ ] **Step 1: failing test (the key invariant)** — one-track-per-pad over House: after rewriting all sequences, total `type==3` events across all dest clips == total in source; and every dest note == 36 (single-pad tracks). Also a combine test: 3 pads→1 track ⇒ notes ∈ {36,37,38}, counts preserved per pad.
```csharp
[Fact]
public void Rewrite_OneTrackPerPad_PreservesAllNoteEvents() {
    var proj = ProjectReader.Open(FixturePaths.HouseFolder);
    var data = proj.Document.Root["data"]!.AsObject();
    var pads = PadAnalyzer.Analyze(data).Where(p=>p.HasContent).ToList();
    var map = PadTrackMap.OneTrackPerPad(pads);
    int srcTotal = CountType3(data);           // helper sums across all sequences
    foreach (var seq in data["sequences"]!.AsArray())
        SequenceRewriter.RewriteSequence(seq!["value"]!.AsObject(), data,
            "Drum 001", map, NoteToPad.From(data));
    int dstTotal = CountType3(data);
    Assert.Equal(srcTotal, dstTotal);
    // all single-pad dest notes are 36
    Assert.All(AllType3Notes(data), n => Assert.Equal(36, n));
}
```
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3: implement.** Preserve `time`, velocity, length, ratchet, automation payloads; only `note.note` (and `automation.note`) change. Preserve clip length/loop/timeSignatureList from source clip via merge onto the clip template.
- [ ] **Step 4:** Run → PASS.
- [ ] **Checkpoint.**

---

## Task 13: Mixer assembly + top-level upgrade + song/arrangement remap

**Files:** Create part of `Conversion/Converter.cs` (helpers) or `Conversion/DocumentAssembler.cs`; Test `DocumentAssemblerTests.cs`.

**Interfaces:**
```csharp
public static class DocumentAssembler {
    // Mutates `data` in place to 3.10 top-level shape:
    // - data.version→30; mixer.input.version→6; add 3.10-only top-level keys from
    //   the "document" template if missing (guiNormalTracksMemento, etc.)
    // - append regenerated Submix1..N/Return1..4/Out1/2..(as in template) mixer tracks
    // - remap songs[] + each track.arrangementClipMap/sharedClipMap track-name refs
    //   from the old drum track name to the new dest track names (best-effort:
    //   point song track refs at the FIRST dest track; log if ambiguous)
    public static void FinalizeDocument(JsonObject data, IReadOnlyList<string> newTrackNames,
        ConversionReport report);
}
```

- [ ] **Step 1: failing test** — after finalize, `data.version`==30, mixer tracks include an "Out 1/2", and all 3.10-only top-level keys exist.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3: implement.** Pull the mixer track set from templates. For songs/arrangement, if the source references the old drum track by name, repoint to the first new track and note it in the report (full multi-track song remap is out of scope v1 — log the simplification).
- [ ] **Step 4:** Run → PASS.
- [ ] **Checkpoint.**

---

## Task 14: Converter orchestrator + self-check + report

**Files:** Create `Conversion/Converter.cs`, `Conversion/ConversionReport.cs`; Test `ConverterTests.cs`.

**Interfaces:**
```csharp
public sealed record ConversionReport(
    int TracksCreated, int PadsPlaced, int EventsMoved, int SamplesCopied,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> Decisions);
public static class Converter {
    // Full pipeline: clone source doc → build dest drum tracks from map →
    // rewrite all sequences → finalize document → bump AcvsDocument format version
    // to 3.10.0.23 → return a new MpcProject (in memory) + report.
    public static (MpcProject project, ConversionReport report) Convert(
        MpcProject source, PadTrackMap map);
    // Re-reads a written project and asserts invariants; throws on failure.
    public static void SelfCheck(string writtenProjectFolder, PadTrackMap map,
        int expectedType3Events);
}
```

- [ ] **Step 1: failing test (end-to-end)** — convert House one-per-pad, write to temp, SelfCheck passes, report.TracksCreated==20, event totals preserved, format version 3.10.0.23.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3: implement** by composing Tasks 9–13. Bump `AcvsDocument.FormatVersion` to `3.10.0.23` (add a `WithFormatVersion`/settable path). SelfCheck: re-open, assert format version, data.version 30, dest track count, and summed type-3 events == expected.
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5: golden-compare test** — structurally compare a converted House drum track to Keys v1's Drums track: same program.type, instrument version 29, clip version 3, presence of the 3.10-only keys, routing destination. Assert no missing template keys.
- [ ] **Checkpoint.**

---

## Task 15: RuleBasedClassifier

**Files:** Create `Classification/PadSuggestion.cs`, `Classification/IPadClassifier.cs`, `Classification/RuleBasedClassifier.cs`; Test `RuleBasedClassifierTests.cs`.

**Interfaces:**
```csharp
public sealed record PadSuggestion(int PadIndex, string TrackName, double Confidence, string? Reason);
public interface IPadClassifier {
    Task<IReadOnlyList<PadSuggestion>> SuggestAsync(IReadOnlyList<PadInfo> pads, CancellationToken ct);
}
public sealed class RuleBasedClassifier : IPadClassifier {
    public RuleBasedClassifier(IReadOnlyDictionary<string, string[]>? buckets = null);
}
```

- [ ] **Step 1: failing test** — House names classify: BDRUM12/SNARE1/HHCLOSE3→"Drums"; AfHouse-Bas→"Bass"; Synth-Deeper1→"Synth"; Keys-MKt3→"Keys"; Melodic-Orng1→"Melodic"; a nonsense name→fallback "Melodic".
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3: implement** the keyword table from the spec (§7.1), case-insensitive substring match on each sample name; first matching bucket wins; unmatched → fallback bucket ("Melodic"). Confidence: 0.9 keyword hit, 0.3 fallback.
- [ ] **Step 4:** Run → PASS.
- [ ] **Checkpoint.**

---

## Task 16: ClaudeClassifier (structured outputs, mocked in tests)

**Files:** Create `Classification/ClaudeClassifier.cs`; add Anthropic SDK to Core; Test `ClaudeClassifierTests.cs`.

> **Executor: before writing any Anthropic SDK code, read `csharp/claude-api/README.md` from the claude-api skill** for exact client init, `messages.parse`/`output_config.format` usage, and model id `claude-opus-5`. Do not guess SDK bindings; compile-fix against errors.

**Interfaces:**
```csharp
public sealed class ClaudeClassifier : IPadClassifier {
    public ClaudeClassifier(string apiKey, string model = "claude-opus-5",
        IAnthropicInvoker? invoker = null); // invoker seam for tests
}
// IAnthropicInvoker wraps the one SDK call so tests can mock it without a network.
public interface IAnthropicInvoker {
    Task<string> GetGroupingJsonAsync(string model, string prompt, CancellationToken ct);
}
```

- [ ] **Step 1: failing test** — with a fake `IAnthropicInvoker` returning a canned JSON array `[{"padIndex":0,"trackName":"Drums","confidence":0.95,"reason":"kick"}]`, `SuggestAsync` returns the parsed suggestion. And: an invoker that throws → `SuggestAsync` throws `ClassifierUnavailableException` (caller does fallback).
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3: implement** — build a prompt listing `padIndex: sampleName(s)`, ask for the grouping; parse the returned JSON into `PadSuggestion[]`. The default (non-test) `IAnthropicInvoker` uses the Anthropic .NET SDK with structured outputs and `claude-opus-5`. Keep the real SDK call in the default invoker only, so Core tests never touch the network.
- [ ] **Step 4:** Run → PASS.
- [ ] **Checkpoint.**

---

## Task 17: Settings + DPAPI key storage

**Files:** Create `Settings/AppSettings.cs`, `Settings/DpapiKeyStore.cs`; Test `DpapiKeyStoreTests.cs` (Windows-only; guard with `[SupportedOSPlatform("windows")]` and skip elsewhere).

**Interfaces:**
```csharp
public static class DpapiKeyStore {
    public static void Save(string apiKey);      // ProtectedData, CurrentUser scope
    public static string? Load();                 // null if none
    public static void Clear();
}
public sealed class AppSettings {
    public bool AiEnabled { get; set; }
    public string Model { get; set; } = "claude-opus-5";
    public string? OutputFolder { get; set; }
    public static AppSettings Load(); public void Save();  // %APPDATA%/MpcConverter/settings.json
}
```

- [ ] **Step 1: failing test** — Save→Load round-trips a secret; Clear removes it. Effective key precedence helper: `ANTHROPIC_API_KEY` env overrides stored.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3: implement** with `System.Security.Cryptography.ProtectedData` (add `System.Security.Cryptography.ProtectedData` NuGet). Store ciphertext base64 in the settings folder.
- [ ] **Step 4:** Run → PASS.
- [ ] **Checkpoint.**

---

## Task 18: WPF shell — open project + pad inventory

**Files:** `MpcConverter.App`: `App.xaml`, `MainWindow.xaml(.cs)`, `ViewModels/MainViewModel.cs`, `ViewModels/PadRowViewModel.cs`, `RelayCommand.cs`. (No unit tests for UI; correctness comes from Core.)

**Interfaces:** Consumes `ProjectReader`, `PadAnalyzer`.

- [ ] **Step 1:** MVVM plumbing: `RelayCommand`, `ObservableObject` base (or use CommunityToolkit.Mvvm — add the NuGet; it's standard and reduces boilerplate).
- [ ] **Step 2:** MainViewModel: `OpenProjectCommand` (folder picker via `OpenFolderDialog` in .NET 10 / `Microsoft.Win32.OpenFolderDialog`), loads `MpcProject`, runs `PadAnalyzer`, fills an `ObservableCollection<PadRowViewModel>` (note, sample names, event count, HasContent).
- [ ] **Step 3:** MainWindow: a `DataGrid` of pads (left), a project header (name, format version), and a disabled-until-loaded `Convert` button. Bind everything.
- [ ] **Step 4:** `dotnet build`; manual smoke via `dotnet run` documented in the report.
- [ ] **Checkpoint.**

---

## Task 19: WPF mapping grid + presets + Suggest

**Files:** `ViewModels/MappingViewModel.cs`, update `MainWindow.xaml`, `SettingsWindow.xaml(.cs)`, `ViewModels/SettingsViewModel.cs`.

**Interfaces:** Consumes `PadTrackMap`, `RuleBasedClassifier`, `ClaudeClassifier`, `AppSettings`, `DpapiKeyStore`.

- [ ] **Step 1:** Each `PadRowViewModel` gets an editable `DestTrackName` (ComboBox, editable, item source = distinct current track names). Empty/"(skip)" excludes.
- [ ] **Step 2:** Toolbar commands: `SuggestCommand`, `OneTrackPerPadCommand`, `AllToOneCommand`. Suggest picks classifier: if `AiEnabled` && key present → `ClaudeClassifier`, catch failures → fall back to `RuleBasedClassifier`, set a status line ("used offline rules"). Populate `DestTrackName` from suggestions; tooltip = reason; flag confidence < 0.5.
- [ ] **Step 3:** SettingsWindow: AiEnabled checkbox, model dropdown (`claude-opus-5`/`claude-sonnet-5`/`claude-haiku-4-5`), API key box (PasswordBox; Save → `DpapiKeyStore`), output folder picker. Persist via `AppSettings`.
- [ ] **Step 4:** build + smoke.
- [ ] **Checkpoint.**

---

## Task 20: WPF convert action + report + final build

**Files:** `ViewModels/MainViewModel.cs` (ConvertCommand), `ReportWindow.xaml(.cs)`.

**Interfaces:** Consumes `Converter`, `ProjectWriter`.

- [ ] **Step 1:** `ConvertCommand`: build `PadTrackMap.FromAssignments` from the grid, `Validate()` (surface errors in a MessageBox), `Converter.Convert`, `ProjectWriter.Write` to the chosen output folder (default `<source parent>/<name> (3.9)`), then `Converter.SelfCheck`. Copy the union of samples kept.
- [ ] **Step 2:** ReportWindow shows `ConversionReport` (tracks, pads, events moved, samples copied, warnings, decisions) + output path with an "Open folder" button.
- [ ] **Step 3:** Full `dotnet build` (all projects) and `dotnet test` (all green). Fix warnings that matter.
- [ ] **Step 4:** Write `README.md` at repo root: what it does, how to build/run (`dotnet run --project src/MpcConverter.App`), how AI/key setup works, and the known limitation (output validated structurally, not yet load-tested in MPC X 3.9). Update the spec/plan decision log.
- [ ] **Checkpoint:** everything builds and tests pass; ready for the user to commit and to load-test in MPC.

---

## Self-Review

- **Spec coverage:** §2 format → Tasks 2–6; superset merge (§2/§3) → Tasks 8,9,11; product decisions (§4) → Tasks 10–14; UI+classification (§7) → Tasks 15,16,18,19; errors/validation (§8) → Tasks 6,10,14; testing (§9) → Tasks 3,9,11,12,14; out-of-scope (§10) noted in Tasks 13. Covered.
- **Placeholder scan:** each logic task carries real test + implementation intent and exact signatures; UI tasks are structural by design (no unit tests) and say so.
- **Type consistency:** `PadInfo`, `PadTrackMap`/`DestTrack`, `PadSuggestion`, `IPadClassifier`, `ConversionReport`, `AcvsDocument`, `MpcProject` names are used consistently across tasks. `NoteToPad`/`PadInfoLookup` are small helpers introduced where first used (Tasks 11/12) — executor defines them there.

## Decision Log (append during execution)

Autonomous decisions made during the overnight build (all reviewable):

1. **Byte-exact writer (better than the planned fallback).** MPC's JSON is LF
   newlines, **zero** indentation, and relaxed escaping (`+`/`<`/`>`/UTF-8 literal).
   Reproduced exactly with `WriteIndented + IndentSize=0 + NewLine="\n" +
   UnsafeRelaxedJsonEscaping`, so the House round-trip is **byte-identical** (Task 3's
   semantic fallback was not needed). Maximizes MPC compatibility.
2. **Full mixer from Keys template.** Rather than synthesize Submix/Return/Out tracks,
   the converter appends the complete, known-valid 28-track mixer extracted from
   `Keys v1` (`Templates/mixerTracks.json`). Converted tracks route to Out 1/2, which
   exists in that set. Guarantees a valid mixer.
3. **Per-layer re-upgrade.** The plain merge copies the source `layersv` array
   wholesale (v28 schema); `ProgramBuilder.UpgradeInstrument` re-upgrades each layer
   onto the 3.10 `layer` template so the new oscillator/slice fields exist. Same for
   the instrument (version bumped 28→29 after merge).
4. **All event types handled.** Source clips carry type 1/2 (automation, note in
   `automation.note`) and type 3 (note in `note.note`). Both are routed and
   renormalized. Empty destination clips are omitted per sequence (matches MPC).
5. **Settings dir override.** `MPCCONVERTER_SETTINGS_DIR` env var isolates DPAPI/
   settings tests from the real `%APPDATA%` (production default unchanged).
6. **`.slnx`** solution format (what `dotnet new sln` produced on this SDK) instead of
   `.sln`.

**Verification performed:** 55 unit tests pass (incl. end-to-end convert → write →
self-check → structural golden-compare vs the real `Keys v1`). A real conversion of
`House os 01` was run and its bytes inspected: header `ACVS 3.10.0.23`, `data.version
30`, 48 tracks (20 drum + 28 mixer, incl. Out 1/2), instruments v29 with samples
preserved and notes renormalized to 36, valid `.xpj`, 20 samples copied, LF/zero-indent
formatting. NOT yet load-tested in MPC X 3.9 software (I don't have it) — that's the
one remaining check for the user.

**Status: all 20 tasks complete.** Nothing committed (per user instruction).
