# Repository media

`eugeniusz-banner.png` is an unchanged copy of the maintainer-provided
`eugeniusz.png`. The three demo GIFs were converted from the supplied September 17,
2026 recordings. The original MP4 files remain outside the repository.

| Asset | Playback | Source interval | Width |
| --- | --- | --- | ---: |
| `demo-driving.gif` | 2x | 6.00–44.30 s | 840 px |
| `demo-snake.gif` | 1x | 0.00–9.67 s | 600 px |
| `demo-pixel-art.gif` | 2.5x | 4.00–38.14 s | 600 px |

Each loop holds its final frame for 1.2 seconds. The top 132 pixels containing
the model picker and local path are cropped; the demo, counters, and image prompt
remain visible. Videos are scaled with a shared 128-color palette and ordered
dithering. The source intervals are continuous: no successful action was spliced
into a different run. Playback speed is disclosed beside each README preview.

These are historical UI demonstrations, not benchmarks of the current adapter.
Current quality and performance measurements are in [model quality](../model-quality.md).
The manifest records source filenames, conversion settings, sizes, and GIF hashes.

To regenerate, install FFmpeg as a documentation-authoring tool and run:

```sh
python scripts/build_demo_gifs.py /path/to/original/recordings
# An explicit executable path is also accepted:
python scripts/build_demo_gifs.py /path/to/original/recordings --ffmpeg /path/to/ffmpeg
```

FFmpeg is not a Eugeniusz build or runtime dependency. The script uses Python's
standard library. Different FFmpeg versions may produce different encoded hashes.
The repository banner is copied separately and is never modified by this script.

The README uses the real `komorra/Eugeniusz` GitHub Actions workflow badge,
Shields.io star/license/language badges, and a theme-aware Star History embed.
No successful CI result or popularity count is hard-coded. External services may
cache updates; the image links open their corresponding live pages.
