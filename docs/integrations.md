# Host integrations

All examples use local files and an in-process C ABI. Pick exactly one backend
runtime directory. Keep the model loaded across repeated requests in a real service.
Move load/inference work off the UI/game thread; only use application objects on
their owning thread. Cancellation currently means ignoring a result, not interrupting
a native evaluation. Do not unload native DLLs while inference is running.

## .NET

Reference `bindings/dotnet/Eugeniusz/Eugeniusz.csproj`, or build it and reference
`Eugeniusz.Managed.dll`. It targets .NET Standard 2.1. The sample targets .NET 8.

```sh
dotnet build examples/dotnet/Example.csproj -c Release
```

The sample build automatically copies native DLLs from an installed runtime on
Windows (`build/install-vulkan`, `build/install-cpu`, `build/install`, or bundle
`bin`). Override the source using `-p:EugeniuszNativeDir=C:/runtime/bin`.
The .NET 8 samples use an explicit native resolver; no PATH edits are necessary.
`EUGENIUSZ_LIBRARY_DIR` overrides the runtime directory. On Unix keep dependencies
in the installed layout with their normal loader paths. Then run:

```sh
dotnet examples/dotnet/bin/Release/net8.0/Example.dll models/downloads/Qwen3-0.6B-Q8_0.gguf 99
```

Omit `99` for CPU. Running without a model argument performs only the C# ABI check.
Use `using`/`Dispose`; SafeHandle keeps an in-flight P/Invoke reference alive during
concurrent disposal. An engine's inference calls are serialized natively. For a
server, bound queues and request sizes to avoid resource exhaustion.

Three [WinForms examples](../examples/winforms/README.md) provide a Choice/Score/Truth
playground, model-controlled Snake, and a 16-color pixel generator. Open
`examples/winforms/Eugeniusz.WinForms.sln`, or run, for example:

```sh
dotnet run --project examples/winforms/Decisions -c Release
```

They target Windows x64 / .NET 8 and support CPU and GPU inference, model selection,
background evaluation, visible errors, and safe stop/close handling. WinForms is
Windows-only; the core library and console sample remain cross-platform. The
sample resolver is in `examples/common/SampleRuntime.cs`; hosts embedding the
.NET Standard wrapper should deploy the native runtime and configure resolution
for their own framework rather than relying on the source checkout.

## Unity 6.6 (desktop)

The current stable supported release reviewed on 2026-09-17 is
[Unity 6.6](https://discussions.unity.com/t/unity-6-6-is-now-available/1735357).
The sample follows the documented [native plugin interface](https://docs.unity3d.com/6000.6/Documentation/Manual/plug-ins-native.html).

1. Create a desktop project using .NET Standard 2.1 API compatibility.
2. Copy `bindings/dotnet/Eugeniusz/Engine.cs` into `Assets/Eugeniusz`, or import the
   compiled managed assembly, but not both.
3. Put the native runtime in `Assets/Plugins/x86_64` on Windows/Linux; use the
   matching native architecture and macOS plugin settings for Apple Silicon.
   Configure Plugin Inspector platform/CPU selections for every dependency.
4. Put the selected GGUF in `Assets/StreamingAssets`. Include its license in the
   distributed game. Large model files may be distributed as separately installed
   content; change the path to an ordinary local file in that case.
5. Attach `examples/unity/EugeniuszExample.cs` to a GameObject and set its model file.

The sample owns the engine in a worker task, disposes it there, and suppresses UI
callbacks after destruction. It illustrates one call; a production service should
retain an engine and serialize requests. Confirm both Editor and the intended
Mono/IL2CPP desktop player builds. IL2CPP support uses ordinary P/Invoke, but has not
been executed on this host. Android, iOS and WebGL are outside this example's scope.

## Unreal Engine 5.8 (desktop)

The current stable version reviewed is
[Unreal Engine 5.8](https://www.unrealengine.com/news/unreal-engine-5-8-is-now-available).
The example uses Epic's [third-party library integration](https://dev.epicgames.com/documentation/en-us/unreal-engine/integrating-third-party-libraries-into-unreal-engine)
and the C ABI, so enabling C++ exceptions or sharing STL objects with Unreal is unnecessary.

1. Copy `examples/unreal/EugeniuszPlugin` into your project's `Plugins` directory.
2. Copy an installed Eugeniusz SDK into the plugin's
   `ThirdParty/Eugeniusz/Win64`, `ThirdParty/Eugeniusz/Linux`, or
   `ThirdParty/Eugeniusz/Mac`. Preserve `include`, `lib`, and `bin` subdirectories.
3. Enable the plugin and regenerate/build the project. Its Build.cs links the
   import libraries and stages native runtime files next to the target executable.
4. Call the Blueprint async **Choose** node with an absolute local model path,
   state, question, criteria, and GPU flag. Connect **Completed** and inspect its
   error and abstention fields before acting.
5. Stage the GGUF as NonUFS content (an ordinary file outside a `.pak`), and construct
   its deployed path with your project's platform path utilities. Native mmap cannot
   read a virtual path inside a game archive.

The worker captures value data; the completion delegate executes on the game thread.
The sample loads per call to make ownership obvious; retain one engine in a subsystem
for repeated gameplay use. Mac distribution also requires correct install names and
codesigning. Keep inference within the game's RAM/VRAM/frame budget, especially when
rendering and the model share an 8 GB GPU. The example plugin is source provided for
integration; the Unreal/Unity editors were not installed or used for validation here.
