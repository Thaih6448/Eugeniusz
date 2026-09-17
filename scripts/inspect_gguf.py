"""Read GGUF tensor storage totals without numpy, loading weights, or modifying files."""
import argparse
import json
from pathlib import Path
import struct


def inspect(path):
    path = Path(path)
    with path.open("rb") as stream:
        def read(fmt):
            size = struct.calcsize("<" + fmt)
            data = stream.read(size)
            if len(data) != size: raise ValueError("Truncated GGUF")
            return struct.unpack("<" + fmt, data)[0]
        def string():
            size = read("Q")
            if size > path.stat().st_size: raise ValueError("Invalid string length")
            return stream.read(size).decode("utf-8")
        formats = {0:"B", 1:"b", 2:"H", 3:"h", 4:"I", 5:"i", 6:"f", 7:"?", 10:"Q", 11:"q", 12:"d"}
        def value(kind, retain=False):
            if kind == 8:
                size = read("Q")
                if retain: return stream.read(size).decode("utf-8")
                stream.seek(size, 1); return None
            if kind == 9:
                subtype, count = read("I"), read("Q")
                if subtype in formats:
                    stream.seek(struct.calcsize("<" + formats[subtype]) * count, 1)
                else:
                    for _ in range(count): value(subtype)
                return None
            return read(formats[kind])
        if stream.read(4) != b"GGUF" or read("I") != 3: raise ValueError("Expected GGUF v3")
        count, metadata_count = read("Q"), read("Q")
        metadata = {}
        for _ in range(metadata_count):
            key, kind = string(), read("I")
            keep = key in ("general.architecture", "general.alignment") or key.endswith((".block_count", ".embedding_length"))
            result = value(kind, keep)
            if keep: metadata[key] = result
        tensors = []
        for _ in range(count):
            name, dimensions = string(), read("I")
            shape = [read("Q") for _ in range(dimensions)]
            tensors.append(dict(name=name, shape=shape, type=read("I"), offset=read("Q")))
        alignment = metadata.get("general.alignment", 32)
        data_start = (stream.tell() + alignment - 1) // alignment * alignment
    tensors.sort(key=lambda t: t["offset"])
    groups = {"transformer_blocks": 0, "input_embedding": 0, "output_head": 0, "other": 0}
    for i, tensor in enumerate(tensors):
        end = tensors[i+1]["offset"] if i+1 < len(tensors) else path.stat().st_size-data_start
        tensor["stored_bytes_including_padding"] = end - tensor["offset"]
        name = tensor["name"]
        group = "transformer_blocks" if name.startswith("blk.") else "input_embedding" if name == "token_embd.weight" else "output_head" if name == "output.weight" else "other"
        groups[group] += tensor["stored_bytes_including_padding"]
    return dict(file=path.name, bytes=path.stat().st_size, metadata=metadata, metadata_bytes=data_start,
                groups=groups, tensors=tensors,
                note="Storage inventory only. Keeping 26 output rows requires a custom runtime; input embedding rows and transformer blocks remain necessary. Tied embeddings cannot be removed from input processing.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("model", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    report = inspect(args.model)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2)+"\n")
    print(json.dumps({k:v for k,v in report.items() if k != "tensors"}, indent=2))
