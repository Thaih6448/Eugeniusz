import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from archive_parts import split_archive, join_archive


class ArchiveTests(unittest.TestCase):
    def test_roundtrip_and_corruption(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "bundle.zip"
            source.write_bytes(bytes(range(256)) * 7)
            manifest = split_archive(source, 300)
            restored = join_archive(manifest, root / "restored.zip")
            self.assertEqual(restored.read_bytes(), source.read_bytes())
            part = root / json.loads(manifest.read_text())["parts"][0]["file"]
            part.write_bytes(b"corrupted")
            with self.assertRaises(ValueError):
                join_archive(manifest, root / "invalid.zip")


if __name__ == "__main__":
    unittest.main()
