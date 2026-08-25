#!/usr/bin/env python3
"""The constitution's static engine — read-only checks that bond documents to code.

Delivered by the Legislator as an owned file (`docs/ai/engine.py`). Never
hand-edit it: change the skill source and re-run /legislator.

Jobs (both read-only — this engine writes nothing):
  anchors    every path or symbol an anchored OKF document backticks resolves
  okf-debt   anchored documents whose sources moved on without them

Usage: python3 docs/ai/engine.py <job>
Exit:  0 clean, 1 findings printed to stdout, 2 usage error,
       3 the engine itself failed — stdout is NOT a verdict. A caller that
       reads stdout lines only would take a crash for a clean check, so the
       failure has to reach the exit code.

The law this executes is `docs/ai/rules/core/okf.md` (link hardness and the
closed anchor definition); the rung that requires it is
`docs/ai/rules/core/verification.md`.
"""
from __future__ import annotations

import re
import subprocess
import sys
from datetime import datetime
from pathlib import Path

# The engine ships at <repo>/docs/ai/engine.py — two parents up is the repo.
ROOT = Path(__file__).resolve().parents[2]
OKF = ROOT / "docs" / "okf"

HUMAN_CLASS = {"glossary.md", "log.md"}   # core/okf.md's human class
BUILD_DIRS = {"bin", "obj", "node_modules", "dist"}   # excluded at ANY depth
# Top-level directories that are not source roots: build output, plus docs
# itself (the knowledge layer must not resolve its own anchors).
IGNORED_DIRS = BUILD_DIRS | {"docs"}
SOURCE_EXTS = (".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".go", ".rs",
               ".java", ".kt", ".rb", ".php", ".sql", ".html", ".css")
DEBT_DAYS = 30            # the threshold core/okf.md declares; this mirrors it
MAX_BYTES = 2_000_000     # a file bigger than this is not prose or source

TOKEN = re.compile(r"`([^`\n]+)`")
PASCAL = re.compile(r"[A-Z][A-Za-z0-9]{3,}(\.[A-Z][A-Za-z0-9]+)*$")
FORBIDDEN = set(" <>*?")


def top_level_dirs() -> set[str]:
    return {p.name for p in ROOT.iterdir() if p.is_dir() and not p.name.startswith(".")}


def source_roots() -> list[Path]:
    return sorted(p for p in ROOT.iterdir()
                  if p.is_dir() and not p.name.startswith(".")
                  and p.name not in IGNORED_DIRS)


STATUS = re.compile(r"^status:\s*(\S+)\s*$", re.M)


def front_matter(text: str) -> str:
    """The YAML front-matter block, or "" when the document has none."""
    lines = text.splitlines()
    if not lines or lines[0].strip() != "---":
        return ""
    for i in range(1, len(lines)):
        if lines[i].strip() == "---":
            return "\n".join(lines[1:i])
    return ""


def is_removed(doc: Path) -> bool:
    """core/okf.md: a document flipped to `status: removed` leaves the
    anchored class. The checklist tells the owner to keep such a document and
    mark it; anchoring it would make obedience to the checklist wedge every
    later task through the verification rung, which is repo-global."""
    try:
        m = STATUS.search(front_matter(doc.read_text(errors="ignore")))
    except OSError:
        return False
    return bool(m) and m.group(1).strip().strip('"\'') == "removed"


def anchored_docs() -> list[Path]:
    if not OKF.is_dir():
        return []
    return sorted(p for p in OKF.rglob("*.md")
                  if p.name not in HUMAN_CLASS and not is_removed(p))


def scannable_lines(text: str):
    """Yield (lineno, line) outside front matter and fenced code blocks."""
    lines = text.splitlines()
    i, n = 0, len(lines)
    if lines and lines[0].strip() == "---":
        i = 1
        while i < n and lines[i].strip() != "---":
            i += 1
        i += 1
    fenced = False
    while i < n:
        line = lines[i]
        if line.lstrip().startswith("```"):
            fenced = not fenced
        elif not fenced:
            yield i + 1, line
        i += 1


def classify(token: str, top: set[str]) -> str | None:
    """'path' | 'symbol' | None — the closed definition in core/okf.md."""
    if any(c in token for c in FORBIDDEN):
        return None
    if token.startswith("~") or token.startswith("/"):
        return None
    if "/" in token:
        return "path" if token.split("/", 1)[0] in top else None
    return "symbol" if PASCAL.match(token) else None


def path_target(token: str) -> Path:
    """The path a path-anchor names; a trailing `.Member()` is stripped."""
    base = token.rstrip(":,.")
    if base.endswith("()"):
        stem = base[:-2].rsplit(".", 1)[0]
        for ext in SOURCE_EXTS:
            if (ROOT / (stem + ext)).exists():
                return ROOT / (stem + ext)
    return ROOT / base


