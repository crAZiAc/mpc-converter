# MPC Sample → MPC 3.9 Converter — Design

**Date:** 2026-08-31
**Status:** Approved design, pending implementation plan
**Author:** Tom de Ridder (with Claude)

## 1. Purpose

A Windows desktop app that converts an **AKAI MPC "Sample" project** (the older
*pads-on-one-track* paradigm) into a project that **MPC X software v3.9 can open**
(the *track-based* paradigm). The core transformation splits the pads of a single
Drum program into separate tracks, driven by a user-defined, fully configurable
pad→track mapping, and applies the restructuring across **all** sequences.

The reference inputs for this work are two real projects: `House os 01` (source
format, ACVS **1.3.0.12**) and `Keys v1` (target format, ACVS **3.10.0.23**, which is
what MPC software 3.9 writes).

## 2. Background: the file format

An MPC project is a folder containing:
- an inner file with **no extension** — an `ACVS` container:
  `ACVS\n<format-version>\nSerialisableProjectData\njson\nLinux\n{ …JSON… }`
- a sibling `<name>.xpj` — a **gzip-compressed copy** of that same inner file
  (decompresses byte-for-byte identically)
- a `<name>_[ProjectData]` folder holding the `.wav` samples

Version markers observed:

| | House os 01 (source) | Keys v1 (target) |
|---|---|---|
| ACVS format version | 1.3.0.12 | 3.10.0.23 |
| `data.version` | 28 | 30 |
| Drum instrument `version` | 28 | 29 |
| Clip `version` | 2 | 3 |
| `mixer.input.version` | 5 | 6 |

### Paradigm difference

- **Source (House):** ONE musical Drum track (`program.type == 0`) holding up to
  ~20 sampled pads, plus mixer routing tracks. Pads map to MIDI notes via
  `program.padNoteMap.noteForPad` (pad 0 → note 36, pad 1 → 37, …). Every sequence
  stores all its notes in a **single clip keyed by the track name** inside
  `sequence.value.trackClipMaps`.
- **Target (Keys):** Multiple musical tracks, each with its own program and its own
  per-track clip, plus a full mixer tree (Submix/Return/Out) materialized as tracks.

### Where events live

- Active storage: `data.sequences[i].value.trackClipMaps` — a list of groups; each
  group is a list of `{ "key": <trackName>, "value": <clip> }`. Clip notes are in
  `clip.eventList.events`.
- Event shape: `type == 3` is a note (`note.note` = the pad's MIDI note — the field
  we filter and rewrite); `type == 1` is per-note automation (parameter 131). The
  note-event schema is **identical** across versions.
- The sequence-level `seqEventList.events` array is **empty** in both files — ignore it.
- Pad samples live at
  `program.drum.instruments[padIdx].layersv[layer].sampleName` / `sampleFile`.

### Critical finding — the 3.10 schema is a strict superset of 1.3

No source field disappears in the newer format; the newer format only **adds** fields:
- Drum program adds `samples`
- Instrument (v28→29) adds: `emulationProfile`, `layerCrossfade`, `layerCrossfadeX`,
  `layerCrossfadeY`, `velocityScale`, `randomPlaySeed`, `partialPresetName`
- Layer adds: `OscillatorType`, `oscillatorMode`, `oscillatorParams`,
  `oscillatorSubTypeName`, `sliceIncrement`, `sliceCycleLength`,
  `sliceIncrementRngSeed`, `quadrantEnabled`, `playbackOffset`

This makes conversion a **well-defined additive upgrade** (Approach C, below) rather
than a lossy guess.

## 3. Conversion strategy (Approach C — superset field-merge)

Bundle small **3.10 default templates**, extracted once from `Keys v1` and stripped of
musical content, for each element type: document top-level, track, drum program,
instrument, layer, clip, and the mixer tracks. Conversion overlays source values onto
these templates:

> For each element: start from the 3.10 template's default object, then for every key
> that exists in **both** template and source, copy the source value; leave
> template-only (new 3.10) keys at their defaults; bump version integers to their 3.10
> values.

Because 3.10 ⊇ 1.3, this is deterministic: known source values are preserved, new
fields get real 3.10 defaults, nothing is invented. The pad→track split and
note-renormalization are a restructuring pass layered on top.

