# Encore

**Your voice. Center stage.**

Encore is a native, entirely offline karaoke app for Windows 10 and 11. It plays your local songs, follows your voice, and scores how closely you sing the melody.

## Start singing

1. Double-click the executable for your computer. Use **Encore-win-x64.exe** for most Intel and AMD PCs, **Encore-win-arm64.exe** for Windows on ARM, or **Encore-win-x86.exe** for 32-bit Windows 10.
2. Open **Studio setup**, select your microphone, and click **Test microphone**. Hum a steady note and check that a note name and input level appear. Stop the test when finished.
3. Choose a song in **Song library** and click **Sing this song**. The three included Encore Originals are ready to use. Enable the melody guide to learn their vocal melodies.
4. After a three-second countdown, follow the horizontal note bars and highlighted lyrics. Your voice draws a coral trail. Sing in whatever octave is comfortable.
5. Finish the song to see your score and save the performance to your history.

No installer, administrator privileges, internet, account, Python, Node.js, .NET installation, or separately installed audio libraries are needed. The Windows runtime and audio decoders are inside each executable. First launch extracts the practice songs into your local app data folder.

## Your songs

Use **Add song folder** to add an existing UltraStar collection. Encore scans its subfolders. You can also drop folders onto the app or create a `songs` folder next to the executable.

Each song needs a matching `.txt` chart and audio file:

```text
songs/
  Artist - Song/
    song.txt
    instrumental.mp3
    cover.jpg                 (optional)
```

The chart supplies the reference melody and syllables. An audio file on its own cannot be scored. The chart's `#MP3` or `#AUDIO` header must point to an audio file inside the song's folder. An optional `#COVER` image is displayed in the library.

WAV PCM/float, MP3, and OGG Vorbis playback use bundled code and work without installing codecs. FLAC, M4A, AAC, and WMA are also accepted when the corresponding Windows media components support them.

The included songs—**Neon Skyline**, **Golden Hour**, and **Midnight Bloom**—are original practice compositions with instrumental backing and a chart. They do not contain recorded lead vocals. The optional synthesized melody guide plays the chart's notes over the backing.

## The stage

- Lavender bars are normal notes. Golden bars count twice as much.
- Freestyle lyrics appear but earn no points.
- Singing within half a semitone earns full credit; within one semitone earns half credit.
- Silence and missed notes earn zero. The final score is normalized to **0–100** across the whole song.
- Pitch classes match across octaves, so different vocal registers receive the same credit.
- The **song score** shows points earned toward the final total. **Accuracy** shows your hit rate for the scored portions already played.
- Your combo counts consecutive successful 50 ms note slices.

Press **Space** to pause/resume, **F11** to expand the window, and **Escape** to leave the stage or close an overlay. Returning to the library ends an unfinished session; only completed performances are saved.

## Studio setup

**Background noise filter:** Defaults to 0.015 RMS. Raise it if room noise registers as your voice, or lower it slightly if a quiet microphone struggles to detect your singing.

**Voice timing adjustment:** Defaults to zero. Positive values move late microphone readings earlier relative to the chart. Adjust in 10 ms steps if your microphone or output introduces noticeable delay. Wired headphones help reduce audible delay and prevent the backing track from entering the microphone.

**Music volume:** Changes only Encore's playback volume.

**Melody guide:** Plays a quiet reference melody from the chart. Turn it off once you know the tune.

**Melody practice key:** Shifts the guide and target notes together. The instrumental audio remains in its original key, so use this as a melody practice aid.

## History and local data

Completed sessions are stored in **Your performances**, along with your personal best, average score, and combo statistics. **Export results** saves a CSV.

Settings, favorites, history, and installed practice songs live in:

```text
%LOCALAPPDATA%\Encore Karaoke\
```

Your imported audio and charts stay in their original folders. Removing a folder from Encore does not delete its files. Microphone samples are processed in memory and discarded; Encore does not record your voice or send data anywhere.

## If a song or microphone needs attention

If no microphone signal appears, check **Windows Settings → Privacy / Privacy & security → Microphone** and allow microphone access for desktop apps. Reconnect your device and click **Refresh** in Studio setup.

If a song fails to import, the app explains which chart needs attention. Check the audio filename, `#BPM`, `#GAP`, and note timing. Duet charts use the first singer's part. Legacy `#RELATIVE:YES` charts, tempo-change events, and overlapping scored notes need conversion to a single-singer chart with absolute, constant-tempo timing.

For Windows 10, version 1607 or later is the minimum target; use an updated Windows installation. Normal access to a microphone, speakers, the temporary folder, and local app data is required. The app is a desktop app and cannot run under Windows S mode restrictions. Native ARM64 builds require an ARM64 Windows installation.