def resolve_symbols(symbols: set[str]) -> set[str]:
    """The subset occurring literally under the source roots — one pass."""
    found: set[str] = set()
    if not symbols:
        return found
    for root in source_roots():
        for p in root.rglob("*"):
            if len(found) == len(symbols):
                return found
            if not p.is_file():
                continue
            parts = p.relative_to(ROOT).parts
            if any(part.startswith(".") for part in parts):
                continue
            # Build output at ANY depth, not just top level: a stale
            # src/App/obj/Debug/App.dll still carrying a deleted symbol would
            # otherwise resolve it, and the check would silently miss the rot
            # it exists to find — differently on a CI clone and a dev clone.
            if any(part in BUILD_DIRS for part in parts):
                continue
            try:
                if p.stat().st_size > MAX_BYTES:
                    continue
                text = p.read_text(errors="ignore")
            except OSError:
                continue
            for s in symbols - found:
                if s.split(".", 1)[0] in text:
                    found.add(s)
    return found


def job_anchors() -> list[str]:
    top = top_level_dirs()
    roots = ", ".join(p.name + "/" for p in source_roots()) or "(no source roots)"
    findings: list[str] = []
    sites: list[tuple[str, int, str]] = []
    symbols: set[str] = set()
    for doc in anchored_docs():
        rel = doc.relative_to(ROOT).as_posix()
        for lineno, line in scannable_lines(doc.read_text(errors="ignore")):
            for m in TOKEN.finditer(line):
                token = m.group(1).strip()
                kind = classify(token, top)
                if kind == "path":
                    if not path_target(token).exists():
                        findings.append(
                            f"{rel}:{lineno}: path-anchor: {token} → no such file")
                elif kind == "symbol":
                    symbols.add(token)
                    sites.append((rel, lineno, token))
    resolved = resolve_symbols(symbols)
    for rel, lineno, token in sites:
        if token not in resolved:
            findings.append(
                f"{rel}:{lineno}: symbol-anchor: {token} → not found in {roots}")
    return sorted(findings)


def git_iso(rel: str) -> str | None:
    """Newest commit date for a path, or None (untracked, or no git)."""
    try:
        r = subprocess.run(["git", "log", "-1", "--format=%cI", "--", rel],
                           cwd=ROOT, capture_output=True, text=True, timeout=10)
    except (OSError, subprocess.SubprocessError):
        return None
    return r.stdout.strip() or None


def job_okf_debt() -> list[str]:
    top = top_level_dirs()
    findings: list[str] = []
    for doc in anchored_docs():
        rel = doc.relative_to(ROOT).as_posix()
        doc_iso = git_iso(rel)
        if not doc_iso:
            continue                      # untracked, or no git — nothing to compare
        doc_dt = datetime.fromisoformat(doc_iso)
        worst: tuple[str, int] | None = None
        for _lineno, line in scannable_lines(doc.read_text(errors="ignore")):
            for m in TOKEN.finditer(line):
                token = m.group(1).strip()
                if classify(token, top) != "path":
                    continue
                target = path_target(token)
                if not target.exists():
                    continue              # a broken anchor is the anchors job's finding
                # A directory's git history is the union of everything beneath
                # it, so it can never say whether one document went stale —
                # not a debt source (the anchors job still checks it exists).
                if target.is_dir():
                    continue
                src_rel = target.relative_to(ROOT).as_posix()
                src_iso = git_iso(src_rel)
                if not src_iso:
                    continue
                days = (datetime.fromisoformat(src_iso) - doc_dt).days
                if days > DEBT_DAYS and (worst is None or days > worst[1]):
                    worst = (src_rel, days)
        if worst:
            findings.append(f"{rel}: okf-sync-debt: {worst[0]} changed "
                            f"{worst[1]} days after this document")
    return sorted(findings)


JOBS = {"anchors": job_anchors, "okf-debt": job_okf_debt}


def main(argv: list[str]) -> int:
    if len(argv) != 2 or argv[1] not in JOBS:
        print(f"usage: python3 {Path(__file__).name} "
              f"{{{'|'.join(sorted(JOBS))}}}", file=sys.stderr)
        return 2
    try:
        findings = JOBS[argv[1]]()
    except Exception as exc:                       # noqa: BLE001 — deliberate
        # Never let a crash reach a caller as an empty stdout: audit checks 15
        # and 17 read stdout lines, so silence would read as "no findings".
        print(f"engine failed: {type(exc).__name__}: {exc}", file=sys.stderr)
        return 3
    for f in findings:
        print(f)
    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
