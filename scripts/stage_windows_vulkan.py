"""Stage a pinned upstream Vulkan runtime beside an MSVC CPU build of Eugeniusz.

This avoids installing a GPU SDK on the build host. The wrapper is built locally;
llama.cpp/ggml GPU DLLs are upstream b6500 binaries with a verified archive hash.
"""
import argparse
import json
from pathlib import Path
import platform
import shutil
import urllib.request
import zipfile
from download_model import digest, ROOT

URL = "https://github.com/ggml-org/llama.cpp/releases/download/b6500/llama-b6500-bin-win-vulkan-x64.zip"
SHA256 = "d485ce9cdda9967d2b27a054bb7e16b57a56a332a4b54b2f02964b95ee591548"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cpu-prefix", type=Path, required=True, help="Installed MSVC shared CPU build")
    parser.add_argument("--output", type=Path, required=True, help="New directory, never overwritten")
    args = parser.parse_args()
    if platform.system() != "Windows" or platform.machine().lower() not in ("amd64", "x86_64"):
        raise SystemExit("This staging helper supports Windows x64 only")
    if args.output.exists():
        raise SystemExit("Output already exists; select a fresh directory")
    for name in ("eugeniusz.dll", "eugeniusz_llama.dll"):
        if not (args.cpu_prefix / "bin" / name).is_file():
            raise SystemExit(f"Missing {name}; install an MSVC shared CPU build first")
    cache = ROOT / ".deps/llama-vulkan.zip"
    cache.parent.mkdir(parents=True, exist_ok=True)
    if not cache.exists() or digest(cache) != SHA256:
        urllib.request.urlretrieve(URL, cache)
    if digest(cache) != SHA256:
        raise SystemExit("Upstream archive hash mismatch")
    shutil.copytree(args.cpu_prefix, args.output)
    destination = args.output / "bin"
    licenses = args.output / "share/eugeniusz/upstream-vulkan"
    licenses.mkdir(parents=True, exist_ok=True)
    shutil.copytree(ROOT / "third_party/licenses", licenses / "licenses", dirs_exist_ok=True)
    with zipfile.ZipFile(cache) as archive:
        # Explicit allowlist: do not ship unrelated executables, network clients or RPC.
        wanted = {"llama.dll", "ggml.dll", "ggml-base.dll", "ggml-vulkan.dll", "libomp140.x86_64.dll"}
        for entry in archive.infolist():
            name = Path(entry.filename).name
            if name in wanted or (name.startswith("ggml-cpu-") and name.endswith(".dll")):
                (destination / name).write_bytes(archive.read(entry))
            elif name.startswith("LICENSE"):
                (licenses / name).write_bytes(archive.read(entry))
    # The copied source CPU backend is registered differently from upstream plugins.
    cpu_dll = destination / "ggml-cpu.dll"
    if cpu_dll.exists():
        cpu_dll.unlink()
    (licenses / "provenance.json").write_text(json.dumps({"url": URL, "sha256": SHA256,
        "runtime": "Upstream prebuilt Vulkan b6500; wrapper built locally with MSVC"}, indent=2) + "\n")
    print(f"Staged Vulkan runtime: {args.output}")


if __name__ == "__main__":
    main()
