"""Assembles the loopback stills written by the LoopbackSampleStreamScreenshotTests and LoopbackChartsScreenshotTests
classes (docs/user-guide/images/{tui,wpf}-loopback-{stream,charts}-N.png) into the matching
{tui,wpf}-loopback-{stream,charts}.gif, one frame per still, holding the last frame. Run after
`dotnet test --filter ClassName~Loopback` in the Console and Wpf test projects."""
import glob
import os
import re
from PIL import Image

images = os.path.join(os.path.dirname(__file__), "..", "docs", "user-guide", "images")

# (front end, kind, ms per frame): stream = one frame per arrived sample; charts = one frame per four samples.
GIFS = (("tui", "stream", 400), ("wpf", "stream", 400), ("tui", "charts", 600), ("wpf", "charts", 600))

for front_end, kind, frame_ms in GIFS:
    stills = sorted(glob.glob(os.path.join(images, f"{front_end}-loopback-{kind}-*.png")),
                    key=lambda p: int(re.search(r"-(\d+)\.png$", p).group(1)))
    if not stills:
        print(f"{front_end} {kind}: no stills, skipped")
        continue
    frames = [Image.open(p).convert("P", palette=Image.ADAPTIVE) for p in stills]
    durations = [frame_ms] * (len(frames) - 1) + [2000]
    frames[0].save(os.path.join(images, f"{front_end}-loopback-{kind}.gif"), save_all=True, append_images=frames[1:],
                   duration=durations, loop=0, optimize=True)
    print(f"{front_end} {kind}: {len(frames)} frames")
