# javstt — Japanese audio → subtitle, standalone

Subtitle Edit's transcription path, lifted out into a tool that does one thing: point it at films,
get `.ja.srt` files beside them.

The sibling project `srt-translator-register` did the same to whisperJAV's translation half; this is
the other half, and the UI follows its shape — header status pills, a file queue, two kinds of stop,
a log pane you can filter to problems only.

## Why it exists as its own tool

Not because the app is missing anything, but because **Fun-ASR removes the reason the app's version
is complicated.** Subtitle Edit's online STT path has to size chunks, run `ffmpeg silencedetect`,
snap cut points to silence midpoints, upload each piece, offset every timestamp back to absolute
time and retry per chunk — all of it forced by Whisper's 25 MB request cap. DashScope Fun-ASR takes
**12 hours in one upload**, so none of that exists here:

```
extract audio  →  upload once  →  poll  →  write SRT
```

Measured on the same 3h17m film: **1,264 cues, 12,358 characters, 202 seconds**, no chunk seams.

## Layout

| Project | What |
|---|---|
| `JavStt.Core` | Headless: audio extraction, transcription, SRT writing, batch queue |
| `JavStt.App` | Avalonia GUI, Nakatashi Deep Gray |
| `JavStt.Tests` | 17 tests over the core |

Nothing here touches `SubtitleEdit.sln` — `JavStt.slnx` is its own solution, so an upstream sync
never sees this directory.

## What is shared, and how

Two files are **linked, not copied**:

- `DashScopeSttService.cs` — the transcription engine. Its `BuildOssUploadForm` carries three OSS
  PostObject rules that took a live four-way probe against the bucket to establish (quoted part
  names, unquoted boundary, no `Content-Type` on the file part; .NET's defaults get all three
  backwards). A copy would fork the first time either side is touched.
- `NakatashiPalette.cs` — the colour table, so this tool and Subtitle Edit are the same grey.

Both compile outside the app because they depend on no app configuration:
`GetSettingsFromConfiguration` lives on `DashScopeQwen3SttEngine`, and the service owns its
`HttpClient`.

`LibSE` supplies `Subtitle` / `Paragraph` / `SubRip`, so the output is byte-identical to what
Subtitle Edit writes — the file goes straight back into that editor.

## Requirements

- **ffmpeg** (and `ffprobe` beside it) on PATH, or set `FfmpegPath` in the settings file
- An **Alibaba Model Studio** API key, Singapore region

## Settings

`javstt.settings.json`, beside the executable — portable, like Subtitle Edit's own. Written on exit
and when a run starts.

| Key | Default | Note |
|---|---|---|
| `Model` | `fun-asr` | The finding this tool is built on |
| `Language` | `ja` | |
| `Region` | `international` | Singapore. The other value is `china` |
| `OutputSuffix` | `ja` | `ABF-062.mp4` → `ABF-062.ja.srt` |
| `SkipExisting` | `true` | |
| `SpeakerCount` | `0` | Diarization off — measured not to separate speakers on this material |

## Two stops, on purpose

- **안전 정지** — let the film in flight finish. No half-written subtitle beside a video, which is
  the state a later run cannot tell from a complete one.
- **정지** — abandon it now, for when the answer is already wrong.

## Build

```powershell
dotnet build   src/javstt/JavStt.slnx -c Release
dotnet test    src/javstt/JavStt.slnx -c Release
dotnet publish src/javstt/JavStt.App/JavStt.App.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -o src/javstt/publish/win-x64
```

Single-file output is ~58 MB.

## What was tried and dropped

Recorded so nobody re-derives it:

- **Hotwords** (`vocabulary_id`, 500 terms, free). No measured effect: real films yield 18 / 24 / 14
  terms because that is all the glossary's series layer holds, and only 1 of 18 was spoken in the
  tested window. With and without: 90 vs 89 cues, `ご主人様` 4 vs 4.
- **Context injection** (synopsis and genre as semantic context). Fun-ASR accepts the parameter and
  discards it — proved with a control, not assumed: a deliberately wrong context (a Hokkaido farming
  programme, tractors and fertiliser) produced zero farming words. The family member that does
  support context, `fun-asr-flash-2026-06-15`, caps at 5 minutes.
- **Whisper's prompt.** Works, dramatically, on Groq — hallucination 97s → 0. Irrelevant here
  because Fun-ASR does not hallucinate in the first place.
