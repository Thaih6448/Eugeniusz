"""Download the optional, pinned instruction model used by the pixel-art demo."""
import argparse
import json
import concurrent.futures
import urllib.request
import time
from pathlib import Path
from download_model import ROOT, download_spec, digest


def fetch_weights(spec, output):
    output.mkdir(parents=True, exist_ok=True)
    target = output / spec["file"]
    if target.exists() and digest(target) == spec["sha256"]:
        return
    # Bounded ranges also work through proxies that buffer a full-file response.
    size, chunk = spec["bytes"], 16 * 1024 * 1024
    url = f'https://huggingface.co/{spec["repository"]}/resolve/{spec["revision"]}/{spec["file"]}?download=true'
    def fetch(start):
        end = min(size, start + chunk) - 1
        for attempt in range(3):
            try:
                request = urllib.request.Request(url, headers={"Range": f"bytes={start}-{end}"})
                with urllib.request.urlopen(request, timeout=60) as response:
                    if response.status != 206 or response.headers.get("Content-Range") != f"bytes {start}-{end}/{size}":
                        raise RuntimeError("Server did not honor the requested byte range")
                    data = response.read(chunk + 1)
                    if len(data) != end - start + 1:
                        raise RuntimeError("Incomplete model range")
                    return start, data
            except Exception:
                if attempt == 2:
                    raise
                time.sleep(attempt + 1)
    starts = list(range(0, size, chunk))
    partial = target.with_suffix(".ranges.part")
    with partial.open("wb") as destination, concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        destination.truncate(size)
        futures = [pool.submit(fetch, start) for start in starts]
        for i, future in enumerate(concurrent.futures.as_completed(futures), 1):
            start, data = future.result()
            destination.seek(start)
            destination.write(data)
            if i % 10 == 0 or i == len(starts):
                print(f"Downloaded {i}/{len(starts)} ranges", flush=True)
    if digest(partial) != spec["sha256"]:
        raise RuntimeError("Model SHA-256 verification failed")
    partial.replace(target)

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "models/downloads")
    args = parser.parse_args()
    spec = json.loads((ROOT / "models/pixel-profile.json").read_text())
    fetch_weights(spec, args.output)
    download_spec("pixel", spec, args.output)
