# Building and deployment

Use CMake 3.20+, C++17 and a 64-bit toolchain. CPU builds require no ML framework.
The core configures offline. Enabling `EUGENIUSZ_LLAMA` downloads the pinned upstream
archive unless `EUGENIUSZ_LLAMA_SOURCE=/absolute/path/to/llama.cpp` is supplied. That
local source must match b6500; arbitrary versions are not ABI compatible.

## CPU

```sh
cmake -S . -B build/cpu -DEUGENIUSZ_LLAMA=ON -DCMAKE_BUILD_TYPE=Release
cmake --build build/cpu --config Release --parallel
ctest --test-dir build/cpu -C Release --output-on-failure
cmake --install build/cpu --config Release --prefix build/install-cpu
```

Windows: use a Visual Studio Developer PowerShell/Command Prompt. Both Ninja and
Visual Studio generators work with MSVC. Local validation used VS 2026 + Ninja;
the CI matrix uses Windows 2022 and its MSVC toolchain. The locally installed MinGW
headers lack symbols used by upstream ggml, so MSVC is the supported Windows path.
Default x86 llama.cpp CPU code requires AVX2/FMA/F16C/BMI2. For an older x86 CPU,
disable those upstream features explicitly (`GGML_AVX2`, `GGML_AVX`, `GGML_FMA`,
`GGML_F16C`, `GGML_BMI2`, `GGML_SSE42`) and validate the resulting target build.

The default Windows build uses the dynamic MSVC runtime. Deploy the matching
Microsoft Visual C++ redistributable (often already present), or Microsoft's
permitted app-local runtime files with your application. `EUGENIUSZ_STATIC_RUNTIME`
is experimental: the local VS 2026 shared-backend /MT build failed during startup,
so it is off and is not used for the verified packages.

Linux: GCC/Clang, CMake and Make/Ninja suffice. macOS: install Xcode command line
tools and CMake; use native architecture builds (arm64 for Apple Silicon).

## GPU from source

```sh
cmake -S . -B build/gpu -DEUGENIUSZ_LLAMA=ON -DEUGENIUSZ_ACCELERATOR=VULKAN -DCMAKE_BUILD_TYPE=Release
cmake --build build/gpu --config Release --parallel
cmake --install build/gpu --config Release --prefix build/install-gpu
```

Vulkan needs a build-time SDK containing headers, the loader import library and
`glslc`. CUDA needs a compatible CUDA toolkit and host compiler; pass
`-DCMAKE_CUDA_ARCHITECTURES=89` for an RTX 4060 target. CUDA runtime deployment follows
the selected toolkit's redistributable rules. Metal requires macOS and is selected
with `-DEUGENIUSZ_ACCELERATOR=METAL`. Do not install CUDA on macOS.

These are separate backend variants of the same API, not libraries to load together.
The CPU library remains usable without a GPU toolkit or GPU driver.

## Windows Vulkan without a GPU SDK

After building/installing the CPU adapter with MSVC:

```sh
python scripts/stage_windows_vulkan.py --cpu-prefix build/install-cpu --output build/install-vulkan
```

The helper verifies a pinned b6500 release archive, copies its DLLs beside the local
adapter and retains provenance. This was the GPU validation path used on this host.
It does not claim that the upstream GPU kernels were compiled here. The plugin
loader resolves dependencies beside the adapter, so Python and .NET hosts work
without using the current working directory as a backend search directory.

Only the matching GPU driver and runtime DLLs are needed on the target machine.
The staged upstream runtime additionally uses LLVM OpenMP; keep its DLL and license.
Do not move only `eugeniusz_llama.dll` and leave its dependencies behind.

## Using an installed SDK

```cmake
find_package(Eugeniusz CONFIG REQUIRED)
add_executable(my_app main.cpp)
target_link_libraries(my_app PRIVATE Eugeniusz::llama)
```

Configure the consumer with `-DCMAKE_PREFIX_PATH=/path/to/installed/sdk`.
Shared libraries reside in `bin` (Windows) or `lib` (Linux/macOS). The native
installation uses relative loader paths on Unix systems. Ensure the application's
loader can find this directory. On Windows copy dependency DLLs beside the executable;
on Linux use an application RPATH or `LD_LIBRARY_PATH`, on macOS an appropriate
`@rpath`/`@loader_path` layout and signing for distribution.

`BUILD_SHARED_LIBS=OFF` supports core-only static embedding. Static llama builds
depend on upstream static package metadata and platform libraries and are not part
of the verified release matrix. Managed integrations use shared libraries.