Rejected alternatives:
- **A. Mutate-and-bump** (rewrite the version string only): leaves instruments at v28
  missing the new fields → MPC 3.9 may reject/misread. Rejected.
- **B. Template injection** (build blank 3.10 and inject content): robust but rebuilds
  scaffolding by hand and discards rich source detail. Rejected in favor of C.

## 4. Confirmed product decisions

- **Split target:** fully configurable pad→track map (destination track + destination
  pad slot per source pad). "One track per pad" is a preset; many-to-one entries
  implement the "combine pads into a single track" option.
- **Note mapping:** renormalize each source pad to its **destination pad slot's**
  canonical note (slot 0 → note 36, slot 1 → 37, …). Within a combine-group each
  source pad gets its own slot, so notes never collide. `note.note` is rewritten in
  every sequence's clip.
- **Empty pads** (no sample) are dropped from output but still shown in the UI.
- **Output:** a full new MPC project folder — `<name>` inner ACVS file + gzipped
  `<name>.xpj` + a copied `<name>_[ProjectData]` folder of the `.wav` samples. Source
  project is never modified.
- **Mixer routing:** every converted track routes to **Out 1/2**. No attempt to
  reproduce complex submix trees; regenerate Submix/Return/Out tracks from template.
- **Scope handled:** drum pads → tracks; preserve songs and arrangement clip maps.
  Keygroup/MIDI/plugin programs are out of scope (source has none).
- **AI classification:** opt-in; rule-based suggestions are the default and the
  offline fallback. Classifier model `claude-opus-5`, switchable in Settings.
- **Git:** the tooling never commits; the user commits themselves.

## 5. Architecture

WPF app (MVVM) + a pure-logic core library so the engine is testable without UI.

```
mpc-converter.sln
├─ src/
│  ├─ MpcConverter.Core/          no WPF; all conversion logic
│  │   ├─ Acvs/                   container read/write + gzip (.xpj)
│  │   ├─ Model/                  minimal typed model over JsonNode
│  │   ├─ Templates/              embedded 3.10 default JSON fragments
│  │   ├─ Conversion/             merge engine, splitter, renormalizer
│  │   ├─ Classification/         IPadClassifier + Rule/Claude impls
│  │   └─ ProjectIo/              read/write project folder + [ProjectData]
│  └─ MpcConverter.App/           WPF: open → map pads → convert
└─ tests/
   └─ MpcConverter.Core.Tests/    xUnit; House/Keys as fixtures
```

Design principles:
- **Minimal typed model.** Give real C# properties only to what we manipulate
  (tracks, programs, pads/instruments, sequences, clips, note events, sample refs).
  Everything else rides along as raw `System.Text.Json.Nodes.JsonNode`, so unknown
  fields are never dropped and we don't model 60+ top-level keys.
- **Templates are embedded resources**, generated once from `Keys v1`.
- **Core has no UI and no hard network dependency**; the Claude classifier is behind an
  interface and optional.

## 6. Data flow

```
Open project folder
  └─ AcvsReader: validate header, strip it → JsonNode (source doc)
  └─ ProjectReader: locate <name>_[ProjectData]

Analyze → pad inventory for the UI
  └─ find Drum program(s); walk drum.instruments[i].layersv[] for sampleName/File
  └─ scan all sequences' clips → per-note event counts (flag empty pads)
  └─ emit PadInfo { padIndex, sourceNote, sampleNames, eventCount, hasContent }

User defines pad→track map (UI); optional Suggest (rules or Claude)

Convert (pure, deterministic)
  1. Upgrade  — every element = 3.10 template ← overlay source values; bump versions
  2. Restructure — per destination track: build a Drum program containing only its
                   pad(s) at canonical slots (renormalize); route → Out 1/2
  3. Sequences — for EACH sequence: partition the source Drum clip's events by source
                 note → destination (track, slot); rewrite note.note to the slot's
                 canonical note; emit one clip per destination track, re-keyed
  4. Preserve  — remap songs/arrangement clip maps to new track names; add 3.10
                 top-level fields (mementos, data.version 30)
  5. Mixer     — regenerate Submix/Return/Out tracks from template; all → Out 1/2

Write
  └─ ProjectWriter: new <name> folder → ACVS inner file + gzipped .xpj +
     copied <name>_[ProjectData]
  └─ Post-convert self-check (see §8)
```

