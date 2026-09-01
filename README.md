# MPC Sample → MPC 3.9 Converter

A Windows desktop app (WPF, .NET 10) that converts an **AKAI MPC "Sample" project**
(the older *pads-on-one-track* paradigm, ACVS format 1.3.x) into a project that
**MPC X software v3.9 can open** (the track-based paradigm, ACVS format 3.10.0.23).

It splits the pads of a single Drum program into separate tracks — driven by a
fully configurable pad→track mapping — and applies the restructuring across **all**
sequences. Pads can also be **combined** onto shared tracks.

## What it does

- Reads an MPC project folder (the `ACVS` container + its `_[ProjectData]` samples).
- Analyzes the Drum program's sampled pads (name, MIDI note, event count).
- Lets you map each pad to a destination track:
  - **One track per pad** (preset)
  - **All → one track** (preset)
  - **Suggest from names** — rule-based grouping (offline), or Claude-assisted
    (opt-in) when an API key is configured
  - Free editing of any assignment in the grid
- Converts by **superset field-merge**: every element is built from an embedded
  3.10 default template with the source's values overlaid, so the output validates
  as 3.10 while preserving all the original per-pad detail (filters, envelopes,
  tuning, sample start/end, velocity, ratchet, automation).
- Renormalizes each destination pad to note `36 + slot`, rewriting note events in
  every sequence.
- Routes every converted track to **Out 1/2** and regenerates the standard mixer.
- Writes a **new** project folder (`<name> (3.9)`) — the source is never modified —
  containing the inner ACVS file, a gzipped `.xpj`, and a copied `_[ProjectData]`
  sample folder.
- Runs a **self-check** on the written project (format version, event counts, note
  ranges) and shows a conversion report.

## Build & run

Requires the .NET 10 SDK.

```bash
dotnet build mpc-converter.slnx
dotnet run --project src/MpcConverter.App
```

Run the tests:

```bash
dotnet test mpc-converter.slnx
```

## AI classification (optional)

AI grouping is **off by default**; the app works fully offline with rule-based
suggestions. To enable it, open **Settings**, tick *Enable AI-assisted pad
classification*, pick a model (default `claude-opus-5`), and provide a Claude API
key. The key is read from the `ANTHROPIC_API_KEY` environment variable if set,
otherwise from a key you enter in Settings (stored encrypted at rest via Windows
DPAPI — never in plaintext or in git). Only pad **sample names** are ever sent to
the API; never audio. Any API failure falls back to the offline rules automatically.

## Project layout

```
src/MpcConverter.Core/     conversion engine (no WPF, fully unit-tested)
  Acvs/                    ACVS container + .xpj gzip I/O
  Model/                   PadInfo, PadTrackMap, MPC JSON helpers
  Analysis/                pad inventory
  Templates/               embedded 3.10 default fragments + loader
  Conversion/              merge, program builder, sequence rewriter, orchestrator
  Classification/          rule-based + Claude classifiers
  Settings/                app settings + DPAPI key store
src/MpcConverter.App/      WPF MVVM UI
tests/MpcConverter.Core.Tests/   xUnit tests (use the reference projects as fixtures)
docs/superpowers/          design spec + implementation plan
```

## Known limitations

- **Not yet load-tested in MPC X 3.9 hardware/software.** Correctness is proven by
  structural golden-comparison against a real 3.9 project (`Keys v1`), byte-stable
  round-trips, and event-count invariants — but you should do the final load test in
  MPC and report back anything it rejects.
- Keygroup / MIDI / plugin programs are out of scope (the converter targets Drum
  programs). Mixer routing is simplified: all tracks route to Out 1/2.
- Song/arrangement remap is best-effort; multi-track song arrangements are not
  rewritten (the reference source projects have empty songs).
