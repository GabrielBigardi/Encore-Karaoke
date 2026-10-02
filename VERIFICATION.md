# Release verification

The standalone executables were rebuilt and their core and interface checks rerun on this Windows 11 x64 computer after the maximized-window fix on October 2, 2026. The x86 executable ran under Windows' normal x86 compatibility support.

| Release | Core / codec checks | Interface checks | Native audio checks (initial release) | Result |
| --- | ---: | ---: | ---: | --- |
| win-x64 | 22 | 15 | 5 | Passed |
| win-x86 | 22 | 15 | 5 | Passed |
| win-arm64 | Cross-published | Not run on ARM hardware | Not run on ARM hardware | Build succeeded; hardware validation pending |

The x64 and x86 tests copied the executable into an isolated folder containing no supporting assemblies, disabled runtime discovery, and pointed the .NET runtime environment variables at a nonexistent folder. Both executables initialized their bundled runtime, decoded MP3/OGG, installed the original songs, rendered the interface, and passed their checks.

Native audio checks in the initial release verified device enumeration, output-clock progression, a frozen playback position while paused, accurate continuation on resume, and microphone-frame delivery through the dedicated analysis worker. Microphone samples stayed in memory and were discarded. These device checks were not rerun for the window fix; audio code is unchanged.

The interface was also rendered and inspected at 1360 × 900, 1080 × 720, and 960 × 600 logical pixels. The automated layout check verifies a usable 900 × 560 window. At smaller heights, the library hero and sidebar note disappear, the performance layout tightens, and the singing action remains visible.

Three additional interface checks reproduce the maximized-window clipping and verify the fix against native Windows client and monitor work-area rectangles. They check that the maximized library fills the visible work area, restoring preserves the previous window size, and the expanded stage's bottom controls remain visible. The library and expanded-stage checks failed before the fix and pass afterward on both x64 and x86. On this display, the available area is 1920 × 1032 physical pixels; the previous client area extended behind the taskbar. Maximized bounds now use the current monitor's work area in physical pixels.

Reports are in `artifacts/win-x64-{self-test,ui-test,audio-test}.json` and the corresponding `win-x86-*` files. Screen previews are in `artifacts/`. Run `tools/verify.ps1` to reproduce the functional checks; add `-Audio` to verify connected audio devices.

Windows 10 and physical ARM64 hardware were not available for direct testing. The builds target Windows 10 1607+ and Windows 11 with separate x64, x86, and ARM64 runtime bundles. Code signing and an installer are not included; neither is needed for the portable app's normal operation.
