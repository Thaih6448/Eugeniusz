"""Run a small reproducible diagnostic set; this is not a general quality benchmark."""
import argparse
import json
import os
from pathlib import Path
import platform
import statistics
import subprocess
import sys
import threading
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "bindings/python"))
from eugeniusz import Runtime, Question, CHOICE, SCORE, TRUTH

ROUTING = ("Billing: payments, charges and invoices", "Shipping: deliveries and lost parcels", "Technical: software faults")
CASES = [
    ("I was charged twice for my subscription.", "Which team should handle this ticket?", ROUTING, CHOICE, 0),
    ("My package has not arrived after three weeks.", "Which team should handle this ticket?", ROUTING, CHOICE, 1),
    ("The application crashes whenever I press Save.", "Which team should handle this ticket?", ROUTING, CHOICE, 2),
    ("Please send me an invoice for last month's payment.", "Which team should handle this ticket?", ROUTING, CHOICE, 0),
    ("Where is my parcel? The tracking page says it is lost.", "Which team should handle this ticket?", ROUTING, CHOICE, 1),
    ("The parcel arrived yesterday.", "Has the parcel arrived?", (), TRUTH, 1),
    ("The parcel did not arrive.", "Has the parcel arrived?", (), TRUTH, 0),
    ("I would like a refund.", "Is the customer requesting a refund?", (), TRUTH, 1),
    ("I am happy with the product and do not want my money back.", "Is the customer requesting a refund?", (), TRUTH, 0),
    ("The entire production service is down for every user.", "How severe is this incident?", ("Cosmetic", "Degraded service", "Total outage"), SCORE, 2),
    ("There is a typo in a tooltip. All features work.", "How severe is this incident?", ("Cosmetic", "Degraded service", "Total outage"), SCORE, 0),
    ("Search works but takes ten seconds instead of one.", "How severe is this incident?", ("Cosmetic", "Degraded service", "Total outage"), SCORE, 1),
]


def gpu_info():
    try:
        output = subprocess.check_output(["nvidia-smi", "--query-gpu=name,memory.used,memory.total", "--format=csv,noheader,nounits"],
                                         text=True, stderr=subprocess.DEVNULL, timeout=5)
        fields = output.splitlines()[0].split(",")
        return {"name": fields[0].strip(), "used_mib": int(fields[1]), "total_mib": int(fields[2])}
    except (OSError, ValueError, subprocess.SubprocessError):
        return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("profile", choices=("small", "light", "medium", "large"))
    parser.add_argument("--library-dir", required=True)
    parser.add_argument("--models", type=Path, default=ROOT / "models/downloads")
    parser.add_argument("--gpu", action="store_true")
    parser.add_argument("--threads", type=int, default=4)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.profile = "light" if args.profile == "small" else args.profile
    spec = json.loads((ROOT / "models/profiles.json").read_text())[args.profile]
    runtime = Runtime(args.library_dir)
    baseline = gpu_info() if args.gpu else None
    samples, stop = [], threading.Event()

    def sample():
        while not stop.is_set():
            info = gpu_info()
            if info:
                samples.append(info["used_mib"])
            stop.wait(.2)

    monitor = threading.Thread(target=sample, daemon=True) if args.gpu else None
    if monitor:
        monitor.start()
    start = time.perf_counter()
    try:
        with runtime.load_model(args.models / spec["file"], context_size=spec["context_size"],
                                threads=args.threads, gpu_layers=99 if args.gpu else 0) as engine:
            load_seconds = time.perf_counter() - start
            engine.truth("Warmup.", "Is this a warmup?")
            results, milliseconds = [], []
            for state, question, criteria, kind, label in CASES:
                start = time.perf_counter()
                result = engine.evaluate(state, [Question(question, criteria, kind)])[0]
                elapsed = 1000 * (time.perf_counter() - start)
                milliseconds.append(elapsed)
                results.append({"state": state, "label": label, "choice": result.choice, "value": result.value,
                                "confidence": result.confidence, "latency_ms": elapsed, "probabilities": result.probabilities})
    finally:
        stop.set()
        if monitor:
            monitor.join(timeout=6)
    report = {
        "profile": args.profile, "model_sha256": spec["sha256"], "gpu": args.gpu, "platform": platform.platform(),
        "threads": args.threads, "context_size": spec["context_size"], "load_seconds": load_seconds,
        "median_ms": statistics.median(milliseconds), "max_ms": max(milliseconds),
        "diagnostic_accuracy": sum(r["choice"] == r["label"] for r in results) / len(results),
        "gpu_baseline": baseline, "gpu_total_peak_mib": max(samples) if samples else None,
        "note": "12 authored sanity cases, not a held-out quality benchmark. GPU memory is sampled total device usage, including other applications. Performance excludes load and warmup. Temperature=1, uncalibrated.",
        "cases": results,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: v for k, v in report.items() if k != "cases"}, indent=2))


if __name__ == "__main__":
    main()
