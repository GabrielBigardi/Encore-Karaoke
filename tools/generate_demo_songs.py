"""Original Encore practice instrumentals; no samples or copyrighted compositions."""
from pathlib import Path
import math
import wave
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
RATE = 22050
SONGS = [
    ("Neon Skyline", "Synth pop", 108, [64, 67, 69, 67, 64, 62, 64, 67],
     ["Under the city lights ", "We let our voices rise ", "Carry the night away ", "Sing till a brand new day "]),
    ("Golden Hour", "Dream pop", 92, [60, 64, 67, 69, 67, 64, 62, 60],
     ["Sunlight upon the sea ", "A little room to breathe ", "Every note feels like home ", "Here we are never alone "]),
    ("Midnight Bloom", "Indie electronic", 120, [62, 65, 69, 72, 69, 67, 65, 62],
     ["Stars in the velvet blue ", "All of the light is you ", "Follow the sound we make ", "Let every dream awake "]),
]

def hz(midi): return 440 * 2 ** ((midi - 69) / 12)

def create_song(title, genre, bpm, melody, sentences):
    directory = ROOT / "songs" / title
    directory.mkdir(parents=True, exist_ok=True)
    tick = 60 / (bpm * 4)
    gap = 3.0
    notes = []
    lines = []
    for li, sentence in enumerate(sentences):
        words = sentence.split(" ")[:-1]
        # Split words into exactly eight syllables while preserving natural spaces.
        while len(words) < 8:
            index = max(range(len(words)), key=lambda i: len(words[i]))
            word = words[index]
            split = max(1, len(word) // 2)
            words[index:index+1] = [word[:split] + "~", word[split:]]
        while len(words) > 8: words[-2:] = [words[-2] + " " + words[-1]]
        for ni, word in enumerate(words):
            beat = li * 64 + ni * 7
            duration = 6 if ni < 7 else 9
            pitch = melody[ni]
            lyric = word.replace("~", "") + ("" if word.endswith("~") else " ")
            kind = "*" if ni == 7 and li in (1, 3) else ":"
            notes.append((gap + beat*tick, duration*tick, pitch))
            lines.append(f"{kind} {beat} {duration} {pitch} {lyric}")
        lines.append(f"- {li*64+61}")
    duration = gap + 256*tick + 2
    count = math.ceil(duration*RATE)
    t = np.arange(count, dtype=np.float64)/RATE
    audio = np.zeros(count)
    rng = np.random.default_rng(417)
    progression = [[48, 52, 55], [45, 48, 52], [41, 45, 48], [43, 47, 50]]
    if title == "Midnight Bloom": progression = [[50, 53, 57], [46, 50, 53], [48, 52, 55], [45, 49, 52]]
    beat_seconds = 60/bpm
    for bar_start in np.arange(0, duration, 4*beat_seconds):
        chord = progression[int(bar_start/(4*beat_seconds))%4]
        end = min(count, int((bar_start+4*beat_seconds)*RATE))
        start = int(bar_start*RATE)
        local = t[start:end]-bar_start
        envelope = np.minimum(local/.15, 1) * np.minimum((4*beat_seconds-local)/.2, 1)
        for note in chord:
            audio[start:end] += .042 * np.sin(2*np.pi*hz(note+12)*local) * envelope
        for step in range(8):
            offset = bar_start+step*beat_seconds/2
            a, b = int(offset*RATE), min(count, int((offset+.35)*RATE))
            if b <= a: continue
            local = t[a:b]-offset
            pitch = chord[step%3]+24
            audio[a:b] += .055*np.sin(2*np.pi*hz(pitch)*local)*np.exp(-local*12)
        for step in range(4):
            offset = bar_start+step*beat_seconds
            a, b = int(offset*RATE), min(count, int((offset+.35)*RATE))
            if b <= a: continue
            local = t[a:b]-offset
            audio[a:b] += .10*np.sin(2*np.pi*(52*local+2*(1-np.exp(-local*25))))*np.exp(-local*18)
            if step%2:
                audio[a:b] += .040*rng.normal(0, 1, b-a)*np.exp(-local*35)
        for step in range(8):
            offset=bar_start+step*beat_seconds/2
            a,b=int(offset*RATE),min(count,int((offset+.065)*RATE))
            if b>a:
                local=t[a:b]-offset
                audio[a:b] += .016*rng.normal(0,1,b-a)*np.exp(-local*80)
    audio *= np.minimum(t/.5, 1)*np.clip((duration-t)/1.5, 0, 1)
    samples = (np.clip(audio, -.95, .95)*32767).astype("<i2")
    with wave.open(str(directory/"instrumental.wav"), "wb") as output:
        output.setnchannels(1); output.setsampwidth(2); output.setframerate(RATE)
        output.writeframes(samples.tobytes())
    chart = f"#TITLE:{title}\n#ARTIST:Encore Originals\n#MP3:instrumental.wav\n#BPM:{bpm}\n#GAP:3000\n#GENRE:{genre}\n#LANGUAGE:English\n"+"\n".join(lines)+"\nE\n"
    (directory/"song.txt").write_text(chart, encoding="utf-8")
    print(f"{title}: {duration:.1f}s, {len(notes)} notes")

def icon():
    im = Image.new("RGBA", (256,256), (19,19,26,255)); d = ImageDraw.Draw(im)
    for x in range(256):
        u=x/255
        d.line([(x,0),(x,255)], fill=(int(238+17*u),int(89+49*u),int(142-33*u),255))
    for x,h in [(64,55),(92,102),(120,146),(148,102),(176,55)]:
        d.rounded_rectangle((x,128-h/2,x+15,128+h/2),radius=8,fill=(255,255,255,255))
    path=ROOT/"src"/"Encore"/"Assets";path.mkdir(parents=True,exist_ok=True)
    im.save(path/"encore.ico",sizes=[(16,16),(32,32),(48,48),(64,64),(128,128),(256,256)])

if __name__ == "__main__":
    for song in SONGS: create_song(*song)
    icon()
