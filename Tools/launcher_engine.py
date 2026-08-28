#!/usr/bin/env python3
"""Clone and verify the exact RobustToolbox revision used by the launcher."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys
from typing import Any


DEFAULT_LOCK = Path(__file__).with_name("launcher_engine_lock.json")
COMMIT_PATTERN = re.compile(r"[0-9a-f]{40}")


class LauncherEngineError(RuntimeError):
    pass


def load_lock(path: Path) -> dict[str, Any]:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise LauncherEngineError(f"cannot read launcher engine lock {path}: {exc}") from exc

    required = {"schemaVersion", "repository", "tag", "version", "commit"}
    missing = required - data.keys() if isinstance(data, dict) else required
    if not isinstance(data, dict) or missing:
        raise LauncherEngineError(
            f"invalid launcher engine lock {path}: missing {', '.join(sorted(missing))}"
        )
    if data["schemaVersion"] != 1:
        raise LauncherEngineError(f"unsupported launcher engine lock schema: {data['schemaVersion']}")
    for field in ("repository", "tag", "version", "commit"):
        if not isinstance(data[field], str) or not data[field].strip():
            raise LauncherEngineError(f"launcher engine lock field {field!r} must be a string")
    data["commit"] = data["commit"].lower()
    if COMMIT_PATTERN.fullmatch(data["commit"]) is None:
        raise LauncherEngineError("launcher engine lock commit must be a full 40-character SHA-1")
    return data


def run_git(engine_dir: Path, *arguments: str) -> str:
    command = ["git", "-C", os.fspath(engine_dir), *arguments]
    result = subprocess.run(command, text=True, capture_output=True, check=False)
    if result.returncode != 0:
        detail = result.stderr.strip() or result.stdout.strip() or f"exit {result.returncode}"
        raise LauncherEngineError(f"{' '.join(command)} failed: {detail}")
    # Preserve the leading status character used by `git submodule status`.
    return result.stdout.rstrip("\r\n")


def read_engine_version(engine_dir: Path) -> str:
    version_file = engine_dir / "MSBuild" / "Robust.Engine.Version.props"
    try:
        contents = version_file.read_text(encoding="utf-8")
    except OSError as exc:
        raise LauncherEngineError(f"cannot read engine version file {version_file}: {exc}") from exc
    matches = re.findall(r"<Version>\s*([^<]+?)\s*</Version>", contents)
    if len(matches) != 1:
        raise LauncherEngineError(f"expected exactly one Version element in {version_file}")
    return matches[0]


def verify_engine(engine_dir: Path, lock: dict[str, Any]) -> None:
    engine_dir = engine_dir.resolve()
    if not engine_dir.is_dir():
        raise LauncherEngineError(f"launcher engine directory does not exist: {engine_dir}")

    head = run_git(engine_dir, "rev-parse", "HEAD").lower()
    if head != lock["commit"]:
        raise LauncherEngineError(
            f"launcher engine commit mismatch: expected {lock['commit']}, found {head}"
        )

    exact_tag = run_git(engine_dir, "describe", "--tags", "--exact-match", "HEAD")
    if exact_tag != lock["tag"]:
        raise LauncherEngineError(
            f"launcher engine tag mismatch: expected {lock['tag']}, found {exact_tag}"
        )

    version = read_engine_version(engine_dir)
    if version != lock["version"]:
        raise LauncherEngineError(
            f"launcher engine version mismatch: expected {lock['version']}, found {version}"
        )

    status = run_git(
        engine_dir,
        "status",
        "--porcelain=v1",
        "--untracked-files=all",
        "--ignore-submodules=none",
    )
    if status:
        raise LauncherEngineError("launcher engine working tree or submodules are not clean")

    submodules = run_git(engine_dir, "submodule", "status", "--recursive")
    invalid = [line for line in submodules.splitlines() if line and line[0] != " "]
    if invalid:
        raise LauncherEngineError(
            "launcher engine has missing or mismatched submodules: " + "; ".join(invalid)
        )


def clone_engine(destination: Path, lock: dict[str, Any]) -> None:
    if os.path.lexists(destination):
        raise LauncherEngineError(f"refusing to overwrite existing destination: {destination}")
    destination.parent.mkdir(parents=True, exist_ok=True)
    command = [
        "git",
        "clone",
        "--depth=1",
        "--single-branch",
        "--branch",
        lock["tag"],
        "--recurse-submodules",
        "--shallow-submodules",
        lock["repository"],
        os.fspath(destination),
    ]
    result = subprocess.run(command, text=True, check=False)
    if result.returncode != 0:
        raise LauncherEngineError(
            f"launcher engine clone failed with exit {result.returncode}; "
            f"partial destination, if any, was left untouched at {destination}"
        )
    verify_engine(destination, lock)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--lock", type=Path, default=DEFAULT_LOCK)
    subparsers = parser.add_subparsers(dest="command", required=True)

    verify_parser = subparsers.add_parser("verify", help="verify an existing engine checkout")
    verify_parser.add_argument("engine_dir", type=Path)

    clone_parser = subparsers.add_parser("clone", help="clone and verify the pinned engine")
    clone_parser.add_argument("destination", type=Path)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        lock = load_lock(args.lock)
        if args.command == "verify":
            verify_engine(args.engine_dir, lock)
        else:
            clone_engine(args.destination, lock)
    except LauncherEngineError as exc:
        print(f"LAUNCHER_ENGINE_GUARD: {exc}", file=sys.stderr)
        return 1

    print(
        "LAUNCHER_ENGINE_GUARD_OK "
        f"tag={lock['tag']} version={lock['version']} commit={lock['commit']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
