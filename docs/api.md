# Native API contract (ABI 1, version 0.1.0)

Headers: `include/eugeniusz/eugeniusz.h`, `llama.h`, `eugeniusz.hpp`, `llama.hpp`.
All C exports use C linkage and the platform C calling convention (`cdecl` on
Windows). Strings are null-terminated UTF-8; embedded NUL is unsupported. The
wrappers reject embedded NUL instead of silently truncating their input.

Create questions with `eg_question_default()` (temperature 1, threshold 0).
Zero-initializing this structure alone leaves an invalid temperature of zero.

Model defaults in C/C++, Python (`threads=None`) and .NET (`ModelOptions.Default`)
come from `eg_llama_options_default()`: 4096 context tokens, between 1 and 8 CPU
threads according to hardware concurrency, and no GPU offload. Specify `threads`
explicitly when reproducing benchmarks. Reading .NET model defaults requires the
native inference adapter to be available.

| Kind | Criteria | Result `value` |
| --- | --- | --- |
| `EG_CHOICE` | 2–26 distinct descriptions in caller order | Winning zero-based index |
| `EG_SCORE` | 2–26 ordered rubric levels | Sum of `index * probability` |
| `EG_TRUTH` | False then true, or null criteria and count 0 for defaults | `P(true)` |

`choice` is the maximum-probability index, with first-index tie-breaking. All kinds
return probabilities and raw selected-label logits in fixed arrays; only the first
`count` entries are meaningful. `confidence=max(p)` and `certainty=1-H(p)/log(count)`
are explicitly defined local statistics, not Jev-compatible confidence formulas.
`abstained` means `confidence < min_confidence`. The proposed answer remains available
even when abstained; the host is responsible for honoring that flag.

Temperatures must be finite and within [0.05,20]; thresholds within [0,1]. Logits
must be finite and have magnitude at most 1e100. Native functions validate values,
not arbitrary invalid memory addresses supplied by the host. Pass correctly sized,
live arrays. Invalid buffers or concurrent destruction are caller errors.

`eg_llama_create()` borrows model path/options and creates an owned engine. Destroy
it with `eg_engine_destroy()`, never `free()` or a foreign allocator. A callback
engine takes ownership of its user data only if `eg_engine_create()` succeeds.
The optional destructor callback must not throw. A callback writes every requested
logit and must not reenter the same engine.

`eg_llama_create_with_system_prompt()` adds per-engine task instructions without
changing the existing options layout or ABI version. The prompt is copied and
must be nonempty UTF-8. It is tokenized as ordinary text inside adapter-owned role
framing. Python accepts `load_model(..., system_prompt=...)`; C# and C++ provide
three-argument load overloads. Default load behavior is preserved.

`eg_llama_generate()` is a separate, optional bounded greedy completion API for
tasks such as constructing a reusable image plan. It accepts only engines created
by the llama adapter, a per-call system prompt, user text, and a 1–4096 token budget.
The output buffer receives UTF-8 plus NUL only on success. Exhausting the token
budget, exceeding context capacity, or providing insufficient output space is an
error; no partial text is returned. It uses its caller-provided error buffer.
Generation and inference share an engine and serialize access to its context.
Keep the engine alive for the entire call, as with typed evaluation. The Python
`generate`, C# `Generate`, and C++ `generate` wrappers provide a 64 KiB output buffer.
The Choice/Score/Truth path still produces no answer tokens.

Status-returning core calls catch C++ exceptions. `eg_last_error()` is thread-local,
valid until the next such core call on that thread. `eg_llama_create()` instead
writes to its caller-provided error buffer. Outputs remain unchanged on failure,
including an entire `eg_evaluate_batch()` output array. No partial success is hidden.

Use `eg_from_logits()` to integrate a separately trained classifier without a
language model. `eg_fit_temperature()` and `eg_measure()` take row-major arrays
of logits with one integer true label per row. The header documents conformal
functions and their bitmask format. C# exposes inference and from-logits; full
calibration utilities are available through the C API and Python wrapper.

The current typed adapter prefills `Answer:` and scores space-prefixed A–Z tokens.
This is a prompt-format change, not an ABI or result-semantics change. Refit any
calibration obtained with the older empty-answer/bare-letter prompt. The release
model profiles were re-evaluated on 100 cases per kind; see `model-quality.md`.

Supported initial targets: 64-bit Windows/MSVC, Linux/GCC or Clang, and macOS/Apple
Clang. CI definitions exercise these targets. 32-bit ABI portability is not tested.
