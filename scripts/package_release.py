"""Create one self-contained profile bundle from an installed native prefix."""
import argparse
import json
from pathlib import Path
import shutil
import zipfile
from download_model import digest, ROOT
from archive_parts import split_archive


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("profile", choices=("light", "medium", "large"))
    parser.add_argument("--backend", choices=("cpu", "vulkan", "cuda", "metal"), required=True)
    parser.add_argument("--platform", required=True, help="For example windows-x64 or linux-x64")
    parser.add_argument("--prefix", type=Path, required=True, help="Installed native SDK")
    parser.add_argument("--models", type=Path, default=ROOT / "models/downloads")
    parser.add_argument("--output", type=Path, default=ROOT / "dist")
    args = parser.parse_args()
    if args.backend == "cpu" and args.profile != "light":
        raise SystemExit("Only the light CPU variant is part of the release matrix")
    spec = json.loads((ROOT / "models/profiles.json").read_text())[args.profile]
    model = args.models / spec["file"]
    if not model.exists() or digest(model) != spec["sha256"]:
        raise SystemExit("Download and verify the selected model first")
    if not (args.prefix / "include/eugeniusz/llama.h").is_file():
        raise SystemExit("Prefix must contain an installed Eugeniusz SDK")
    runtime_files = list(args.prefix.rglob("*.dll")) + list(args.prefix.rglob("*.so*")) + list(args.prefix.rglob("*.dylib"))
    if not any("eugeniusz_llama" in p.name for p in runtime_files):
        raise SystemExit("An installed shared inference adapter is required")
    if args.backend != "cpu" and not any(f"ggml-{args.backend}" in p.name for p in runtime_files):
        raise SystemExit(f"The prefix has no {args.backend} runtime library")
    name = f"eugeniusz-0.1.0-{args.profile}-{args.platform}-{args.backend}"
    stage = args.output / name
    if stage.exists():
        raise SystemExit("Bundle directory already exists; use a new output directory")
    args.output.mkdir(parents=True, exist_ok=True)
    shutil.copytree(args.prefix, stage)
    model_dir = stage / "models"
    model_dir.mkdir()
    shutil.copy2(ROOT / "models/profiles.json", model_dir)
    shutil.copy2(ROOT / "models/pixel-profile.json", model_dir)
    shutil.copy2(model, model_dir / model.name)
    for suffix in ("LICENSE.txt", "MODEL_CARD.md", "provenance.json"):
        shutil.copy2(args.models / f"{args.profile}-{suffix}", model_dir)
    for filename in ("LICENSE", "README.md", "THIRD_PARTY_NOTICES.md"):
        shutil.copy2(ROOT / filename, stage)
    for directory in ("docs", "examples", "bindings", "third_party", "scripts"):
        shutil.copytree(ROOT / directory, stage / directory,
                        ignore=shutil.ignore_patterns("bin", "obj", "__pycache__", "*.egg-info"))
    managed = ROOT / "bindings/dotnet/Eugeniusz/bin/Release/netstandard2.1/Eugeniusz.Managed.dll"
    if managed.is_file():
        shutil.copy2(managed, stage / "bindings/dotnet")
    (stage / "BUNDLE.md").write_text("# Binary bundle\n\nRead profile.json for the model path and default options. Native DLLs are in bin on Windows, shared libraries in lib on Unix. Keep dependencies together. C/C++ headers and CMake package files are included. Python bindings need only ctypes. The .NET assembly, when present, is bindings/dotnet/Eugeniusz.Managed.dll. See docs/integrations.md for host setup and docs/building.md for runtime prerequisites. README build commands describe the source repository.\n")
    (stage / "profile.json").write_text(json.dumps({"name": args.profile, "backend": args.backend,
        "model": "models/" + spec["file"], "context_size": spec["context_size"],
        "gpu_layers": 0 if args.backend == "cpu" else 99, "model_sha256": spec["sha256"]}, indent=2) + "\n")
    checksums = {str(p.relative_to(stage)).replace("\\", "/"): digest(p) for p in sorted(stage.rglob("*")) if p.is_file()}
    (stage / "SHA256SUMS.json").write_text(json.dumps(checksums, indent=2) + "\n")
    archive = args.output / (name + ".zip")
    # GGUF is already compressed by quantization; STORE avoids a long packing step.
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_STORED, allowZip64=True) as output:
        for path in sorted(stage.rglob("*")):
            if path.is_file():
                output.write(path, path.relative_to(stage.parent))
    archive.with_suffix(".zip.sha256").write_text(f"{digest(archive)}  {archive.name}\n")
    if archive.stat().st_size >= 2 * 1024**3:
        print(f"GitHub upload parts: {split_archive(archive)}")
    print(archive)


if __name__ == "__main__":
    main()
