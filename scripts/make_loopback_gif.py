"""Assembles docs/user-guide/images/tui-loopback-stream-N.png (written by LoopbackSampleStreamScreenshotTests)
into tui-loopback-stream.gif, one frame per arrived sample, holding the last frame."""
import glob
import os
import re
from PIL import Image

images = os.path.join(os.path.dirname(__file__), "..", "docs", "user-guide", "images")
stills = sorted(glob.glob(os.path.join(images, "tui-loopback-stream-*.png")),
                key=lambda p: int(re.search(r"-(\d+)\.png$", p).group(1)))
frames = [Image.open(p).convert("P", palette=Image.ADAPTIVE) for p in stills]
durations = [400] * (len(frames) - 1) + [2000]
frames[0].save(os.path.join(images, "tui-loopback-stream.gif"), save_all=True, append_images=frames[1:],
               duration=durations, loop=0, optimize=True)
print(f"{len(frames)} frames")
