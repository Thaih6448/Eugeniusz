"""Create the README demo loops from the maintainer's original recordings.

Requires FFmpeg only when regenerating documentation assets, not at runtime.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
CLIPS = (
    ("driving", "Model drives a car _ Eugeniusz 2026-09-17 13-52-14.mp4", 6, 38.3, 2, 840, 12),
    ("snake", "Model plays Snake _ Eugeniusz 2026-09-17 11-37-15.mp4", 0, 9.67, 1, 600, 12),
    ("pixel-art", "16-color pixel generator _ Eugeniusz 2026-09-17 13-06-31.mp4", 4, 34.14, 2.5, 600, 10),
)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("recordings", type=Path, help="Directory containing the three original MP4 recordings")
    parser.add_argument("--ffmpeg", default="ffmpeg", help="FFmpeg executable or full path")
    parser.add_argument("--output", type=Path, default=ROOT / "docs/assets")
    args = parser.parse_args()
    sources = [(spec, args.recordings / spec[1]) for spec in CLIPS]
    for _, source in sources:
        if not source.is_file():
            parser.error(f"Missing recording: {source}")
    args.output.mkdir(parents=True, exist_ok=True)
    manifest = []
    for (name, filename, start, duration, speed, width, fps), source in sources:
        destination = args.output / f"demo-{name}.gif"
        # Remove the model picker/path; retain the prompt, counters, and demo area.
        # A shared palette and ordered dithering avoid temporal color flicker.
        filters = (
            f"crop=iw:ih-132:0:132,setpts=(PTS-STARTPTS)/{speed},"
            f"fps={fps},scale={width}:-2:flags=lanczos,"
            "tpad=stop_mode=clone:stop_duration=1.2,split[a][b];"
            "[a]palettegen=max_colors=128:stats_mode=diff[p];"
            "[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle"
        )
        subprocess.run([
            args.ffmpeg, "-hide_banner", "-loglevel", "error", "-y",
            "-ss", str(start), "-t", str(duration), "-i", str(source),
            "-filter_complex", filters, "-an", "-loop", "0", str(destination),
        ], check=True)
        manifest.append(dict(
            file=destination.name, source=filename, source_start_seconds=start,
            source_duration_seconds=duration, playback_speed=speed, width=width,
            fps=fps, crop_top_pixels=132, end_hold_seconds=1.2,
            bytes=destination.stat().st_size,
            sha256=hashlib.sha256(destination.read_bytes()).hexdigest(),
        ))
        print(f"{destination.name}: {destination.stat().st_size / 1024:.0f} KiB", flush=True)
    (args.output / "demo-manifest.json").write_text(
        json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
