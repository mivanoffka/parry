import array
import hashlib
import math
from pathlib import Path
import random
import sys
import wave


SAMPLE_RATE = 44100
OUTPUT = Path(__file__).resolve().parents[1] / "Assets/Resources/Audio/Combat"
DURATIONS = {"Swing": .2, "Hit": .19, "Block": .25, "Parry": .55,
             "Roll": .32, "Break": .55, "Death": .48, "Revive": .55}


def synthesize(name, duration):
    noise = random.Random(name)
    samples = []
    filtered = 0
    for index in range(round(duration * SAMPLE_RATE)):
        time = index / SAMPLE_RATE
        progress = time / duration
        white = noise.uniform(-1, 1)
        filtered += .12 * (white - filtered)
        attack = min(1, time / .004)
        release = min(1, (duration - time) / .025)
        if name == "Swing":
            value = (white - filtered) * math.sin(math.pi * progress) ** 1.6 * .55
        elif name == "Roll":
            value = (filtered * 1.8 + white * .08) * math.sin(math.pi * progress) ** .7
        elif name in ("Block", "Parry"):
            frequencies = (620, 1130, 1870, 3100) if name == "Block" else (1150, 1870, 2930, 4370)
            value = sum(math.sin(math.tau * frequency * time) * math.exp(-time * (7 + partial * 3))
                        / (partial + 2) for partial, frequency in enumerate(frequencies))
            value += white * .35 * math.exp(-time * 65)
        elif name == "Hit":
            value = math.sin(math.tau * (105 * time - 90 * time * time)) * math.exp(-time * 28)
            value += (filtered * 2 + white * .25) * math.exp(-time * 38)
        elif name == "Death":
            value = math.sin(math.tau * (85 * time - 55 * time * time)) * math.exp(-time * 11)
            value += filtered * 1.6 * math.exp(-time * 8)
        elif name == "Break":
            value = sum(math.sin(math.tau * frequency * time) * math.exp(-time * (10 + partial * 2))
                        / (partial + 3) for partial, frequency in enumerate((340, 610, 970, 1620)))
            value += (white - filtered) * .65 * math.exp(-time * 16) * (.65 + .35 * math.sin(time * 230) ** 2)
        else:
            value = sum(math.sin(math.tau * frequency * time) * math.exp(-time * 6)
                        / (partial + 2) for partial, frequency in enumerate((660, 880, 1320)))
        samples.append(value * attack * release)
    peak = max(abs(value) for value in samples)
    return [value * .8 / peak for value in samples]


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for name, duration in DURATIONS.items():
        samples = synthesize(name, duration)
        pcm = array.array("h", (round(value * 32767) for value in samples))
        if sys.byteorder != "little":
            pcm.byteswap()
        path = OUTPUT / (name + ".wav")
        with wave.open(str(path), "wb") as clip:
            clip.setnchannels(1)
            clip.setsampwidth(2)
            clip.setframerate(SAMPLE_RATE)
            clip.writeframes(pcm.tobytes())
        meta = path.with_suffix(".wav.meta")
        if not meta.exists():
            guid = hashlib.md5(("parry-combat-audio-" + name).encode()).hexdigest()
            meta.write_text("fileFormatVersion: 2\nguid: " + guid + "\n")
        rms = math.sqrt(sum(value * value for value in samples) / len(samples))
        assert .01 < rms < .6
        assert max(abs(value) for value in samples) < 1
        assert abs(samples[0]) < .001 and abs(samples[-1]) < .002
        print(f"{name}: {duration:.2f}s, RMS {rms:.3f}, peak 0.800")


if __name__ == "__main__":
    main()
