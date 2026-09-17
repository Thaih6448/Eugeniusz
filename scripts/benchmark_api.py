"""Evaluate typed API quality and latency; raw model probabilities are uncalibrated."""
import argparse
import datetime
import hashlib
import json
import math
from pathlib import Path
import platform
import random
import statistics
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "bindings/python"))
from eugeniusz import Runtime, Question, CHOICE, SCORE, TRUTH
from download_model import digest
from benchmark import gpu_info


def summarize(rows):
    if not rows:
        raise ValueError("Cannot summarize an empty set of benchmark results")
    correct = [int(r["choice"] == r["label"]) for r in rows]
    times = sorted(r["latency_ms"] for r in rows)
    ece = 0
    for index in range(10):
        group = [(r, ok) for r, ok in zip(rows, correct) if min(9, int(r["confidence"] * 10)) == index]
        if group:
            ece += len(group) / len(rows) * abs(statistics.mean(r["confidence"] for r, _ in group) - statistics.mean(ok for _, ok in group))
    result = dict(count=len(rows), correct=sum(correct), accuracy=statistics.mean(correct),
                  median_ms=statistics.median(times), p95_ms=times[math.ceil(.95*len(times))-1],
                  mean_ms=statistics.mean(times), ece=ece,
                  nll=statistics.mean(-math.log(max(1e-15, r["probabilities"][r["label"]])) for r in rows),
                  high_confidence_errors=sum(not ok and r["confidence"] >= .9 for r, ok in zip(rows, correct)),
                  brier=statistics.mean(sum((p-int(i == r["label"]))**2 for i, p in enumerate(r["probabilities"])) for r in rows))
    if rows[0]["kind"] == "score":
        result.update(value_mae=statistics.mean(abs(r["value"]-r["label"]) for r in rows),
                      normalized_mae=statistics.mean(abs(r["value"]-r["label"])/(len(r["probabilities"])-1) for r in rows))
    if rows[0]["kind"] == "truth":
        result["binary_brier"] = statistics.mean((r["value"]-r["label"])**2 for r in rows)
    return result


def summarize_kinds(rows):
    return {kind: summarize([r for r in rows if r["kind"] == kind])
            for kind in sorted({r["kind"] for r in rows})}


def validate_result(result, kind):
    probabilities = result.probabilities
    count = len(probabilities)
    if not 2 <= count <= 26 or (kind == TRUTH and count != 2):
        raise ValueError("API invariant failed: invalid probability count")
    if not all(math.isfinite(p) and 0 <= p <= 1 for p in probabilities):
        raise ValueError("API invariant failed: invalid probability")
    if abs(sum(probabilities) - 1) >= 1e-9:
        raise ValueError("API invariant failed: probabilities do not sum to one")
    if result.choice != max(range(count), key=lambda j: probabilities[j]):
        raise ValueError("API invariant failed: choice is not the probability argmax")
    expected = result.choice if kind == CHOICE else probabilities[1] if kind == TRUTH else sum(j*p for j, p in enumerate(probabilities))
    if not math.isfinite(result.value) or abs(result.value - expected) >= 1e-9:
        raise ValueError("API invariant failed: incorrect typed value")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--name", required=True)
    parser.add_argument("--library-dir", type=Path, default=ROOT / "build/install-vulkan")
    parser.add_argument("--dataset", type=Path, default=ROOT / "tests/data/api-quality-v1.jsonl")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--cpu", action="store_true")
    parser.add_argument("--threads", type=int, default=4)
    parser.add_argument("--context", type=int, default=4096)
    parser.add_argument("--split", choices=("all", "development", "evaluation"), default="all")
    parser.add_argument("--system", type=Path)
    args = parser.parse_args()
    dataset = args.dataset.read_bytes()
    cases = [json.loads(line) for line in dataset.decode("utf-8").splitlines()]
    if args.split != "all": cases = [c for c in cases if c["split"] == args.split]
    if not cases:
        parser.error("The selected dataset/split contains no cases")
    random.Random(927).shuffle(cases)
    runtime = Runtime(args.library_dir)
    adapter_names = ("eugeniusz_llama.dll", "libeugeniusz_llama.so", "libeugeniusz_llama.dylib")
    adapter = next((folder / name for folder in (args.library_dir, args.library_dir / "bin", args.library_dir / "lib") for name in adapter_names if (folder / name).is_file()), None)
    if adapter is None: raise FileNotFoundError("Cannot find the adapter for provenance hashing")
    rows = []
    report = dict(name=args.name, model=args.model.name, model_sha256=digest(args.model), model_bytes=args.model.stat().st_size,
                  dataset_sha256=hashlib.sha256(dataset).hexdigest(), utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                  platform=platform.platform(), gpu=None if args.cpu else gpu_info(), gpu_layers=0 if args.cpu else 99,
                  threads=args.threads, context=args.context, temperature=1, adapter_sha256=digest(adapter),
                  system_prompt=args.system.read_text(encoding="utf-8") if args.system else None,
                  note="Authored diagnostic, not a public benchmark. Development/evaluation labels were frozen before model tests; evaluation results used for model selection are not an untouched final holdout. Latency excludes loading and warmup. No calibration or model fine-tuning.")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    start = time.perf_counter()
    with runtime.load_model(args.model, context_size=args.context, threads=args.threads, gpu_layers=report["gpu_layers"], system_prompt=report["system_prompt"]) as engine:
        report["load_seconds"] = time.perf_counter() - start
        for _ in range(3): engine.truth("The lamp is on.", "Is the lamp on?")
        for index, case in enumerate(cases):
            start = time.perf_counter()
            kind = {"choice": CHOICE, "score": SCORE, "truth": TRUTH}[case["kind"]]
            result = engine.evaluate(case["state"], [Question(case["question"], tuple(case["criteria"]), kind)])[0]
            elapsed = (time.perf_counter()-start)*1000
            probabilities = list(result.probabilities)
            validate_result(result, kind)
            rows.append(dict(**case, choice=result.choice, value=result.value, probabilities=probabilities,
                             logits=list(result.logits), confidence=result.confidence, latency_ms=elapsed))
            if (index+1) % 25 == 0: print(f"{args.name}: {index+1}/{len(cases)}", flush=True)
    report["summary"] = summarize_kinds(rows)
    report["by_split"] = {split: summarize_kinds([r for r in rows if r["split"] == split]) for split in sorted({r["split"] for r in rows})}
    report["by_family"] = {f"{kind}/{family}": summarize([r for r in rows if r["kind"] == kind and r["family"] == family]) for kind, family in sorted({(r["kind"], r["family"]) for r in rows})}
    report["api_invariants_passed"] = len(rows)
    report["cases"] = rows
    args.output.write_text(json.dumps(report, indent=2, ensure_ascii=False)+"\n", encoding="utf-8")
    print(json.dumps(report["summary"], indent=2), flush=True)


if __name__ == "__main__":
    main()
