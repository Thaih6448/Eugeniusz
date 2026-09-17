"""Download a pinned model, verify SHA-256, and preserve its license/model card."""
import argparse
import hashlib
import json
from pathlib import Path
import urllib.request
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parents[1]


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def download(profile, output):
    profile = "light" if profile == "small" else profile
    spec = json.loads((ROOT / "models/profiles.json").read_text())[profile]
    return download_spec(profile, spec, output)


def download_spec(profile, spec, output):
    if "license_url" in spec:
        url = urlsplit(spec["license_url"])
        if url.scheme != "https" or not url.netloc:
            raise ValueError("Model license_url must be an absolute HTTPS URL")
    output.mkdir(parents=True, exist_ok=True)
    target = output / spec["file"]
    base = f'https://huggingface.co/{spec["repository"]}/resolve/{spec["revision"]}/'
    if not target.exists() or digest(target) != spec["sha256"]:
        partial = target.with_name(target.name + ".part")
        print(f'Downloading {profile}: {spec["bytes"] / 1e6:.1f} MB', flush=True)
        with urllib.request.urlopen(base + spec["file"], timeout=120) as source, partial.open("wb") as destination:
            while True:
                block = source.read(1024 * 1024)
                if not block:
                    break
                destination.write(block)
        if partial.stat().st_size != spec["bytes"] or digest(partial) != spec["sha256"]:
            raise RuntimeError(f"Model verification failed: {partial}")
        partial.replace(target)
    for remote, local in (("LICENSE", f"{profile}-LICENSE.txt"), ("README.md", f"{profile}-MODEL_CARD.md")):
        url = spec.get("license_url", base + remote) if remote == "LICENSE" else base + remote
        with urllib.request.urlopen(url, timeout=30) as response:
            (output / local).write_bytes(response.read())
    (output / f"{profile}-provenance.json").write_text(json.dumps(spec, indent=2) + "\n")
    print(f"Verified: {target}", flush=True)
    return target


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("profile", choices=("small", "light", "medium", "large"))
    parser.add_argument("--output", type=Path, default=ROOT / "models/downloads")
    args = parser.parse_args()
    download(args.profile, args.output)
