# Release profiles and packaging

Three model profiles have one ABI and two deployment classes: GPU for light, medium,
large, plus CPU for light. `small` is not a fourth profile. Profile defaults and
immutable weight hashes live in `models/profiles.json`.
`cpu_supported` describes the supported release matrix, not whether llama.cpp
can technically load those weights on a CPU. The pixel-art compatibility manifest
uses the same options and support policy as the large profile.

The large profile now uses Qwen3-4B-Instruct-2507 Q4_K_M, the same weights
previously offered as the optional pixel-art model. `download_pixel_model.py`
remains a compatibility downloader; it reuses the same file. `small` is a CLI
alias for the canonical `light` profile. See [model quality](model-quality.md)
for current sizes, measurements, tradeoffs, and the archived previous profiles.

The ready-to-run WinForms apps are published separately with
`examples/winforms/publish.ps1` and keep weights external. The four desktop apps
are Decisions, Snake, PixelArt, and Driving. Source checkouts prefer medium for
Snake, large for PixelArt, and the large instruction model (then medium) for
Driving; bundles retain their selected profile.

The full planned matrix is:

| OS / architecture | CPU | GPU |
| --- | --- | --- |
| Windows x64 | light | light / medium / large, Vulkan or CUDA |
| Linux x64 | light | light / medium / large, Vulkan or CUDA |
| macOS arm64 | light | light / medium / large, Metal |

This source tree can build all listed backend configurations with their SDKs. Local
artifacts validate Windows CPU and an upstream Windows Vulkan runtime. Other matrix
entries require their platform build/smoke checks; a CI definition is not evidence
that its jobs have run. No release is automatically published by these workflows.

```sh
python scripts/download_model.py light
python scripts/download_model.py medium
python scripts/download_model.py large

python scripts/package_release.py light --backend cpu --platform windows-x64 --prefix build/install-cpu
python scripts/package_release.py light --backend vulkan --platform windows-x64 --prefix build/install-vulkan
python scripts/package_release.py medium --backend vulkan --platform windows-x64 --prefix build/install-vulkan
python scripts/package_release.py large --backend vulkan --platform windows-x64 --prefix build/install-vulkan
```

Each archive contains the SDK, weights, model card/license, language bindings,
examples, documentation, a `profile.json`, per-file hashes and an archive checksum.
The script refuses to overwrite an existing bundle. Native DLL filenames deliberately
do not encode model size: the bundle directory/profile does. Read `profile.json` and
pass its model path, context size and GPU layer count when creating an engine.

[GitHub release assets must be smaller than 2 GiB](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases).
The large archive is automatically split into 1 GiB `.partNNN` files plus a
`.parts.json` manifest. Upload those parts and the manifest instead of its full ZIP.
The full ZIP is retained locally. Recipients can reconstruct a new ZIP with:

```sh
python scripts/archive_parts.py join eugeniusz-0.1.0-large-windows-x64-vulkan.zip.parts.json large-restored.zip
```

Publish the small reassembly helper and its `download_model.py` dependency alongside
the parts, or point recipients to this source repository. All parts and the final
archive are verified before the reconstructed ZIP should be used.

Before publishing, validate the exact packaged runtime, model hash, clean-machine
loading, licenses, dependency set and target memory use. Run the small diagnostic
suite as a smoke check, then evaluate representative held-out application data.
Document calibration provenance and do not publish performance numbers measured
with a different quantization/backend as though they apply to every release.

Windows source builds use the MSVC runtime. The packaging script does not silently
copy system or vendor runtime files whose redistribution should be handled by the
application installer. Supply the documented runtime prerequisites or permitted
app-local redistributables. Model licenses are separate from the project's MIT code.
