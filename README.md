# Encore Karaoke

A complete native Windows karaoke application built from `interactive_karaoke_pitch_scoring_app.md`. The WPF interface uses custom vector artwork, a dark studio palette, responsive layouts, a scrolling piano roll, and timed lyric highlighting. Playback, capture, pitch analysis, and rendering are separated.

**Run:** Double-click `dist/Encore-win-x64.exe` on most PCs. Also included are standalone x86 and ARM64 builds. All runtime libraries, managed MP3/OGG decoders, and three original practice songs are embedded. There is no end-user setup or network access.

See [USER_GUIDE.md](USER_GUIDE.md) for instructions, import formats, studio controls, and scoring.

## Features

- Recursive UltraStar song library, search, sort, favorites, cover art, and folder drag/drop.
- Native WASAPI output clock and capture, with default or selected microphone.
- Event-driven 48 kHz mono capture into a bounded FIFO; dedicated analysis worker.
- 2,048-sample YIN pitch estimation every 960 samples, RMS gating, and 0.85 confidence threshold.
- Octave-equivalent pitch grades, 50 ms score slices, normalized 0–100 score, and double-weight golden notes.
- Freestyle lyrics, syllable highlighting, live voice trail, combo, and accuracy.
- Note scrolling follows the Windows compositor's display refresh cadence, including 144 Hz screens.
- Three-second countdown, clock-preserving pause/resume, expanded stage mode.
- Microphone tuner, input levels, noise filter, timing adjustment, volume, reference melody, and melody transposition.
- Completed session history, best scores, CSV export, and local JSON settings.
- Read-only imports and in-app errors for missing files, malformed charts, and microphone failures.

## Build

Development needs a .NET SDK able to target .NET 8 with Windows Desktop support, on Windows, and NuGet access for the first restore. No SDK is needed to run the releases.

```powershell
powershell -ExecutionPolicy Bypass -File tools/build.ps1
```

This publishes self-contained, compressed, single-file releases for `win-x64`, `win-x86`, and `win-arm64` into `dist/`. Native WPF runtime libraries extract automatically to Windows' temporary directory. Subsequent offline builds can use `-SkipRestore`.

```powershell
powershell -ExecutionPolicy Bypass -File tools/build.ps1 -Runtime win-x64 -SkipRestore
```

`tools/generate_demo_songs.py` deterministically regenerates the original WAV tracks, charts, and app icon. That optional asset-generation tool needs Python, NumPy, and Pillow; the finished app does not.

## Verification

The executable includes developer diagnostics that exit after writing a JSON report. Use separate test data so synthetic test results cannot affect real history.

The release verification script copies the executable into an isolated folder, disables runtime discovery, and runs its checks:

```powershell
powershell -ExecutionPolicy Bypass -File tools/verify.ps1 -Runtime win-x64
powershell -ExecutionPolicy Bypass -File tools/verify.ps1 -Runtime win-x86
```

Add `-Audio` to also check connected speakers and microphone. The app fits the current display on startup and adapts down to a 900 × 560 logical-pixel window.

Add `-Rendering` to check frame delivery with silent playback. For an actual display-rate measurement, use `-VisibleRendering`: it briefly shows the stage and reports composition, note-update, and drawn-frame rates. Windows throttles hidden or occluded windows, so a hidden rendering test cannot measure the monitor's full refresh rate.

```powershell
Encore-win-x64.exe --self-test --report="C:\path\self-test.json"
Encore-win-x64.exe --ui-test --data-dir="C:\path\test-data" --report="C:\path\ui-test.json"
Encore-win-x64.exe --audio-test --data-dir="C:\path\audio-test-data" --report="C:\path\audio-test.json"
Encore-win-x64.exe --render-test --render-visible --data-dir="C:\path\render-test-data" --report="C:\path\render-test.json"
Encore-win-x64.exe --screenshot="C:\path\library.png" --data-dir="C:\path\preview-data"
Encore-win-x64.exe --screenshot="C:\path\stage.png" --view=stage --data-dir="C:\path\preview-data"
```

The audio test briefly plays at low volume and processes under a second of microphone samples without saving them. It checks native device enumeration, the output clock, pause/resume, and capture/DSP delivery. The stage screenshot mode shows a labeled chart preview with no simulated voice or score.

Core diagnostics exercise quarter-beat timing, GAP, chart validation, lyric preservation, golden/freestyle/duet handling, octave mapping, harmonic pitch detection, silence/noise rejection, score boundaries, full-song normalization, stale readings, short notes, pause-safe tick accounting, combos, persistence, bundled demos, and MP3/OGG decoding and precise seeking. UI diagnostics exercise selection, search, favorites, sorting, settings, results/history, expansion, minimum window dimensions, and delivery of every distinct frame at simulated 60, 144, and 240 Hz.

## Architecture

```text
src/Encore.Core/       Models, UltraStar parser, YIN, pitch mapping, score engine
src/Encore/Services/   WASAPI playback/capture, bounded audio FIFO, local storage
src/Encore/Controls/   Custom vector artwork and piano-roll renderer
src/Encore/Diagnostics/ Repeatable functional and native-audio checks
src/Encore/           Native WPF shell, views, and session lifecycle
songs/                Original demo charts and instrumentals (embedded at build)
tools/                Asset generation and standalone release packaging
```

Timing uses `GAP + beat × 60,000 / (4 × BPM)`, matching the [UltraStar format](https://github.com/UltraStar-Deluxe/format). Silence and unavailable samples count as misses; scored duration, including partial slices, determines the denominator. Golden notes have twice the numerator and denominator weight. Octave mapping uses a circular pitch-class distance for all registers.

The output position uses WASAPI's hardware clock rather than the decoder read position or a wall timer. Playback is recreated at the exact decoded position on resume. Pitch windows are timestamped at their center, then adjusted by the user's latency setting. The note display and lyrics update on each distinct [WPF composition frame](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/how-to-render-on-a-per-frame-interval-using-compositiontarget), without a fixed frame-rate cap. Pitch ranges, formatted text, and frozen drawing resources are cached, while score labels and meters update less frequently. The capture callback performs no DSP. No arbitrary sub-5 ms end-to-end latency is promised: actual latency depends on Windows audio drivers and the microphone's 43 ms analysis window.

Deployment follows Microsoft's [self-contained single-file publishing](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview). The target OS is Windows 10 1607+ or Windows 11; see the user guide for operational requirements. Native x64 and x86 are testable on this development machine; ARM64 requires separate ARM hardware validation. Code signing is not included.
