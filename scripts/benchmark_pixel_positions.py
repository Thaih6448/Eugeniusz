"""Compare frozen before/after pixel prompt formats on two geometric diagnostics.

These prompt snapshots measure this format change, not general image quality.
Requires the local Python binding, native runtime, and downloaded large GGUF.
"""
import argparse
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "bindings/python"))
from eugeniusz import Runtime

COLORS = ("Black", "Navy", "Blue", "Teal", "Dark green", "Green", "Brown", "Red",
          "Orange", "Yellow", "Cream", "White", "Gray", "Purple", "Pink", "Sky blue")
HEX = ("#101820", "#202D59", "#305CCF", "#008080", "#205C35", "#55AB41", "#854C30", "#D83C45",
       "#EF8834", "#F4D04B", "#F5E4BF", "#FFFFFF", "#85939D", "#7947A8", "#F28FB8", "#8FD3EB")
CASES = (
    ("centered_square",
     "Choose red if BOTH normalized x and y are between 0.25 and 0.75 inclusive; otherwise choose sky blue. This draws a red square on a sky blue background.",
     "Choose red if BOTH top and left are between 25% and 75% inclusive; otherwise choose sky blue. This draws a red square on a sky blue background."),
    ("upper_right",
     "Choose yellow if normalized y is below 0.5 AND normalized x is above 0.5; otherwise choose blue.",
     "Choose yellow if top is below 50% AND left is above 50%; otherwise choose blue."),
)


def prompts(description, x, y, percent):
    state = "Create a coherent tiny pixel-art image by choosing a color for one pixel at a time.\r\n"
    state += f"Image description: {description}\r\n"
    if percent:
        position = f"top {100 * y / 7:.0f}%, left {100 * x / 7:.0f}%"
        state += f"Canvas: 8 by 8. Target pixel position: {position}.\r\n"
        state += "Top is distance down from the top edge: 0% is the top row, 100% is the bottom row. Left is distance across from the left edge: 0% is the leftmost column, 100% is the rightmost column.\r\n"
    else:
        position = f"normalized x={x / 7:.3f}, y={y / 7:.3f}"
        state += f"Canvas: 8 by 8. Target pixel: x={x}, y={y}. Origin is top-left; x increases right, y increases down.\r\n"
        state += f"Normalized coordinates: x={x / 7:.3f}, y={y / 7:.3f}.\r\n"
    question = description + f"\nApply this specification at {position}. Select the color of ONLY this target pixel, not the dominant color of the whole image."
    return state, question


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--library-dir", default=str(ROOT / "build/install-vulkan"))
    parser.add_argument("--model", default=str(ROOT / "models/downloads/Qwen3-4B-Q4_K_M.gguf"))
    parser.add_argument("--gpu-layers", type=int, default=99)
    parser.add_argument("--output", type=Path, default=ROOT / "build/pixel-position-format.json")
    args = parser.parse_args()
    report = {"model": Path(args.model).name, "gpu_layers": args.gpu_layers,
              "size": 8, "note": "Two authored geometric cases; not a general image-quality benchmark. Frozen prompt snapshots of the format change.", "results": []}
    with Runtime(args.library_dir).load_model(args.model, gpu_layers=args.gpu_layers) as model:
        for name, old, new in CASES:
            for percent, description in ((False, old), (True, new)):
                row = {"case": name, "format": "percent" if percent else "normalized", "description": description, "correct": 0, "pixels": []}
                for index in range(64):
                    x, y = index % 8, index // 8
                    expected = (7 if 2 <= x <= 5 and 2 <= y <= 5 else 15) if name == "centered_square" else (9 if y < 4 and x >= 4 else 2)
                    state, question = prompts(description, x, y, percent)
                    answer = model.choice(state, question, [f"{c} ({h})" for c, h in zip(COLORS, HEX)])
                    row["correct"] += answer.choice == expected
                    row["pixels"].append({"x": x, "y": y, "expected": COLORS[expected], "actual": COLORS[answer.choice]})
                report["results"].append(row)
                print(f"{name} / {row['format']}: {row['correct']}/64", flush=True)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
