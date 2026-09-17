# Validation status

Local measurements on 2026-09-17: Windows 11 x64, Visual Studio 2026 / MSVC 19.51,
NVIDIA GeForce RTX 4060 Ti with 16 GB VRAM. The GPU path uses a hash-verified upstream
llama.cpp b6500 Vulkan runtime with the locally built adapter. The CPU path is built
from pinned source with four inference threads. These are not RTX 4060 8 GB measurements.

| Profile/backend | Median request | Maximum request | Correct / 12 diagnostic cases | Sampled additional device memory |
| --- | ---: | ---: | ---: | ---: |
| light / CPU | 217 ms | 239 ms | 10/12 | n/a |
| light / Vulkan | 17 ms | 90 ms | 10/12 | ~908 MiB |
| medium / Vulkan | 31 ms | 68 ms | 11/12 | ~2,522 MiB |
| large / Vulkan | 54 ms | 55 ms | 12/12 | ~3,267 MiB |

Raw inputs, outputs, probabilities and times are in `docs/benchmarks/*.json`.
Reproduce with `scripts/benchmark.py`. There are only twelve authored sanity cases,
one pass after warmup, no independent test corpus, and no quality confidence interval.
Do not interpret 12/12 as general accuracy or a calibrated correctness guarantee.
Load time and warmup are excluded from latency. Cold shader compilation and different
input lengths can change timings. No state caching is used between questions.

Memory is total GPU usage sampled through nvidia-smi, minus its pre-load baseline;
other applications can change both values. Large's entire sampled device total was
7,572 MiB, including a 4,305 MiB baseline. The observed increment supports the 8 GB
target with a moderate context, but does not certify a game plus inference on every
RTX 4060. Test on the actual 8 GB card and reserve VRAM for rendering. Notebook GPU
variants and shared-memory driver spill can have different performance.

Local checks cover native numeric invariants, invalid arguments, stable softmax,
temperature fitting, metrics, conformal quantiles/sets, batch failure atomicity,
engine serialization, C ABI, Python FFI, .NET FFI, real CPU/GPU inference and model
file hashes. Build/install/consumer checks and package loading are also performed.
The core tests use synthetic callback logits; model smoke tests exercise the real
adapter separately. There is no hidden fake inference fallback.

Linux/macOS and GPU-from-source CI recipes are provided; they have not been run on
this Windows host. Unity 6.6 and Unreal 5.8 examples have been reviewed against
current primary documentation, but not built inside those editors. A local VS 2026
/MT shared-backend experiment failed at startup, so verified packages use /MD.

The .NET console startup issue was reproduced as `DllNotFoundException` and fixed
with native DLL deployment and an explicit resolver. Its ABI check and real light
GPU inference pass without editing PATH. The three Windows Forms apps compile
without warnings; automated form workflows exercise Choice/Score/Truth, missing
model recovery, Snake moves and cancellation, and all 64 pixels of an 8 × 8 image
with PNG round-trip verification. Rules tests cover relative turns, growth, wall/body
deaths, victories, departing-tail movement, palette bounds, and randomized full
pixel coverage. Rendered form previews use `DrawToBitmap`; these are not a claim
of manual mouse testing. Self-contained Windows x64 publishes include .NET.

Model behavior is separate from application correctness. In these demos, light
often repeated one turn or one color. Supplying per-action outcomes improved
Snake decisions with medium. Large generated a multicolor image with explicit
coordinate rules but still miscolored some pixels. Free-form image descriptions
can collapse to a solid color. No heuristic silently replaces a model decision.

Changing pixel positions from normalized `x/y` values to `top/left` percentages
improved one diagnostic but did not solve spatial reasoning. With large / Vulkan
on two 8 × 8 geometric prompts, the centered square improved from 43/64 to 56/64
correct palette choices; the upper-right quadrant remained at 16/64 in both
formats. These are two authored cases, not a general accuracy estimate. That intermediate
format maps the first/last rows and columns to 0%/100% and rounds intermediate
positions to whole percentages. Both prompt descriptions use equivalent bounds.
See [raw pixel results](benchmarks/pixel-position-format.json) and reproduce the
frozen prompt comparison with `python scripts/benchmark_pixel_positions.py`.

