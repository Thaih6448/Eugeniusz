"""Run every Python suite, failing if a required suite is missing or empty."""
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "bindings/python"))


def main():
    loader = unittest.TestLoader()
    for name in ("test_python", "test_api_dataset", "test_archives", "test_release"):
        if not (ROOT / "tests" / f"{name}.py").is_file():
            raise RuntimeError(f"Required test suite is missing: {name}")
        if loader.loadTestsFromName(name).countTestCases() == 0:
            raise RuntimeError(f"Required test suite is empty: {name}")
    suite = loader.discover(str(ROOT / "tests"), pattern="test_*.py")
    return 0 if unittest.TextTestRunner(verbosity=2).run(suite).wasSuccessful() else 1


if __name__ == "__main__":
    sys.exit(main())
