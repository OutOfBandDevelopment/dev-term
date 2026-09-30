#!/usr/bin/env python3
"""
Pixel-diffs the docs/user-guide/images/*.png screenshots against a git ref, so a reviewer (or
Claude) doesn't have to spend vision tokens opening every regenerated screenshot after running the
test suite.

Why this is safe to treat as an exact diff, not a fuzzy one: TuiScreenshot.Save and WpfScreenshot.Save
(tests/DevTerm.Console.Tests/TuiScreenshot.cs, tests/DevTerm.Wpf.Tests/WpfScreenshot.cs) render from a
deterministic Terminal.Gui screen buffer / WPF visual tree - no live clock, no unseeded Random anywhere
in the render path (DevTerm.Transports.Loopback's demo data generators are explicitly documented
deterministic) - and running the same screenshot test twice in a row on the same machine produces
byte-identical PNGs (verified 2026-09-30 via sha256, see docs/changes/2026-09-30.md). So any pixel
difference against a committed baseline is a real content change from a real code change, never noise -
the only judgment call is whether that change is *expected* for the edit you just made, which is what
the per-image mask file below records.

## Masks

Each image's mask lives in its own sidecar JSON file, co-located next to the PNG, one per file (not one
shared file for every screenshot) since a given edit's expected-change regions are specific to that
image and are easiest to review/edit sitting right next to it:

    docs/user-guide/images/tui-theme-dark-panel.png
    docs/user-guide/images/tui-theme-dark-panel.mask.json   <- sidecar, same stem + ".mask.json"

Sidecar format (a bare list of rects also works if you don't need a comment):
    {
      "_comment": "why these regions are expected to change for the current edit",
      "regions": [
        {"x": 0, "y": 0, "w": 80, "h": 3, "note": "the focused panel header this edit targets"}
      ]
    }
Rects are in PNG pixel coordinates (not terminal cells/DIPs). No sidecar file (or an empty regions
list) means that image must match its baseline exactly. Prefer deleting/narrowing a sidecar once its
change is reviewed and committed, so a later unrelated diff isn't silently waved through by a stale,
overly broad mask.

## Commands

    python scripts/image-diff/image_diff.py [diff] [--ref HEAD] [--save-diff DIR] [--tolerance N] [file ...]
        Diff mode (default). With no file arguments, diffs every changed-or-new
        docs/user-guide/images/*.png (per `git status`) against --ref. Exit code 0 if every change is
        fully covered by that file's mask sidecar (or there's no change at all), 1 if any file has a
        diff pixel outside its mask.

    python scripts/image-diff/image_diff.py render-masks [--out-dir artifacts/image-diff-preview] [file ...]
        Draws each image's declared mask rects (outlined + translucent fill, numbered) onto a copy of
        the image so you can visually confirm a mask actually covers the region you mean it to, before
        trusting it to auto-clear a diff. With no file arguments, renders every image under
        docs/user-guide/images that currently has a mask sidecar. Output is untracked review output
        (same convention as artifacts/ for TuiReview/WpfReview) - never commit it.
"""

from __future__ import annotations

import argparse
import io
import json
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFont

IMAGES_DIR = Path("docs/user-guide/images")
DEFAULT_PREVIEW_DIR = Path("artifacts/image-diff-preview")
MASK_RECT_COLOR = (255, 0, 0, 255)
MASK_FILL_ALPHA = 60


def mask_sidecar_path(image_path: Path) -> Path:
    return image_path.with_suffix("").with_suffix(".mask.json")


def load_regions(image_path: Path) -> list[dict]:
    sidecar = mask_sidecar_path(image_path)
    if not sidecar.exists():
        return []
    with sidecar.open("r", encoding="utf-8") as f:
        data = json.load(f)
    if isinstance(data, list):
        return data
    return data.get("regions", [])


def git_show(ref: str, path: str) -> bytes | None:
    result = subprocess.run(["git", "show", f"{ref}:{path}"], capture_output=True)
    if result.returncode != 0:
        return None
    return result.stdout


def changed_png_paths(ref: str) -> list[str]:
    result = subprocess.run(
        ["git", "status", "--porcelain", "--", f"{IMAGES_DIR.as_posix()}/*.png"],
        capture_output=True,
        text=True,
        check=True,
    )
    paths = []
    for line in result.stdout.splitlines():
        path = line[3:].strip()
        if path.endswith(".png"):
            paths.append(path)
    return paths


def rect_mask_image(size: tuple[int, int], regions: list[dict]) -> Image.Image:
    mask = Image.new("L", size, 0)
    draw = ImageDraw.Draw(mask)
    for r in regions:
        x0, y0 = r["x"], r["y"]
        x1, y1 = min(size[0], x0 + r["w"]), min(size[1], y0 + r["h"])
        if x1 > x0 and y1 > y0:
            draw.rectangle([x0, y0, x1 - 1, y1 - 1], fill=255)
    return mask