Further pixel experiments compared independent decisions, dedicated system
instructions, a 5 × 5 neighborhood of completed pixels, and the complete partial
canvas. On the original large model, square correctness was respectively 16, 38,
50, and 35 out of 64; flag correctness was 24, 32, 24, and 25 out of 64. Neighbor
feedback sometimes propagated the first wrong color. These use different prompts
from the earlier percentage-format test. Raw data is in
`benchmarks/pixel-context.json`; `scripts/benchmark_pixel_context.py` contains the
frozen prompts, palette, seed, and rendering procedure.

The shipped pixel demo now plans a scene once, computes coverage/occlusion in the
host, then requests a palette choice for every randomly visited pixel. A dedicated
optional Qwen3-4B-Instruct-2507 Q4_K_M model was tested with the same Vulkan runtime.
On six authored 8 × 8 cases, the final renderer matched its model-authored scene
for all 384 pixels. On the two cases with pixel-exact references, the square improved
from 16/64 to 60/64 and the three-band flag from 46/64 to 64/64, comparing both
methods on this same instruction model. These results measure geometric correctness
only for those two cases. Agreement with the model's own plan does not establish
that the plan depicts the requested scene accurately. Fruit details, proportions,
or attachment of neighboring parts can still be wrong. A shorter final planner
prompt with a worked flower example improved layer ordering; a preceding verbose
prompt could place grass in front of a house and hide the walls at 12 × 12.
The final house check at the default 12 × 12 resolution completed 144/144 pixel
choices consistent with its scene; its [image and raw results](benchmarks/pixel-scenes/default-12/results.json)
are retained separately from the six-case 8 × 8 comparison.

[Before/after image sheet](benchmarks/pixel-scenes/comparison.png) and
[per-pixel results](benchmarks/pixel-scenes/results.json) include all six cases,
not just the best outputs. The scene JSON is retained beside them. The comparison
runner uses the application's actual planner, parser, rasterization, and inference
code. Latencies include planning/load when applicable and are individual local runs,
not controlled throughput measurements. The final runtime also passed CPU tests
for default/custom prompts, completion, budget exhaustion, and subsequent typed
inference; existing C++ SDK and .NET decision/Snake smoke checks pass.

The Driving WinForms demo passed deterministic tests for circle/segment raycasts,
relative sensor angles, acceleration/braking/steering, contact collisions, small
obstacle tunneling, finish precedence, and fractional-frame timeouts. A test of
100 seeds checks deterministic generation of 12 varied circles and a connected
corridor with car-radius clearance. The complete solution builds without warnings.

With Qwen3-4B-Instruct-2507 Q4_K_M on the local Vulkan runtime, the final controller
finished seeds 42, 7, and 103 without contact: 85, 87, and 84 control cycles,
respectively (512 total Choice calls). Simulation times were 25.46, 25.91, and
24.98 seconds; inference wall time is additional. These are three diagnostic
courses, not a general driving success-rate estimate. The model receives five
distances, speed, and derived comparison summaries, then makes separate steering
and pedal choices. Earlier versions using raw numeric distances could repeatedly
brake or steer incorrectly. Small models can still stall; success with light is
not asserted. No controller replaces model actions.

The [driving results](benchmarks/driving/results.json), per-decision JSON traces,
and [rendered form preview](benchmarks/driving/track-42.png) are retained. The
previews use DrawToBitmap rather than manual mouse testing. Reproduce with the
`--benchmark` command in the [WinForms guide](../examples/winforms/README.md).
CPU/light and GPU/instruction-model smoke checks exercise the normal form workflow
and cancellation; passing those checks verifies operation rather than race wins.

Commands:

```sh
ctest --test-dir build/cpu -C Release --output-on-failure
# Set PYTHONPATH=bindings/python and EUGENIUSZ_LIBRARY_DIR to your build/install path.
# Optionally set EUGENIUSZ_TEST_MODEL to enable the real model smoke test.
python -m unittest discover -s tests -p test_python.py -v
python scripts/benchmark.py light --library-dir build/install-cpu --output build/light-cpu.json
python scripts/benchmark.py large --library-dir build/install-vulkan --gpu --output build/large-gpu.json
```
