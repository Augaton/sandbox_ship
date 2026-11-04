"""The game's mix, measured as it plays at the options' default volumes.

Loudness is ITU-R BS.1770's (K-weighted, in LUFS): a piece of music over its whole length
(gated), an effect over its loudest 100 ms (the ear's integration for a short sound), centered
as the interface plays it. Needs numpy and scipy.

  godot --headless --path godot/Game --audio-driver Dummy -- --sfx-dump=DIR/sfx
  godot --headless --path godot/Game --audio-driver Dummy -- --music-dump=DIR/music
  python3 -I godot/tools/loudness.py DIR
  python3 -I godot/tools/loudness.py --record FILE.wav    (a --music-record run, every 3 s)
"""
import glob
import os
import sys

import numpy as np
from scipy.io import wavfile
from scipy.signal import lfilter

# The options' default volumes (Settings.Volume, Settings.MusicVolume x Music.Level).
EFFECTS, MUSIC = 0.5, 0.5 * 0.6


def kweight(x, fs):
    """BS.1770's two stages, a high shelf then a high pass, at any rate."""
    f0, g, q = 1681.974450955533, 3.999843853973347, 0.7071752369554196
    k = np.tan(np.pi * f0 / fs)
    vh, a0 = 10 ** (g / 20), 1 + k / q + k * k
    vb = vh ** 0.4996667741545416
    shelf = ([(vh + vb * k / q + k * k) / a0, 2 * (k * k - vh) / a0, (vh - vb * k / q + k * k) / a0],
             [1, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0])
    f0, q = 38.13547087602444, 0.5003270373238773
    k = np.tan(np.pi * f0 / fs)
    a0 = 1 + k / q + k * k
    high = ([1, -2, 1], [1, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0])
    return lfilter(*high, lfilter(*shelf, x, axis=0), axis=0)


def load(path):
    fs, d = wavfile.read(path)
    d = d.astype(np.float64) / 32768
    return fs, d[:, None] if d.ndim == 1 else d


def lufs(mean_power):
    return -0.691 + 10 * np.log10(np.maximum(mean_power, 1e-12))


def power(fs, x):
    return (kweight(x, fs) ** 2).sum(axis=1)


def loudest(fs, x, seconds):
    """The loudest window of `seconds`, the sound followed by silence when it is shorter."""
    n = int(fs * seconds)
    z = power(fs, np.vstack([x, np.zeros((n, x.shape[1]))]))
    c = np.concatenate([[0], np.cumsum(z)])
    return lufs((c[n:] - c[:-n]).max() / n)


def integrated(fs, x):
    """Gated over 400 ms blocks every 100 ms: absolute gate at -70, relative at -10 LU."""
    z = power(fs, x)
    n, hop = int(0.4 * fs), int(0.1 * fs)
    blocks = np.array([z[i:i + n].mean() for i in range(0, len(z) - n, hop)])
    blocks = blocks[lufs(blocks) > -70]
    return lufs(blocks[lufs(blocks) > lufs(blocks.mean()) - 10].mean())


def peak(x):
    return 20 * np.log10(max(abs(x).max(), 1e-9))


def mix(root):
    pieces = {os.path.basename(p)[:-4]: load(p) for p in sorted(glob.glob(f"{root}/music/*.wav"))}
    print("Music: integrated LUFS, loudest 3 s, peak dBFS")
    for name, (fs, x) in pieces.items():
        if name.endswith(("-motion", "-fury")):
            continue
        rows = [(name, x)]
        if name.endswith("-calm"):
            # A battle's layers, as they come in with the fighting.
            base = name[:-5]
            rows.append((f"{base} +motion", x + pieces[f"{base}-motion"][1]))
            rows.append((f"{base} +fury", rows[-1][1] + pieces[f"{base}-fury"][1]))
        for label, y in rows:
            y = y * MUSIC
            print(f"  {label:16s} {integrated(fs, y):6.1f} {loudest(fs, y, 3):6.1f} {peak(y):6.1f}")
    print("Effects, centered: loudest 100 ms LUFS, peak dBFS, length")
    for p in sorted(glob.glob(f"{root}/sfx/*.wav")):
        fs, x = load(p)
        y = np.hstack([x, x]) * np.sqrt(0.5) * EFFECTS
        print(f"  {os.path.basename(p)[:-4]:16s} {loudest(fs, y, 0.1):6.1f} {peak(y):6.1f} {len(x) / fs * 1000:6.0f} ms")


def record(path):
    fs, x = load(path)
    z = power(fs, x)
    n = 3 * fs
    print(f"{len(x) / fs:.0f} s, peak {peak(x):.1f} dBFS; every 3 s: short-term LUFS, peak dBFS")
    for at in range(0, len(z) - n + 1, n):
        print(f"  {at / fs:5.0f} s {lufs(z[at:at + n].mean()):6.1f} {peak(x[at:at + n]):6.1f}")


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--record":
        record(sys.argv[2])
    elif len(sys.argv) == 2:
        mix(sys.argv[1])
    else:
        sys.exit(__doc__)
