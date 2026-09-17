# Roadmap

Version 0.1 is a working local inference and calibration baseline. The next work
should be driven by measured application needs:

1. Establish a licensed, representative multilingual evaluation corpus covering
   routing, rubrics, factual propositions, ambiguity and out-of-distribution inputs.
2. Measure latency/VRAM on an actual RTX 4060 8 GB while the target application is
   rendering; benchmark context length scaling and cold-start costs.
3. Train and release task calibration artifacts with reproducible split provenance.
4. Compare the token head against a trained discriminative head and distilled light
   specialist. Keep held-out accuracy and risk/coverage as acceptance criteria.
5. Profile selected-vocabulary projection, prefix caching and shared-state independent
   question branches before modifying model graphs or tensors.
6. Add native cancellation, resource budgets and more model-specific chat templates.
7. Run full packaged-player tests in Unity and Unreal on all three desktop platforms.

No entry above is silently implemented or represented as completed functionality.
