# Eugeniusz WinForms examples

Four Windows x64 / .NET 8 desktop applications use the real local native model.
All code, prompts and UI text are in English. CPU is the safe default; select GPU
when using a Vulkan/CUDA native runtime. A model stays loaded between decisions.

Watch the [Driving, Snake, and PixelArt demo GIFs](../../README.md#demos) in the
repository overview. Playback speeds are labeled beside each recording.

| Application | Behavior |
| --- | --- |
| **Decisions** | Enter a state and prompt, choose Choice / Score / Truth, inspect the typed answer and the full probability distribution. Choice options and score levels are editable. |
| **Snake** | The model chooses relative left, right or straight for every move. The UI tracks apples, deaths and victories, and logs decisions and latency. |
| **PixelArt** | Enter an image prompt. Each randomly selected pixel is assigned one of 16 colors by the model; preview progress, stop, and save an original-resolution PNG. |
| **Driving** | A model drives a car along a seeded winding track with circular obstacles, using five visible range sensors. Accelerate, coast, brake, and steer; count finishes, collisions, and timeouts. |

## Run from the source checkout

First build/install a native runtime using `docs/building.md`, or use the existing
`build/install-vulkan` / `build/install-cpu` directory if available. Download light
with `python scripts/download_model.py light` if weights are missing. Install the
.NET 8 SDK to build the examples. Framework-dependent EXEs require the .NET 8 Desktop
Runtime. Native Windows libraries also need the Microsoft Visual C++ x64 runtime.

From the repository root:

```powershell
dotnet run --project examples/winforms/Decisions -c Release
dotnet run --project examples/winforms/Snake -c Release
dotnet run --project examples/winforms/PixelArt -c Release
dotnet run --project examples/winforms/Driving -c Release

# Optional model/backend selection at startup:
./examples/winforms/run.ps1 Snake -Gpu
./examples/winforms/run.ps1 PixelArt -Model C:/Models/Qwen3-4B-Instruct-2507-Q4_K_M.gguf -Gpu
```

`examples/Directory.Build.targets` copies the native DLLs into every sample's output
and publish directories. It prefers the installed Vulkan runtime, then CPU, then
`build/install`. Override this during build using
`-p:EugeniuszNativeDir=C:/path/to/runtime/bin`. Keep one coherent backend DLL set
per directory. Use a fresh output/publish directory when switching native backends.

At runtime, `EUGENIUSZ_LIBRARY_DIR` overrides the native location; it accepts either
an SDK prefix or its `bin` directory. Otherwise the program first uses DLLs beside
the EXE. It also discovers installed runtimes in the source checkout. It registers
the dependency directory explicitly: manual PATH edits are not required.

The model picker discovers the light model in the source tree or a `profile.json`
in a release bundle. Source discovery reads the current `models/profiles.json`.
In source checkouts, Snake prefers the downloaded medium model
and PixelArt prefers the large instruction model when those
files exist. `EUGENIUSZ_MODEL`, `--model`, or **Browse model** can choose another
file. No application downloads a model silently. Missing native files/model paths
are displayed as actionable errors; they do not terminate the GUI.
Driving prefers the downloaded Qwen3-4B-Instruct-2507 model, then medium. Download
it with `python scripts/download_model.py large`. The older pixel-model downloader
obtains the same file. Existing downloaded weights are reused.

## Decision playground

- **Choice:** supply 2–26 distinct option descriptions, one per line.
- **Score:** supply 2–26 rubric levels from lowest to highest. The answer is an
  expected zero-based level and can be fractional.
- **Truth:** the two outcomes are automatically false and true; the answer is P(true).
- Temperature and confidence threshold are explicit controls. Default T=1 is
  uncalibrated. A threshold may mark the answer as abstained without hiding it.

## Snake rules

The board is 12 × 12 with walls and an initial length of three. Set **Apples to win**
(default six); reaching that target is a victory. Collision with a wall/body is a
death. Moving into the tail is allowed when it departs on the same non-growth step.
A round also ends in death after 576 consecutive moves without an apple, preventing
endless loops. Rounds restart automatically while running; totals persist until
**Reset statistics** or a change to the victory target.

The prompt contains snake length, head/apple coordinates, direction, body cells,
board bounds, and the destination/collision/distance for each possible action.
These are observed game facts. The model chooses the action; there is no pathfinder,
automatic correction or hidden fallback. Poor choices really can kill the snake.
Delay is added after each inference, so actual step time includes model latency.

## Driving with five sensors

**Driving** creates a winding, open track from start to finish, with 12 circular
obstacles of different sizes. **New track** changes the seed; **Retry track**
replays it. The generator reserves a connected corridor wide enough for the car.
It does not supply the road centerline or obstacle coordinates to the model.

Five raycasts originate at the car center: left 60°, left 30°, straight ahead,
right 30°, and right 60°. They detect the nearest circle surface or road edge,
up to 230 world units. Colored rays and endpoints show their measurements.
The dotted circle around the car is its collision footprint (radius 13).
Touching any obstacle or road boundary ends the run; crossing the checkered
finish safely counts as a victory. Finishes, collisions, and 120-second simulation
timeouts persist across retries and new tracks for the current session.

Each control cycle makes two Choice requests: left/straight/right steering and
accelerate/coast/brake. They combine into nine possible controls. The prompt
contains the five distances and speed, plus numerical summaries: whether diagonal
clearances differ by more than 30%, whether speed is below/within/above a 32–38
units/second cruise band, and whether front clearance exceeds braking distance
plus 25 units. These comparisons helped the tested model use the readings rather
than miscompare numbers. They contain no map or future collision information.
System instructions remain in code. There is no
path-following controller, automatic brake, or replacement of a model decision.
Steering uses a simple bicycle model; braking never reverses. Collision checks
use small physics substeps so a fast car cannot skip through small obstacles.

A control lasts 0.3 simulation seconds. Inference runs in the background while
simulation time pauses, then movement is animated. This makes CPU and GPU runs
use the same physics even when their inference latency differs. **Pause** waits
for the active native call and **Resume model** continues the current run.
Model mistakes can cause crashes, oscillation, or a timeout.

```powershell
./examples/winforms/run.ps1 Driving -Gpu
# Full-course diagnostics: normal model control, accelerated animation, seeded tracks.
dotnet run --project examples/winforms/Driving -c Release -- --gpu --benchmark build/driving --seeds 42,7,103
```

The benchmark saves outcomes, per-decision sensor/control traces, and rendered
form previews. A successful diagnostic process means it executed correctly;
check `results.json` to distinguish a finish from a model-caused collision.

## Pixel generation

Enter an ordinary image description, for example a house with a red roof, a tree,
or a striped flag. Choose a square resolution from 4–32 pixels per side (default 12).
Internal system prompts are defined in `PixelScene.cs`; they are never placed in
the visible image-description field. The planner prompt includes a worked flower
example to demonstrate connected shapes and background-to-foreground layer order.

The model first produces one shared composition: background, shapes, colors, bounds,
and layer order. The app validates this small JSON scene and allows one repair
attempt if the structure or bounds are invalid. It then computes shape membership
and occlusion at each target pixel, snapping shape bounds to the pixel grid so
small details do not disappear between sample centers. Shapes are rectangles,
ellipses, upward triangles, and diamonds. No scene templates or subject-specific
drawings are selected by the application.

Pixels are still visited in random order, exactly once each. Every pixel makes a
real Choice request over all 16 palette colors. It receives the user's description,
integer column/row, canvas resolution, pixel-center percentages, and the visible
surface derived from the shared plan. The color remains a model decision; there is
no post-hoc replacement of a disagreeing answer. The plan itself is model-authored;
the host performs rasterization math. Copying previously painted neighbors was less
reliable in experiments because it could spread an earlier mistake.

For this demo, download the large instruction model once:

```powershell
python scripts/download_model.py large
```

This fetches a hash-pinned Qwen3-4B-Instruct-2507 Q4_K_M GGUF (about 2.5 GB) and its
license. It uses the same native runtime and supports CPU or GPU. Source checkouts
prefer it automatically when present. In a release bundle, select it with Browse
or `--model` to override a light/medium bundle. This file is now the large release
profile; the earlier `download_pixel_model.py` command remains compatible. No GUI
download happens automatically.

This produces simple geometric illustrations rather than diffusion-model images.
Composition, proportions, and interpretation can still be wrong. Scene validation
checks syntax and geometry, not whether a drawing faithfully depicts the prompt.
A 32 × 32 image needs 1,024 pixel calls plus the planning request(s). **Stop** waits
for the current native call and preserves the partial canvas; unpainted pixels are
transparent when saved. The enlarged UI preview uses nearest-neighbor scaling,
while PNG export keeps the original small resolution.

To reproduce the comparison using the exact application planner and renderer:

```powershell
dotnet run --project examples/winforms/PixelArt.Benchmark -c Release -- models/downloads/Qwen3-4B-Instruct-2507-Q4_K_M.gguf build/pixel-comparison
```

It writes per-image PNGs, the model's scene JSON, a comparison sheet, and measured
results. Only the square and flag cases have a pixel-exact reference. Agreement
with a scene plan is a consistency metric, not a measure of semantic image quality.

## Lifetime, responsiveness and validation

Loading and inference run on worker threads. Each application has at most one
native call in flight. Stop/close requests wait for that call before disposing the
engine; there is no unsafe forced cancellation. GPU execution does not require an
application-side graphics context.

```powershell
dotnet run --project examples/winforms/Tests -c Release
# Real UI/model smoke tests: forms open briefly, exercise their normal workflows,
# write result.txt + preview.png, and exit with a nonzero code on failure.
dotnet run --project examples/winforms/Decisions -c Release -- --gpu --smoke-test build/ui-tests/decisions
dotnet run --project examples/winforms/Snake -c Release -- --gpu --smoke-test build/ui-tests/snake
dotnet run --project examples/winforms/PixelArt -c Release -- --gpu --smoke-test build/ui-tests/pixel-art
dotnet run --project examples/winforms/Driving -c Release -- --gpu --smoke-test build/ui-tests/driving
```

Publish each app into a separate directory. Native dependencies are copied by MSBuild;
models remain separate files selected by the user. A self-contained publish can
include the .NET Desktop runtime:

```powershell
dotnet publish examples/winforms/Decisions -c Release -r win-x64 --self-contained true -o dist/winforms/Decisions
```

`Eugeniusz.WinForms.sln` opens all four apps and the rules tests in Visual Studio.
Use `publish.ps1` to publish all four as self-contained Windows x64 applications.
If a local `dist/winforms` directory has already been published, launch the EXEs in
its four subdirectories directly. Keep each EXE with all files in its directory.
The published apps include .NET but keep model weights separate.

The original console example is fixed by the same runtime deployment/resolver:
`dotnet run --project examples/dotnet -c Release` now runs its ABI check without PATH
configuration. Add a GGUF path and optional `99` argument for real GPU inference.
