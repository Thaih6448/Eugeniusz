"""Split oversized release archives and reassemble verified parts for GitHub assets."""
import argparse
import hashlib
import json
from pathlib import Path
from download_model import digest


def split_archive(archive, part_bytes=1024 * 1024 * 1024):
    manifest = {"archive": archive.name, "sha256": digest(archive), "parts": []}
    with archive.open("rb") as source:
        index = 1
        while source.tell() < archive.stat().st_size:
            part = archive.with_name(archive.name + f".part{index:03d}")
            if part.exists():
                raise FileExistsError(part)
            remaining = part_bytes
            with part.open("xb") as destination:
                while remaining:
                    block = source.read(min(1024 * 1024, remaining))
                    if not block:
                        break
                    destination.write(block)
                    remaining -= len(block)
            manifest["parts"].append({"file": part.name, "sha256": digest(part), "bytes": part.stat().st_size})
            index += 1
    output = archive.with_name(archive.name + ".parts.json")
    output.write_text(json.dumps(manifest, indent=2) + "\n")
    return output


def join_archive(manifest_path, output):
    manifest = json.loads(manifest_path.read_text())
    if output.exists():
        raise FileExistsError(output)
    parts = []
    for spec in manifest["parts"]:
        name = spec["file"]
        if Path(name).name != name or "/" in name or "\\" in name:
            raise ValueError("Part names must be plain filenames")
        part = manifest_path.parent / name
        if part.stat().st_size != spec["bytes"] or digest(part) != spec["sha256"]:
            raise ValueError(f"Invalid part: {part}")
        parts.append(part)
    checksum = hashlib.sha256()
    with output.open("xb") as destination:
        for part in parts:
            with part.open("rb") as source:
                for block in iter(lambda: source.read(1024 * 1024), b""):
                    checksum.update(block)
                    destination.write(block)
    if checksum.hexdigest() != manifest["sha256"]:
        raise ValueError("Reassembled archive checksum mismatch; do not use the output")
    return output


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    split = commands.add_parser("split")
    split.add_argument("archive", type=Path)
    join = commands.add_parser("join")
    join.add_argument("manifest", type=Path)
    join.add_argument("output", type=Path)
    args = parser.parse_args()
    print(split_archive(args.archive) if args.command == "split" else join_archive(args.manifest, args.output))
