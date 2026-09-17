# Third-party notices

Eugeniusz's original source is MIT licensed. Model weights retain their own license.

| Component | Version / source | License |
| --- | --- | --- |
| llama.cpp / ggml | b6500, a7a98e0fffed794396b3fbad4dcdbbc184963645 | MIT |
| Qwen3 0.6B, 1.7B, 4B weights | Pinned in `models/profiles.json` | Apache-2.0 |
| Historical ggml-org 0.6B GGUF conversion | Pinned in `models/profiles.previous.json` | Apache-2.0, derived from Qwen3 |
| Large: Qwen3-4B-Instruct-2507 / Unsloth GGUF conversion | Pinned in `models/profiles.json` and `models/pixel-profile.json` | Apache-2.0 |
| Evaluated Qwen2.5 and SmolLM2 candidate weights | Pinned in `models/candidates.json`; not included in release bundles | Apache-2.0 |

The large profile (also used by PixelArt) is derived from [Qwen3-4B-Instruct-2507](https://huggingface.co/Qwen/Qwen3-4B-Instruct-2507)
and distributed as [Unsloth GGUF weights](https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF).
Its downloader retains the upstream license, model card, and exact hash/revision.

Preserve the included upstream licenses, copyright notices and model cards when
redistributing a bundle. Apache-2.0 permits commercial use and modification subject
to its conditions; it does not grant trademark rights. This project's MIT license
does not relicense Qwen weights. The download script retains model provenance.

The CPU source build disables OpenMP and network clients. Optional source GPU builds
use vendor/system SDKs: CUDA, Vulkan or Metal. Redistributable runtime components
and GPU drivers have their own terms. Do not bundle an entire SDK with an application.

The optional upstream Windows Vulkan runtime contains LLVM OpenMP and may include
other upstream components. Its provenance and license files are retained by the
staging script. This path uses upstream binaries, not a locally compiled GPU backend.

Jev and TypeSafe AI are names belonging to their respective owners. Eugeniusz is an
independent implementation inspired by the public decision API, with no affiliation,
proprietary model weights, copied implementation, or claimed API/quality parity.
