# Contributing

Keep code, documentation, comments and public issues in English. Use C++17 and
preserve the C ABI ownership rules. Do not add a framework dependency to the core.
Use the optional backend boundary for inference-specific dependencies.

Build and run CTest before proposing a change. Run Python and .NET FFI checks if
changing layout, exports or lifetime rules. Use the real model smoke test for
adapter changes. Add tests for meaningful behavior, numerical edge cases and
regressions. Do not substitute a mock backend for real inference validation.

Record model/version/hash/backend, prompts, data provenance and sample counts with
performance or quality claims. Keep training, calibration and test splits separate.
Never commit model weights, build outputs or credentials. Preserve upstream license
notices. Proposed contributions are licensed under this repository's MIT license.

This project is pre-1.0. Breaking ABI changes require an explicit version increment,
updated wrappers, migration notes and tests on all supported platforms.