## 7. The mapping UI

Left = source pad inventory; right = destination tracks; toolbar of presets.

- Toolbar: `Suggest from names`, `One track per pad`, `All → one track`.
- Each pad row: MIDI note, sample name(s), event count, and an **editable destination
  track** dropdown (type a new name to create a track). Same name on several pads =
  combine-group; distinct names = full split. Empty pads default to "skip".
- Destination panel shows each track's pads in canonical slot order.
- `Convert →` runs the pipeline and shows the conversion report.

### 7.1 Classification

```csharp
interface IPadClassifier {
    Task<IReadOnlyList<PadSuggestion>> SuggestAsync(
        IReadOnlyList<PadInfo> pads, CancellationToken ct);
}
// PadSuggestion { int PadIndex, string TrackName, double Confidence, string? Reason }
```

- **RuleBasedClassifier** — keyword table (editable data in Core, not hardcoded in UI):
  - Drums: `kick, bdrum, snare, hh, hat, clap, rim, crash, ride, tom, perc, break, clhat`
  - Bass: `bass, bas, sub, 808`
  - Keys: `key, ep, rhodes, piano, organ, mkt`
  - Synth/Lead: `synth, lead, pad, arp, saw, retro, magic, deeper`
  - Melodic/Other: `melodic, orng, chord, string, violin, brass` (fallback bucket)
  - Unmatched → fallback bucket (never silently skipped).
- **ClaudeClassifier** — sends only pad **sample names** (never audio) to Claude via the
  official Anthropic .NET SDK, using **structured outputs** (`output_config.format` /
  `messages.parse`) to return a validated JSON array of
  `{padIndex, trackName, confidence, reason}`. One request per conversion. Default
  model `claude-opus-5`, switchable. Off unless a key is available.
- **Selection:** opt-in. Default = rules. When AI is enabled and a key is present,
  `Suggest` uses Claude and **falls back to rules on any failure** (offline, bad key,
  rate limit), showing a small "used offline rules" note. Suggestions populate the same
  editable grid; `reason` is a tooltip; low-confidence rows are flagged.
- **Key handling:** read `ANTHROPIC_API_KEY` from the environment, or a key entered in
  Settings, stored encrypted via Windows **DPAPI** (never plaintext, never in git).

## 8. Error handling & validation

- **Read guards:** validate ACVS header; confirm a Drum program exists; confirm
  `[ProjectData]` resolves. If the source is already 3.x, warn ("nothing to convert").
- **Mapping guards (pre-Convert):** block if a destination track exceeds 128 pad slots,
  or if every pad is "skip". Warn on pad-referenced samples missing on disk.
- **Non-destructive:** Convert builds a new document in memory and writes only to a new
  output folder. If the target exists, prompt before overwriting.
- **Post-convert self-check:** re-read the written file; assert it parses; ACVS version
  == 3.10.0.23 and `data.version` == 30; track/clip counts match the map; every note
  event resolves to a real pad slot. Emit a conversion report (tracks created, pads
  placed, events moved, samples copied).
- **AI failures never block** — fall back to rules, note it, continue.

## 9. Testing

xUnit against Core, with `House os 01` and `Keys v1` copied in as fixtures.

- **Round-trip:** read House → write → re-read is an equivalent document (I/O lossless).
- **Superset-merge:** an upgraded instrument/layer has version 29 and all new 3.10
  fields at template defaults, while every source value is preserved.
- **Split correctness:** one-track-per-pad on House yields N tracks, each with one
  canonical pad at note 36; total note-event count across all output clips equals the
  source total across all 21 sequences (nothing dropped/duplicated).
- **Combine correctness:** merging 3 pads onto one track places them at slots 0/1/2
  (notes 36/37/38), events remapped, no collisions.
- **Golden compare:** structurally diff a converted House Drums track against the Keys
  Drums track to catch schema drift (keys present, versions, routing).
- **Classifier:** rule-based table has deterministic unit tests; ClaudeClassifier is
  tested against a mocked SDK response (no live API in CI).

## 10. Out of scope (v1)

- Keygroup / MIDI / plugin program conversion.
- Reproducing arbitrary submix/return routing (everything routes to Out 1/2).
- Audio analysis for classification (names only).
- Editing the resulting project beyond the described transformation.