def diff_one(path: str, ref: str, tolerance: int, save_diff_dir: Path | None) -> bool:
    """Returns True if the file is clean (no unmasked diff)."""
    working_path = Path(path)
    if not working_path.exists():
        print(f"SKIP  {path}: not present in working tree")
        return True

    baseline_bytes = git_show(ref, path)
    if baseline_bytes is None:
        print(f"NEW   {path}: not in {ref} - nothing to diff against, review directly")
        return False

    baseline = Image.open(io.BytesIO(baseline_bytes)).convert("RGBA")
    working = Image.open(working_path).convert("RGBA")

    if baseline.size != working.size:
        print(f"DIFF  {path}: size changed {baseline.size} -> {working.size} (review directly)")
        return False

    diff = ImageChops.difference(baseline, working)
    if tolerance > 0:
        diff = diff.point(lambda p: 0 if p <= tolerance else p)

    diff_gray = diff.convert("L")
    bbox = diff_gray.getbbox()
    if bbox is None:
        print(f"OK    {path}: identical to {ref}")
        return True

    regions = load_regions(working_path)
    if regions:
        mask = rect_mask_image(working.size, regions)
        diff_gray = ImageChops.subtract(diff_gray, mask)
        bbox = diff_gray.getbbox()

    if bbox is None:
        print(f"OK*   {path}: diff fully covered by {mask_sidecar_path(working_path).name} ({len(regions)} region(s))")
        return True

    diff_pixels = sum(diff_gray.histogram()[1:])
    print(
        f"DIFF  {path}: {diff_pixels} pixel(s) differ outside any declared mask region, "
        f"bounding box {bbox} - review this one"
    )

    if save_diff_dir is not None:
        save_diff_dir.mkdir(parents=True, exist_ok=True)
        out_path = save_diff_dir / f"{working_path.stem}.diff.png"
        diff.point(lambda p: min(255, p * 8)).convert("RGB").save(out_path)
        print(f"      wrote {out_path}")

    return False


def run_diff(args: argparse.Namespace) -> int:
    files = args.files or changed_png_paths(args.ref)
    if not files:
        print("No changed PNGs to check.")
        return 0

    save_diff_dir = Path(args.save_diff) if args.save_diff else None

    all_clean = True
    for path in files:
        clean = diff_one(path, args.ref, args.tolerance, save_diff_dir)
        all_clean = all_clean and clean

    return 0 if all_clean else 1


def render_one_mask_preview(image_path: Path, out_dir: Path, font: ImageFont.ImageFont) -> None:
    regions = load_regions(image_path)
    if not regions:
        print(f"SKIP  {image_path}: no mask sidecar ({mask_sidecar_path(image_path).name})")
        return

    base = Image.open(image_path).convert("RGBA")
    overlay = Image.new("RGBA", base.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)

    for i, r in enumerate(regions, start=1):
        x0, y0 = r["x"], r["y"]
        x1, y1 = x0 + r["w"], y0 + r["h"]
        draw.rectangle(
            [x0, y0, x1 - 1, y1 - 1],
            outline=MASK_RECT_COLOR,
            fill=(*MASK_RECT_COLOR[:3], MASK_FILL_ALPHA),
            width=2,
        )
        label = f"#{i}" + (f" {r['note']}" if r.get("note") else "")
        draw.text((x0 + 3, y0 + 2), label, fill=MASK_RECT_COLOR, font=font)

    combined = Image.alpha_composite(base, overlay).convert("RGB")
    out_dir.mkdir(parents=True, exist_ok=True)
    out_path = out_dir / f"{image_path.stem}.masked-preview.png"
    combined.save(out_path)
    print(f"WROTE {out_path} ({len(regions)} region(s))")


def run_render_masks(args: argparse.Namespace) -> int:
    if args.files:
        files = [Path(p) for p in args.files]
    else:
        files = sorted(p for p in IMAGES_DIR.glob("*.png") if mask_sidecar_path(p).exists())

    if not files:
        print("No images with a mask sidecar to render.")
        return 0

    font = ImageFont.load_default()
    out_dir = Path(args.out_dir)
    for path in files:
        render_one_mask_preview(path, out_dir, font)

    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    subparsers = parser.add_subparsers(dest="command")

    diff_parser = subparsers.add_parser("diff", help="pixel-diff changed screenshots against a git ref (default)")
    diff_parser.add_argument("files", nargs="*", help="specific PNG paths; default is every changed docs/user-guide/images/*.png")
    diff_parser.add_argument("--ref", default="HEAD", help="git ref to diff against (default: HEAD)")
    diff_parser.add_argument("--save-diff", metavar="DIR", help="write an amplified diff PNG for each unmasked-diff file into DIR")
    diff_parser.add_argument("--tolerance", type=int, default=0, help="per-channel delta to ignore before masking (default: 0 - renders are proven deterministic)")
    diff_parser.set_defaults(func=run_diff)

    render_parser = subparsers.add_parser("render-masks", help="render each image's declared mask rects onto a copy, for visual review")
    render_parser.add_argument("files", nargs="*", help="specific PNG paths; default is every docs/user-guide/images/*.png that has a mask sidecar")
    render_parser.add_argument("--out-dir", default=str(DEFAULT_PREVIEW_DIR), help=f"where to write preview PNGs (default: {DEFAULT_PREVIEW_DIR})")
    render_parser.set_defaults(func=run_render_masks)

    # No explicit subcommand -> diff mode, so the common case ("did the screenshots I just
    # regenerated actually need review") stays a plain `python image_diff.py [--flags] [files]`
    # with no subcommand keyword to remember. argparse's own subparsers only default this way when
    # sys.argv is completely empty (an unrecognized leading flag like --save-diff otherwise errors
    # out before main() ever sees it), so insert "diff" explicitly whenever the first token isn't
    # already a known subcommand or -h/--help.
    argv = sys.argv[1:]
    if argv and argv[0] not in ("diff", "render-masks", "-h", "--help"):
        argv = ["diff", *argv]

    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
