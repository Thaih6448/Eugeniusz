# Calibration and decision policy

For raw candidate logits `z`, Eugeniusz computes:

```
p_i = exp((z_i - max(z)) / T) / sum_j exp((z_j - max(z)) / T)
score = sum_i i * p_i
truth = p_1                         # false=0, true=1
confidence = max_i p_i
certainty = 1 - H(p) / log(K)
```

Temperature 1 returns an **uncalibrated** model distribution. Calling softmax or
returning a float in [0,1] does not make a probability calibrated. Even a calibrated
model can be confidently wrong on an individual example or under distribution shift.

Use four disjoint data partitions when training a specialist: training, temperature
fitting, conformal calibration, and final testing. For an unchanged pretrained model,
the last three are sufficient. Keep task labels, descriptions, order, model hash,
quantization and prompt version fixed. Split by time/source/entity when near-duplicate
examples would otherwise leak across partitions.

```python
from eugeniusz import Runtime
runtime = Runtime("build/install-cpu")
# fit_logits and test_logits are lists of raw result.logits rows collected earlier.
temperature = runtime.fit_temperature(fit_logits, fit_labels)
before = runtime.measure(test_logits, test_labels)
after = runtime.measure(test_logits, test_labels, temperature)
print(temperature, before, after)
```

The fitter minimizes mean negative log-likelihood using a bounded search over
inverse temperature, where the objective is convex. Temperature scaling preserves
the winning class and does not repair classification accuracy. A boundary result
or no measurable improvement is a diagnostic, not evidence of success.

Metrics: mean NLL, multiclass Brier sum (range 0–2), equal-width top-label ECE, and
accuracy. ECE depends on binning and sample size. Report uncertainty intervals and
reliability plots in a serious evaluation; a low ECE on a tiny sample is weak evidence.

For split conformal classification, fit `1 - p_true` on a separate calibration set.
The quantile rank is `ceil((n+1)*(1-alpha))`. If that rank exceeds `n`, all classes
are included. The set includes class `i` when `1-p_i <= quantile`; it may be empty,
a singleton, or multiple classes. Coverage is marginal under exchangeability, not
per-example, subgroup or adversarial coverage. Reusing temperature-fitting examples
for conformal calibration invalidates the simple split argument.

```python
q = runtime.conformal_fit(conformal_true_probabilities, alpha=0.1)
prediction_set = runtime.conformal_set(new_result.probabilities, q)
if len(prediction_set) != 1 or new_result.abstained:
    request_review()
```

Choose thresholds using held-out risk/coverage curves and the actual cost of mistakes.
The examples' thresholds are placeholders, not recommendations for a real application.
Persist metadata with fitted parameters, and re-evaluate when the model, prompt,
class list, backend or deployment data changes.

The current `Answer:` / space-prefixed-label adapter and refreshed small/large
profiles change the logits. Old calibration values must not be reused. The
300-case diagnostic reports raw NLL, Brier, ECE, and high-confidence errors; it
does not fit a universal temperature or certify probability calibration.
