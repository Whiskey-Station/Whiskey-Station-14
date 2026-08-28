#!/usr/bin/env python3

from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest


SCRIPT = Path(__file__).parents[1] / "launcher_engine.py"
SPEC = importlib.util.spec_from_file_location("launcher_engine", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
launcher_engine = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(launcher_engine)


class LauncherEngineTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.source = self.root / "source"
        self.source.mkdir()
        self.git("init", "-b", "main")
        self.git("config", "user.email", "ci@example.invalid")
        self.git("config", "user.name", "CI")
        version_file = self.source / "MSBuild" / "Robust.Engine.Version.props"
        version_file.parent.mkdir()
        version_file.write_text(
            "<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>\n",
            encoding="utf-8",
        )
        self.git("add", ".")
        self.git("commit", "-m", "engine")
        self.git("tag", "v1.2.3")
        self.commit = self.git("rev-parse", "HEAD").stdout.strip()
        self.lock = {
            "schemaVersion": 1,
            "repository": str(self.source),
            "tag": "v1.2.3",
            "version": "1.2.3",
            "commit": self.commit,
        }

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def git(self, *args: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            ["git", "-C", str(self.source), *args],
            text=True,
            capture_output=True,
            check=True,
        )

    def test_verify_accepts_exact_clean_engine(self) -> None:
        launcher_engine.verify_engine(self.source, self.lock)

    def test_verify_rejects_wrong_commit(self) -> None:
        wrong = dict(self.lock, commit="0" * 40)
        with self.assertRaisesRegex(launcher_engine.LauncherEngineError, "commit mismatch"):
            launcher_engine.verify_engine(self.source, wrong)

    def test_verify_rejects_tracked_modification(self) -> None:
        version_file = self.source / "MSBuild" / "Robust.Engine.Version.props"
        version_file.write_text(version_file.read_text() + "<!-- dirty -->\n")
        with self.assertRaisesRegex(launcher_engine.LauncherEngineError, "not clean"):
            launcher_engine.verify_engine(self.source, self.lock)

    def test_verify_rejects_untracked_source(self) -> None:
        (self.source / "Injected.cs").write_text("// must not enter a launcher build\n")
        with self.assertRaisesRegex(launcher_engine.LauncherEngineError, "not clean"):
            launcher_engine.verify_engine(self.source, self.lock)

    def test_clone_is_pinned_and_refuses_overwrite(self) -> None:
        destination = self.root / "clone"
        launcher_engine.clone_engine(destination, self.lock)
        launcher_engine.verify_engine(destination, self.lock)
        with self.assertRaisesRegex(launcher_engine.LauncherEngineError, "overwrite"):
            launcher_engine.clone_engine(destination, self.lock)

    def test_load_lock_rejects_partial_sha(self) -> None:
        path = self.root / "lock.json"
        path.write_text(json.dumps(dict(self.lock, commit=self.commit[:8])), encoding="utf-8")
        with self.assertRaisesRegex(launcher_engine.LauncherEngineError, "full 40-character"):
            launcher_engine.load_lock(path)


if __name__ == "__main__":
    unittest.main()
