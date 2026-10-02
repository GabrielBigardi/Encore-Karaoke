# Release verification

The standalone executables were rebuilt and their core, interface, native audio, playback stress, and rendering checks rerun on this Windows 11 x64 computer after the pause/resume fixes on October 2, 2026. The x86 executable ran under Windows' normal x86 compatibility support.

| Release | Core / codec | Interface | Native audio | Playback stress | Rendering | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| win-x64 | 24 | 17 | 5 | 6 | 3 | All 55 passed |
| win-x86 | 24 | 17 | 5 | 6 | 3 | All 55 passed |
| win-arm64 | Cross-published | Not run | Not run | Not run | Not run | Build succeeded; ARM hardware validation pending |

The x64 and x86 tests copied the executable into an isolated folder containing no supporting assemblies, disabled runtime discovery, and pointed the .NET runtime environment variables at a nonexistent folder. Both executables initialized their bundled runtime, decoded MP3/OGG, installed the original songs, rendered the interface, and passed their checks.

Native audio checks verified device enumeration, output-clock progression, a frozen playback position while paused, accurate continuation on resume, and microphone-frame delivery through the dedicated analysis worker. Microphone samples stayed in memory and were discarded. The rendering diagnostic also exercises the native playback clock with silent playback and no microphone capture.

Playback stress checks perform 80 rapid pause/resume cycles for each of WAV, stereo 44.1 kHz MP3, and mono 48 kHz OGG while a separate task polls the playback clock. They verify no playback error or premature completion, monotonic positions, and a frozen clock during pauses. Four longer pauses check that resuming excludes paused time. Another 120 native pause/resume cycles use a counted stereo 44.1 kHz source with a partial final buffer, verifying that decoding stops while paused, every source frame is consumed, the final output-clock duration matches the decoded audio, and disposal stops all decoder reads. A short OGG track verifies natural completion after the final buffer drains. Each executable completes 360 rapid cycles across these tests.

Playback now retains one native WASAPI stream, decoder, resampler, and endpoint queue for the performance. Resume does not seek the compressed decoder or recreate the output. Core regressions reject unavailable, stale, and future audio-clock timestamps. The interface check sends synthetic WPF Spacebar events through the stage's normal keyboard handler and verifies that auto-repeat pauses once and a separate press resumes.

The interface was also rendered and inspected at 1360 × 900, 1080 × 720, and 960 × 600 logical pixels. The automated layout check verifies a usable 900 × 560 window. At smaller heights, the library hero and sidebar note disappear, the performance layout tightens, and the singing action remains visible.

Three additional interface checks reproduce the maximized-window clipping and verify the fix against native Windows client and monitor work-area rectangles. They check that the maximized library fills the visible work area, restoring preserves the previous window size, and the expanded stage's bottom controls remain visible. The library and expanded-stage checks failed before the fix and pass afterward on both x64 and x86. On this display, the available area is 1920 × 1032 physical pixels; the previous client area extended behind the taskbar. Maximized bounds now use the current monitor's work area in physical pixels.

The refresh-rate regression check delivers simulated 60, 144, and 240 Hz composition frames through the application's normal render path and verifies that every distinct frame reaches the note display while duplicate timestamps do not add updates. The fixed 15 ms render throttle has been removed. Repeated text, pitch-range calculations, brushes, and pens are cached to reduce drawing allocations.

A visible x64 stage diagnostic measured 432 note updates and 432 draws across about three seconds on the connected 144 Hz NVIDIA display: approximately **144.01 drawn frames per second**. Three rendering checks passed. The measurement uses the normal render loop and real WASAPI playback clock, with music volume zero and no microphone capture. It counts WPF drawing callbacks, rather than GPU presentation timestamps. Hidden rendering checks verify frame delivery but run at Windows' throttled background cadence.

Reports are in `artifacts/win-x64-{self-test,ui-test,audio-test,playback-test}.json` and the corresponding `win-x86-*` files. Screen previews are in `artifacts/`. Run `tools/verify.ps1 -Runtime win-x64 -Audio -Playback -Rendering` to reproduce all 55 checks; repeat with `-Runtime win-x86` for that executable.

Rendering reports are `artifacts/win-x64-render-test.json`, `artifacts/win-x86-render-test.json`, and `artifacts/win-x64-render-visible.json`. Use `tools/verify.ps1 -Rendering` for hidden rendering checks or `-VisibleRendering` for a brief visible stage measurement. The latter writes the measurement to the selected runtime's `render-test.json` report.

Windows 10 and physical ARM64 hardware were not available for direct testing. The builds target Windows 10 1607+ and Windows 11 with separate x64, x86, and ARM64 runtime bundles. Code signing and an installer are not included; neither is needed for the portable app's normal operation.
