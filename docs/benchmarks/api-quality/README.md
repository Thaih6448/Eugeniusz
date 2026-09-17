# API quality evidence

See [analysis and reproduction](../../model-quality.md). Reports retain the complete
corpus outputs, weight and adapter hashes, platform, GPU, context, temperature,
load time, system-prompt override, per-case latency, and aggregate metrics.

- `final-small`, `final-medium`, `final-large`: current GPU profiles, 300 cases each.
- `final-small-cpu`: current small CPU-only backend, 300 cases.
- `original-*`: previous weights and bare-letter framing, 300 cases each.
- `prefix-*`: explicit `Answer:` + space-prefixed labels (current framing).
- `qwen25-*`, `smollm2-small-q8`, `instruct-large`: original bare-letter framing.
- `instruct-medium-q3`: current framing with the rejected 4B Q3_K_S weights.
- `small-simple-dev` and `small-examples-dev`: original small model/framing with
  the complete system override stored in the report and `reference/`.
- `verbal-*-dev`: current letter framing for Choice/Score, experimental literal
  space-prefixed `No`/`Yes` tokens for Truth, with `Answer No or Yes.` instructions.
  This Truth variant was reverted. Development files contain only 60 cases.
- `*-tensors`: read-only GGUF storage inventories, not performance reports.

The baseline source in `reference/llama_backend_before_prefix.cpp` records the
previous framing (empty assistant prefix and bare A-Z tokens), plus the candidate
architecture acceptance needed for Qwen2/SmolLM2. To reproduce old-model results,
use a separate source/build directory with this adapter and the same pinned llama.cpp;
never mix reports from different framing as if only the weights changed.
Download legacy/candidate weights with `scripts/download_candidate_model.py NAME`,
using names from `models/candidates.json`. Context 4096 was used for candidate runs;
final-small uses the release profile's 2048 context.

These authored cases and selected evaluation results informed model selection.
They are not a final untouched test set, calibrated reliability guarantee, or a
claim of equivalence to TypeSafe AI. Raw timings retain cold/shape shader outliers.
No binary runtime or model weights are stored in this evidence directory.
