"""Keep the quality corpus frozen and its metrics separate from model correctness."""
import hashlib
import json
from pathlib import Path
import sys
import unittest
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
from benchmark_api import summarize, summarize_kinds, validate_result, TRUTH


class DatasetTests(unittest.TestCase):
    def test_empty_summary(self):
        with self.assertRaisesRegex(ValueError, "empty"):
            summarize([])

    def test_sparse_kinds(self):
        row = dict(kind="truth", choice=1, label=1, value=.75,
                   probabilities=[.25, .75], confidence=.75, latency_ms=10)
        self.assertEqual(set(summarize_kinds([row])), {"truth"})
        self.assertEqual(summarize_kinds([]), {})

    def test_api_invariants(self):
        valid = dict(probabilities=[.25, .75], choice=1, value=.75)
        validate_result(SimpleNamespace(**valid), TRUTH)
        for changes in ({"probabilities": []}, {"probabilities": [.2, .2]},
                        {"probabilities": [float("nan"), .75]}, {"choice": 0},
                        {"value": float("nan")}, {"value": .5}):
            with self.subTest(changes=changes), self.assertRaises(ValueError):
                validate_result(SimpleNamespace(**(valid | changes)), TRUTH)

    def test_frozen_cases(self):
        data = (ROOT / "tests/data/api-quality-v1.jsonl").read_bytes()
        self.assertEqual(hashlib.sha256(data).hexdigest(), "dae03a9c98a055df56b8aaa5542e92c1853a7617e3607d64ce675dc1ef115203")
        cases = [json.loads(line) for line in data.decode("utf-8").splitlines()]
        self.assertEqual(len({c["id"] for c in cases}), 300)
        for kind in ("choice", "score", "truth"):
            rows = [c for c in cases if c["kind"] == kind]
            self.assertEqual(len(rows), 100)
            self.assertEqual(sum(c["split"] == "development" for c in rows), 20)
            self.assertEqual(len({c["family"] for c in rows}), 10)
            for case in rows:
                count = len(case["criteria"]) if kind != "truth" else 2
                self.assertTrue(0 <= case["label"] < count)
                self.assertEqual(len(set(case["criteria"])), len(case["criteria"]))
        self.assertEqual(sum(c["label"] for c in cases if c["kind"] == "truth"), 50)

    def test_score_mean_is_not_argmax_accuracy(self):
        result = summarize([dict(kind="score", choice=1, label=1, value=.6, probabilities=[.4, .6], confidence=.6, latency_ms=10)])
        self.assertEqual(result["accuracy"], 1)
        self.assertAlmostEqual(result["value_mae"], .4)
        self.assertAlmostEqual(result["brier"], .32)


if __name__ == "__main__":
    unittest.main()
