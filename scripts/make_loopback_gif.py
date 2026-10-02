"""Assembles the loopback-stream stills written by the LoopbackSampleStreamScreenshotTests classes
(docs/user-guide/images/{tui,wpf}-loopback-stream-N.png) into {tui,wpf}-loopback-stream.gif, one frame per arrived
sample, holding the last frame. Run after `dotnet test --filter ClassName~LoopbackSampleStreamScreenshotTests`."""
import glob
import os
import re
from PIL import Image

images = os.path.join(os.path.dirname(__file__), "..", "docs", "user-guide", "images")

for front_end in ("tui", "wpf"):
    stills = sorted(glob.glob(os.path.join(images, f"{front_end}-loopback-stream-*.png")),
                    key=lambda p: int(re.search(r"-(\d+)\.png$", p).group(1)))
    if not stills:
        print(f"{front_end}: no stills, skipped")
        continue
    frames = [Image.open(p).convert("P", palette=Image.ADAPTIVE) for p in stills]
    durations = [400] * (len(frames) - 1) + [2000]
    frames[0].save(os.path.join(images, f"{front_end}-loopback-stream.gif"), save_all=True, append_images=frames[1:],
                   duration=durations, loop=0, optimize=True)
    print(f"{front_end}: {len(frames)} frames")
