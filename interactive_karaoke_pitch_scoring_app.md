# Project Specification: Local Desktop Pitch-Scoring Karaoke App

## 1. Project Overview

A native, standalone desktop application that runs 100% offline. The user loads local song files (audio + chart), sings into their connected microphone, sees pitch bars and lyrics scroll across the screen in real-time, and receives an accuracy score from **0 to 100** upon completion.

### Key Desktop Advantages
* **Ultra-low audio input latency:** Native audio drivers (WASAPI on Windows, CoreAudio on macOS, ALSA/JACK on Linux) avoid web-browser buffer delays.
* **Direct File System Access:** Simply point the app to a local `Songs/` folder.
* **Predictable Threading:** Dedicated OS-level audio capture and DSP thread completely separated from GUI rendering.
* **Zero Cloud/Server Costs:** Fully self-contained.

---

## 2. Recommended Tech Stack Options

Choose one of these stacks depending on your language preference:

| Stack | Language | Audio Backend | Pitch Detection | GUI / Rendering | Best For |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Option A (Fastest to Prototype)** | **Python 3.11+** | `sounddevice` / `miniaudio` | `aubio` / `numpy` (YIN) | `Pygame-CE` or `ModernGL` | Rapid development, rich DSP libraries. |
| **Option B (Best Game Polish)** | **Godot 4** (GDScript / C#) | Godot AudioEngine | C++ GDExtension (YIN/Aubio) | Godot 2D Engine | Visual polish, particle effects, UI animations. |
| **Option C (Lightweight & Native)** | **Rust** or **C++** | `cpal` / `miniaudio` | Custom YIN implementation | `egui`, `macroquad`, or `Raylib` | Maximum performance, sub-5ms input latency, tiny executable. |

---

## 3. Core Architecture & Threading Model

To avoid visual stutter or audio glitches, the application must run across **three distinct threads**:

```
+-----------------------------------------------------------------------+
|  Thread 1: Audio Capture (Real-Time OS Audio Callback)                 |
|  - Reads raw PCM frames from microphone at 44.1/48 kHz                |
|  - Pushes fixed audio buffers (e.g., 1024 samples) into thread-safe FIFO|
+-----------------------------------------------------------------------+
                                    |
                                    v
+-----------------------------------------------------------------------+
|  Thread 2: Pitch Analysis & Scoring Engine                            |
|  - Pops PCM buffers from FIFO                                         |
|  - Runs RMS Noise Gate check                                          |
|  - Runs YIN / MPM algorithm -> extracts fundamental frequency ($F_0$)  |
|  - Converts $Hz \to \text{MIDI}$ semitones                            |
|  - Matches against current target note at 50ms tick intervals         |
|  - Updates running score state                                        |
+-----------------------------------------------------------------------+
                                    |
                                    v
+-----------------------------------------------------------------------+
|  Thread 3: Main Render Loop (60 / 144 FPS)                            |
|  - Queries song playback timestamp from audio output clock            |
|  - Renders horizontal target pitch bars                               |
|  - Draws user's real-time pitch marker / particle trail               |
|  - Highlights syllables; draws combo counters & running score         |
+-----------------------------------------------------------------------+
```

---

## 4. Reference Data Format: The Song Chart

The app will parse local `.txt` chart files following the established **UltraStar** standard.

### Chart File Example (`song.txt`)
```txt
#TITLE:Don't Stop Believin'
#ARTIST:Journey
#MP3:song.mp3
#BPM:118.0
#GAP:12500
: 0 4 64 Just
: 5 4 66 a
: 10 8 68 small
: 19 6 64 town
: 26 8 63 girl
- 38
: 42 4 64 Liv-
: 46 4 66 in'
: 50 8 68 in
: 59 6 69 a
: 66 10 68 lone-
: 77 12 66 ly
: 90 14 64 world
E
```

### Parsing Rules
* `#BPM`: 4 times the song BPM (each tick = quarter-beat).
* `#GAP`: Audio start offset in milliseconds before beat `0`.
* Line syntax: `[Type] [StartBeat] [Duration] [MidiPitch] [Syllable]`
  * `:` = Standard scored note.
  * `*` = Golden note (bonus points).
  * `F` = Freestyle (lyrics displayed, not scored).
  * `-` = Line break (scrolls lyric display to next sentence).
  * `E` = End of chart.

---

## 5. Pitch Detection & Frequency Mapping

### 1. RMS Noise Gate
Before processing a buffer, check root-mean-square amplitude to ignore ambient room silence:
$$\text{RMS} = \sqrt{\frac{1}{N} \sum_{i=0}^{N-1} x[i]^2}$$
If $\text{RMS} < 0.015$, classify the frame as **unvoiced / silent**.

### 2. Fundamental Frequency ($F_0$) Extraction
* Use **YIN** or **MPM (McLeod Pitch Method)**. Avoid simple FFT peak detection, which misidentifies vocal harmonics.
* Input buffer: 1024 or 2048 samples at 44.1 kHz (~23–46 ms window).
* Output: Pitch frequency $f$ in Hertz, along with confidence score ($> 0.85$).

### 3. Hertz to MIDI Conversion
$$\text{midi} = 69 + 12 \cdot \log_2\left(\frac{f}{440}\right)$$

---

## 6. Scoring Algorithm (0–100)

### 1. Octave Equivalence
Singers naturally sing in different vocal registers (e.g., bass vs. soprano). Compare pitches across octaves using modulo 12:
$$\Delta p = \min_{k \in \{-1, 0, 1\}} \left| (\text{singer\_midi} + 12k) - \text{target\_midi} \right|$$

### 2. Hit Tolerance & Grading
For each active note tick slice ($\Delta t = 50\text{ ms}$):
* **Perfect Hit ($1.0$ pts):** $\Delta p \le 0.5$ semitones ($\pm 50 \text{ cents}$).
* **Good Hit ($0.5$ pts):** $0.5 < \Delta p \le 1.0$ semitones.
* **Miss ($0.0$ pts):** $\Delta p > 1.0$ semitone or silence/unvoiced.

### 3. Final Score Formulation
$$\text{Final Score} = \left( \frac{\sum_{t=1}^{N_{\text{total}}} S_t}{N_{\text{total}}} \right) \times 100$$
*(Where $N_{\text{total}}$ is the total number of scored note ticks in the song).*

---

## 7. Local File Management

The app expects songs stored in a simple directory structure:
```
KaraokeApp/
├── songs/
│   ├── Journey - Don't Stop Believin/
│   │   ├── song.txt
│   │   └── song.mp3 (or instrumental.ogg)
│   └── Queen - Bohemian Rhapsody/
│       ├── song.txt
│       └── song.mp3
```
On launch, the app scans the `songs/` folder, parses the `#TITLE` and `#ARTIST` headers, and presents a scrollable song-selection menu.

---

## 8. Implementation Steps for an AI Coding Assistant

1. **Step 1: Audio Playback & Clock Sync:** Create an audio player that loads an MP3/OGG file and provides a microsecond-accurate playback position.
2. **Step 2: Chart Parser:** Write a parser for UltraStar `.txt` files that outputs structured song events with exact start/end millisecond timestamps.
3. **Step 3: Microphone & Pitch Capture:** Implement the mic capture stream and YIN pitch detector. Verify it prints detected MIDI notes while humming.
4. **Step 4: Piano-Roll Visualizer:** Render horizontal bars corresponding to reference pitches and moving dots for detected vocal pitch.
5. **Step 5: Scoring Engine:** Run the tick-based comparison loop and display the 0–100 rating upon song completion.