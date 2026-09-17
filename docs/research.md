# Research and design rationale

Sources reviewed on 2026-09-17. Separate public facts from hypotheses about a
closed model. No proprietary implementation or private weights were accessed.

## What Jev publicly exposes

TypeSafe describes a decision-oriented system with Choice, Score and Noul
primitives. Its documentation says questions are isolated against shared state.
Choice returns a class distribution, Score uses ordered levels, and Noul returns
a yes probability. TypeSafe describes RLCD and a parallel sampler, but the linked
materials do not publish enough architecture, training data or algorithm detail
to reproduce their system.

Primary sources: [product](https://typesafe.ai/),
[introduction](https://docs.typesafe.ai/introduction),
[Choice](https://docs.typesafe.ai/primitives/choice),
[Score](https://docs.typesafe.ai/primitives/score),
[Noul](https://docs.typesafe.ai/primitives/noul),
[confidence](https://docs.typesafe.ai/confidence),
[training primer](https://docs.typesafe.ai/introduction/machine-learning-primer),
[launch article](https://typesafe.ai/blog/introducing-system-one-models-and-jev).

Type constraints can prevent an invalid output category. They cannot prove that a
valid category is factually correct. Eugeniusz explicitly makes this distinction.

## Plausible designs, not claims about Jev's implementation

1. A pretrained transformer could produce a state representation and feed
   discriminative heads conditioned on question/criteria representations.
2. A shared state encoder could serve independent question branches in a batched
   attention graph. This could amortize input encoding while avoiding question
   cross-contamination.
3. A proper scoring objective, supervised soft targets, post-hoc calibration or
   a reinforcement objective could train predictive distributions rather than
   preferred prose. The public term RLCD alone does not specify the objective.

These are architectural hypotheses. A serious reproduction project needs datasets,
held-out evaluations and training, not just prompt formatting.

## Papers and practical consequences

| Primary source | Relevant result / idea | Consequence for Eugeniusz |
| --- | --- | --- |
| Guo et al., 2017, [On Calibration of Modern Neural Networks](https://arxiv.org/abs/1706.04599) | Neural confidence can be miscalibrated; scalar temperature is a useful post-hoc method. | Fit positive temperature using labeled held-out logits. Measure on another split. |
| Zhao et al., 2021, [Calibrate Before Use](https://arxiv.org/abs/2102.09690) | Prompt/label priors affect few-shot predictions; contextual calibration can reduce bias. | Preserve logits and record exact question/criteria ordering. Prior correction is a future experiment. |
| Kadavath et al., 2022, [Language Models (Mostly) Know What They Know](https://arxiv.org/abs/2207.05221) | Correctness estimates depend on task and format, and transfer is imperfect. | A truth probability is a task prediction, not universal self-knowledge. |
| Cho et al., 2024, [Token-based Decision Criteria Are Suboptimal in In-context Learning](https://arxiv.org/abs/2406.16535) | Hidden-state classifiers can outperform label-token rules in the studied setting. | The current token head is a baseline; the callback ABI allows a trained representation classifier later. |
| Angelopoulos and Bates, 2021, [A Gentle Introduction to Conformal Prediction](https://arxiv.org/abs/2107.07511) | Prediction sets can obtain marginal coverage under exchangeability using a calibration split. | Implement split conformal sets; document assumptions and separate fitting/calibration/test data. |
| Hinton et al., 2015, [Distilling the Knowledge in a Neural Network](https://arxiv.org/abs/1503.02531) | Soft teacher distributions can transfer information to a smaller student. | A future specialist light model can use licensed task data and teacher distributions; this repository does not pretend to have trained one. |
| Qwen team, 2025, [Qwen3 Technical Report](https://arxiv.org/abs/2505.09388) | Open Qwen3 models offer different parameter budgets and thinking modes. | Pin dense 0.6B, 1.7B and 4B models; use the non-thinking response prefix. |

## Why this initial backend

[llama.cpp](https://github.com/ggml-org/llama.cpp) provides C/C++ GGUF inference,
quantization, CPU execution and several GPU backends under MIT. It allows deployment
without a Python ML framework. The [Qwen3 4B GGUF model](https://huggingface.co/Qwen/Qwen3-4B-GGUF)
and the other selected models are Apache-2.0; exact revisions and file hashes are
in `models/profiles.json`. This is a manageable first implementation with inspectable
numerics, not a claim of the best available model in every domain.

The adapter assigns criteria to single-token labels A–Z, prefills one prompt,
reads those labels' logits, and applies softmax. This avoids answer generation,
length bias between long criterion strings, and a JSON parser. It still inherits
letter bias, prompt sensitivity and general model errors. Scores are distributions
over rubric indices; the mean is returned as the score.

## Optimization experiments worth doing next

Profile prefill, attention, vocabulary projection, data transfer and allocation
separately. Then compare: persistent prompt/KV caching; batched independent question
branches; an encoder plus trained classification head; distillation; and a restricted
output projection. Any optimization must preserve both accuracy and calibration.

Only 26 output rows are consumed here, but the generic backend still computes the
full vocabulary head. A custom ggml graph could project selected rows while keeping
the input embedding matrix intact. With tied embeddings, deleting vocabulary rows
from the GGUF file would also remove input representations and is not a safe general
optimization. Transformer blocks are still needed to understand arbitrary text.
No undocumented weight surgery is included in v0.1. The existing quantized light
profile provides a measured size reduction without changing tokenizer semantics.
