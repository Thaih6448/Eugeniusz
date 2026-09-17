"""Download a hash-pinned comparison model without changing release profiles."""
import argparse
import json
from pathlib import Path
from download_model import ROOT, download_spec
from download_pixel_model import fetch_weights

if __name__ == "__main__":
    candidates = json.loads((ROOT / "models/candidates.json").read_text())
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("candidate", choices=sorted(candidates))
    parser.add_argument("--output", type=Path, default=ROOT / "models/downloads")
    args = parser.parse_args()
    spec = candidates[args.candidate]
    fetch_weights(spec, args.output)
    download_spec(args.candidate, spec, args.output)
