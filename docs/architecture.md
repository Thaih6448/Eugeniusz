# Architecture

```
Application (C / C++ / C# / Python / game engine)
    -> eugeniusz: validation, distributions, calibration, uncertainty
        -> backend callback
            -> eugeniusz_llama: Qwen3 prompt + selected label logits
                -> llama.cpp / ggml: CPU, Vulkan, CUDA or Metal
```

`eugeniusz` has only C++ standard-library and platform threading dependencies.
`eugeniusz_llama` is a separate optional shared/static native library. It owns model
weights and one inference context. The core owns a callback/user-data pair and calls
the backend under an engine mutex. C++ RAII, Python context managers and .NET
SafeHandle wrap the same ownership contract.

For every question, the adapter builds a fixed Qwen3 chat frame and maps the supplied
criteria to A–Z. It validates that each letter is a single token. Only fixed framing
is tokenized with special-token recognition; caller text cannot inject a native
chat-role token. This is not a semantic prompt-injection defense. No sensitive action
should be authorized solely by model output.

The context cache is cleared before each question. Input is processed in batches
of at most 512 tokens; overlong input fails instead of being silently truncated.
The last-position logits for candidate letters are copied to caller-owned results.
No answer tokens are generated. Normalization is restricted to the supplied labels:
these probabilities are conditional on the offered classes, not the full vocabulary.
An omitted correct category can therefore still produce an apparently certain result.

The optional completion API reuses the same loaded weights and context to generate
bounded text, for example a complete geometric scene before rendering pixels. It
is separate from label scoring. A per-backend mutex protects the shared context;
a provider registry rejects completion calls for non-llama callback engines.
Both paths clear their context at the beginning of each request. Role markers stay
fixed and caller text never receives special-token recognition. Hybrid Qwen3
templates disable thinking explicitly; Instruct-2507 uses a plain assistant prefix,
detected from the model's template rather than its filename.

Batches validate every question before inference, evaluate sequentially, and copy
outputs only after every evaluation succeeds. One question cannot read another
question's state. There is no advertised constant-time multi-question parallelism.
Multiple engines may execute concurrently at the cost of additional model/context
memory. Destruction must not race C/C++ use; Python and .NET protect managed handles.

CPU and GPU release variants share filenames and ABI but ship in separate directories.
Do not mix dependency DLLs from different llama.cpp revisions or backends in a process.
Load one native runtime per process. Profiles choose model weights and defaults,
not different C ABI layouts. Loading several profiles concurrently is not required.

Floating-point kernels can produce different scores across CPUs, GPU vendors,
thread counts and quantizations. There is no random sampling, but bitwise portability
is not promised. Calibration belongs to a specific model hash, prompt format,
criteria order, task distribution and numerical backend.
