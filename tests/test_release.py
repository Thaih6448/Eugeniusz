"""Release metadata must follow the installed SDK, not a hard-coded version."""
from pathlib import Path
import json
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from package_release import installed_version
from download_model import download_spec


class ReleaseTests(unittest.TestCase):
    def test_pixel_profile_matches_large(self):
        models = Path(__file__).resolve().parents[1] / "models"
        profiles = json.loads((models / "profiles.json").read_text(encoding="utf-8"))
        pixel = json.loads((models / "pixel-profile.json").read_text(encoding="utf-8"))
        self.assertEqual(pixel.pop("schema_version"), profiles["schema_version"])
        self.assertEqual(pixel, profiles["large"])

    def test_reject_local_license_url_before_downloading(self):
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder) / "models"
            for url in ("file:///private/license", "ftp://example.com/license", "https:relative"):
                with self.subTest(url=url), self.assertRaisesRegex(ValueError, "HTTPS"):
                    download_spec("test", {"license_url": url}, output)
            self.assertFalse(output.exists())

    def test_installed_version(self):
        with tempfile.TemporaryDirectory() as folder:
            prefix = Path(folder)
            config = prefix / "lib/cmake/Eugeniusz/EugeniuszConfigVersion.cmake"
            config.parent.mkdir(parents=True)
            config.write_text('set(PACKAGE_VERSION "2.3.4")\n', encoding="utf-8")
            self.assertEqual(installed_version(prefix), "2.3.4")
            config.write_text("# missing version\n", encoding="utf-8")
            with self.assertRaises(ValueError):
                installed_version(prefix)
