# API quality measurements — 2026-09-17

The current profiles use an explicit `Answer:` prefix and space-prefixed single-token
labels. Light (also called small) changes from Qwen3-0.6B Q4_0 to Q8_0; medium keeps
Qwen3-1.7B Q8_0; large changes to Qwen3-4B-Instruct-2507 Q4_K_M. All selected weights
are Apache-2.0, with immutable revisions, hashes, licenses and model cards retained.

## Corpus and interpretation

The frozen [dataset](../tests/data/api-quality-v1.jsonl) contains **100 Choice, 100 Score,
and 100 Truth prompts**. Truth is Eugeniusz's yes-probability primitive corresponding
to the requested Noul use case. It returns a probability, not a certainty flag.
Ten task families per API cover routing, extraction, rules, numeric/spatial reasoning,
negation, some Polish text, untrusted-state instructions, and ordered rubrics.
Choice criteria are deterministically shuffled; Truth has 50 positive and 50 negative labels.

Dataset SHA-256: `dae03a9c98a055df56b8aaa5542e92c1853a7617e3607d64ce675dc1ef115203`.
The generator and integrity tests are versioned. Labels were fixed before model testing.
There are 20 development and 80 evaluation cases per API, but evaluation results
were subsequently used for model selection: **this is not an untouched final holdout**.
Cases share authored templates and are not a representative sample of all user tasks.
No fine-tuning or temperature fitting was performed; all measurements use temperature 1.

Choice/Truth accuracy compares the most probable label with the reference. Score
accuracy also means the most probable rubric level, **not a rounded API value**. The
Score API returns the distribution-weighted mean; its error is reported separately.
Every call checks finite probabilities, normalization, valid indices, argmax, and
the exact Choice/Score/Truth value formula. Final GPU runs passed 900/900 such checks.

## Current profiles on Vulkan

Windows 11 x64; NVIDIA RTX 4060 Ti **16 GB**; pinned llama.cpp b6500 Vulkan runtime;
locally compiled MSVC adapter; four CPU threads; GPU layer request 99. Profile contexts
are 2,048 / 4,096 / 4,096. No other inference job ran alongside these final GPU runs.
Load time and three warmups are excluded. Values below are median / p95 milliseconds.

| Profile | Weights (decimal MB) | Choice /100 | Score /100 | Truth /100 | Choice ms | Score ms | Truth ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| small | 639 | 78 | 71 | 64 | 16.2 / 17.9 | 16.3 / 18.3 | 16.2 / 18.9 |
| medium | 1834 | 91 | 80 | 81 | 31.1 / 33.9 | 31.1 / 34.1 | 31.2 / 34.5 |
| large | 2497 | 97 | 92 | 95 | 51.7 / 52.7 | 52.2 / 53.1 | 51.8 / 52.8 |

One small/medium Score request took 2,994/2,681 ms after the three Truth warmups,
consistent with cold-kernel/shape warmup; the exact cause was not profiled.
These outliers remain in the raw results and their
means; median/p95 describe typical requests, not a bound on first-use latency.
Different contexts, longer prompts, drivers, or GPU contention can change latency.
The earlier candidate runs used context 4,096, including small; their timings are
not context-controlled speedup claims against the final small profile.

| Profile | Score mean absolute error (rubric index) | Score normalized MAE | Truth binary Brier | Wrong at confidence >= 0.9 (Choice / Score / Truth) |
| --- | ---: | ---: | ---: | --- |
| small | 0.572 | 0.165 | 0.231 | 7 / 13 / 10 |
| medium | 0.281 | 0.075 | 0.173 | 5 / 13 / 15 |
| large | 0.087 | 0.023 | 0.050 | 3 / 7 / 5 |

Raw reports also contain negative log-likelihood, ten-bin ECE, multiclass Brier,
per-family results, split summaries, and every prompt, label, logit and probability.
**Small remains weak for logical/numeric decisions**. Medium is a speed/quality
compromise. Large performs best overall here but still makes highly confident errors.
Softmax confidence is not a calibrated probability that an arbitrary answer is correct.
Refit existing temperature calibration after changing weights or answer framing.

The final CPU-only small run also tested all 300 cases: 78/71/65 correct; medians
274/301/287 ms. One Truth argmax differs from GPU; numerical behavior is not promised
to be bit-identical across backends. Medium/large CPU performance was not evaluated.

## Before/after and rejected alternatives

All rows below contain 100 cases per API. "Original" uses the previous answer
framing. "Prefix" uses the current `Answer:` + space-prefixed labels. Candidate
timings use context 4,096 and are individual runs rather than rigorous microbenchmarks.

