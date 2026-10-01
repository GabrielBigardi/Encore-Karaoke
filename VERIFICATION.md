# Release verification

The final standalone executables were built and checked on this Windows 11 x64 computer. The x86 executable ran under Windows' normal x86 compatibility support.

| Release | Core / codec checks | Interface checks | Native audio checks | Result |
| --- | ---: | ---: | ---: | --- |
| win-x64 | 22 | 12 | 5 | Passed |
| win-x86 | 22 | 12 | 5 | Passed |
| win-arm64 | Cross-published | Not run on ARM hardware | Not run on ARM hardware | Build succeeded; hardware validation pending |

The x64 and x86 tests copied the executable into an isolated folder containing no supporting assemblies, disabled runtime discovery, and pointed the .NET runtime environment variables at a nonexistent folder. Both executables initialized their bundled runtime, decoded MP3/OGG, installed the original songs, rendered the interface, and passed their checks.

Native audio checks verified device enumeration, output-clock progression, a frozen playback position while paused, accurate continuation on resume, and microphone-frame delivery through the dedicated analysis worker. Microphone samples stayed in memory and were discarded.

The interface was also rendered and inspected at 1360 × 900, 1080 × 720, and 960 × 600 logical pixels. The automated layout check verifies a usable 900 × 560 window. At smaller heights, the library hero and sidebar note disappear, the performance layout tightens, and the singing action remains visible.

Reports are in `artifacts/win-x64-{self-test,ui-test,audio-test}.json` and the corresponding `win-x86-*` files. Screen previews are in `artifacts/`. Run `tools/verify.ps1` to reproduce the functional checks; add `-Audio` to verify connected audio devices.

Windows 10 and physical ARM64 hardware were not available for direct testing. The builds target Windows 10 1607+ and Windows 11 with separate x64, x86, and ARM64 runtime bundles. Code signing and an installer are not included; neither is needed for the portable app's normal operation.