| Report / model configuration | Choice | Score | Truth | Median ms (Choice / Score / Truth) |
| --- | ---: | ---: | ---: | --- |
| [original-small](benchmarks/api-quality/original-small.json): Qwen3-0.6B-Q4_0.gguf | 66 | 34 | 60 | 15.6 / 15.9 / 15.9 |
| [original-medium](benchmarks/api-quality/original-medium.json): Qwen3-1.7B-Q8_0.gguf | 93 | 62 | 77 | 31.8 / 31.5 / 32.1 |
| [original-large](benchmarks/api-quality/original-large.json): Qwen3-4B-Q4_K_M.gguf | 98 | 92 | 89 | 51.8 / 52.3 / 52.2 |
| [prefix-small-q4](benchmarks/api-quality/prefix-small-q4.json): Qwen3-0.6B-Q4_0.gguf | 71 | 62 | 63 | 20.7 / 20.9 / 20.8 |
| [prefix-small-q8](benchmarks/api-quality/prefix-small-q8.json): Qwen3-0.6B-Q8_0.gguf | 78 | 71 | 64 | 20.3 / 20.6 / 20.8 |
| [prefix-medium](benchmarks/api-quality/prefix-medium.json): Qwen3-1.7B-Q8_0.gguf | 91 | 80 | 81 | 30.3 / 30.5 / 30.5 |
| [prefix-large](benchmarks/api-quality/prefix-large.json): Qwen3-4B-Instruct-2507-Q4_K_M.gguf | 97 | 92 | 95 | 51.9 / 52.1 / 51.7 |
| [qwen25-small-q4](benchmarks/api-quality/qwen25-small-q4.json): qwen2.5-0.5b-instruct-q4_k_m.gguf | 54 | 44 | 55 | 12.1 / 12.2 / 12.1 |
| [qwen25-medium-q4](benchmarks/api-quality/qwen25-medium-q4.json): qwen2.5-1.5b-instruct-q4_k_m.gguf | 71 | 81 | 70 | 22.6 / 22.7 / 22.7 |
| [smollm2-small-q8](benchmarks/api-quality/smollm2-small-q8.json): smollm2-360m-instruct-q8_0.gguf | 20 | 21 | 50 | 20.3 / 20.3 / 20.2 |
| [prefix-qwen25-small](benchmarks/api-quality/prefix-qwen25-small.json): qwen2.5-0.5b-instruct-q4_k_m.gguf | 55 | 55 | 54 | 12.7 / 12.9 / 12.9 |
| [prefix-qwen25-medium](benchmarks/api-quality/prefix-qwen25-medium.json): qwen2.5-1.5b-instruct-q4_k_m.gguf | 71 | 80 | 71 | 23.0 / 23.2 / 23.0 |
| [prefix-smollm2-medium](benchmarks/api-quality/prefix-smollm2-medium.json): smollm2-1.7b-instruct-q4_k_m.gguf | 61 | 61 | 61 | 33.5 / 33.8 / 33.6 |
| [instruct-medium-q3](benchmarks/api-quality/instruct-medium-q3.json): Qwen3-4B-Instruct-2507-Q3_K_S.gguf | 98 | 85 | 92 | 69.1 / 69.9 / 69.6 |

The answer prefix improves medium Score from 62 to 80 and Truth from 77 to 81,
with a Choice tradeoff from 93 to 91. More precise small weights add improvements
beyond formatting alone, at a storage increase from 429 to 639 MB. Large improves
Truth from 89 to 95 and retains Score 92, with Choice 98 to 97. These small differences
are not statistical proof of universal superiority. The replacement large weights
are approximately the same size as before. No artificial confidence correction,
answer heuristic, or test-case lookup is used in the runtime.

Qwen2.5 0.5B/1.5B and SmolLM2 360M/1.7B did not improve the overall tradeoff.
Qwen3-4B-Instruct Q3_K_S (1,887 MB) produced 98/85/92 but took about 70 ms versus
52 ms for Q4_K_M. It was therefore rejected as a medium replacement. Development
experiments with longer system instructions, few-shot examples and No/Yes Truth
labels were not adopted; their `*-dev.json` files contain **20 cases per API**, not 100.

## Can the model be cut down?

Read-only GGUF inspection found **no separate `output.weight` in any selected model**.
All three reuse the input embedding matrix as the output projection. Their embedding
storage is approximately 165 MB / 331 MB / 319 MB. Deleting vocabulary rows would
remove representations needed to read arbitrary input. Transformer blocks still
perform the reasoning needed for all three APIs; there is no isolated Noul/Score
module to extract. Tensor inventories, including shapes and byte counts, are retained.

A custom llama.cpp/ggml graph could project only the requested label rows while
keeping all input embeddings. This might reduce final projection work, but the
current runtime computes the full vocabulary head; no measured acceleration or
pruned artifact is claimed. [SliceGPT](https://arxiv.org/abs/2401.15024) and
[LLM-Pruner](https://arxiv.org/abs/2305.11627) describe structural compression with
additional procedures and recovery/validation requirements. Neither establishes
a speed/quality gain for this deployment. We kept full models, tested quantization,
and improved the answer framing instead. Task-specific distillation remains future work.

## Reproduction and evidence

```sh
python scripts/download_model.py small
python scripts/download_model.py medium
python scripts/download_model.py large
python scripts/benchmark_api.py --name small --model models/downloads/Qwen3-0.6B-Q8_0.gguf --context 2048 --output build/quality-small.json
python scripts/benchmark_api.py --name medium --model models/downloads/Qwen3-1.7B-Q8_0.gguf --output build/quality-medium.json
python scripts/benchmark_api.py --name large --model models/downloads/Qwen3-4B-Instruct-2507-Q4_K_M.gguf --output build/quality-large.json
# CPU: add --cpu --library-dir build/install-cpu
python scripts/inspect_gguf.py models/downloads/Qwen3-0.6B-Q8_0.gguf --output build/small-tensors.json
python -m unittest discover -s tests -p test_api_dataset.py -v
```

The benchmark defaults to `build/install-vulkan`; pass your installed runtime using
`--library-dir` on another platform. Python uses only the standard library and the
existing Eugeniusz binding. Linux/macOS measurements have not been performed here.

[Raw evidence](benchmarks/api-quality/README.md) records provenance, experimental
framing, and legacy-model reproduction. Current profiles are pinned in
`models/profiles.json`; previous weights in `models/profiles.previous.json`; all
additional downloads in `models/candidates.json`. The baseline adapter source is
archived for comparison, not compiled as part of the current library.

Primary model sources: [Qwen3 0.6B](https://huggingface.co/Qwen/Qwen3-0.6B),
[Qwen2.5 GGUF](https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF),
[SmolLM2](https://huggingface.co/HuggingFaceTB/SmolLM2-1.7B-Instruct),
[selected large GGUF](https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF).
