#!/usr/bin/env python3
"""The constitution's static engine — read-only checks that bond documents to code.

Delivered by the Legislator as an owned file (`docs/ai/engine.py`). Never
hand-edit it: change the skill source and re-run /legislator.

Jobs — the check jobs write nothing; `baseline` writes exactly its
declared target and nothing else (ADR-0003):
  anchors    every path or symbol an anchored OKF document backticks resolves
  okf-debt   anchored documents whose sources moved on without them
  sdd-lint   case practice: dangling per-R-NNN references, uncovered
             requirements in planned cases, unresolved placeholders
  baseline   writes docs/ai/baseline.md — the R-NNN <-> annotated-tests
             register, regenerated from the case specs and the test tree

Usage: python3 docs/ai/engine.py <job>
Exit:  0 clean, 1 findings printed to stdout, 2 usage error,
       3 the engine itself failed — stdout is NOT a verdict. A caller that
       reads stdout lines only would take a crash for a clean check, so the
       failure has to reach the exit code.

The law this executes: `docs/ai/rules/core/okf.md` (link hardness, the
closed anchor definition) for the anchor jobs, and `docs/ai/rules/core/sdd.md`
(the analyze gate's mechanical passes; the EARS and per-R-NNN forms) for
sdd-lint and baseline. The rung that requires the anchor jobs is
`docs/ai/rules/core/verification.md`.
"""
from __future__ import annotations

import os
import re
import subprocess
import sys
import tempfile
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


# --- SDD practice (BL-043) -------------------------------------------------
# One id vocabulary: R-NNN, exactly three digits, the form core/sdd.md pins.
# EARS headings define ids; `per R-NNN` references them — in a plan as task
# traceability, in a test source file as the annotation that makes the file
# an annotated test for that requirement.

CASES = ROOT / "docs" / "cases"
BASELINE = ROOT / "docs" / "ai" / "baseline.md"
# A definition line: the id, an em-dash, the requirement — in any of the
# three forms this repo's cases actually use (`### R-NNN — t`,
# `- **R-NNN** — t`, bare `R-NNN — t`). The em-dash after the id is the
# definition's signature; `per R-NNN` is always a reference.
EARS_DEF = re.compile(
    r"^(?:#{1,6}\s+|-\s+)?\*{0,2}(R-\d{3})\*{0,2}\s+— (.+?)\s*$", re.M)
# `per R-NNN` and the list form `per R-001, R-002` — the first real plan
# written under this law used the list, so the reference form admits it.
PER_REF = re.compile(r"\bper (R-\d{3}(?:,\s*R-\d{3})*)\b")
RID = re.compile(r"R-\d{3}")
PLACEHOLDER = re.compile(r"\{\{[A-Z_]+\}\}")
INLINE_CODE = re.compile(r"`[^`\n]*`")


def per_refs(text: str) -> set[str]:
    """Every R-NNN referenced by a `per ...` in the text."""
    out: set[str] = set()
    for group in PER_REF.findall(text):
        out.update(RID.findall(group))
    return out


def case_dirs() -> list[Path]:
    if not CASES.is_dir():
        return []
    return sorted(p for p in CASES.iterdir() if p.is_dir())


def case_requirements(case: Path) -> dict[str, str]:
    """id -> definition text, from EARS definition lines in the case's
    spec.md. Ids are unique WITHIN a case only — R-001 lawfully exists in
    many cases — so every consumer keys by (case, id), never by id alone.

    Parsed with fences blanked but inline code KEPT: a definition line
    lawfully backticks the paths it binds, and stripping them would
    truncate the definition the baseline displays."""
    spec = case / "spec.md"
    if not spec.is_file():
        return {}
    return dict(EARS_DEF.findall(_blank_fences(spec.read_text(errors="ignore"))))


def case_is_converged(case: Path) -> bool:
    """True when any of the case's documents carries the closing verdict.
    A converged case is history (core/artifact-lifecycle.md): its going out
    of date is the design, so the lint never re-judges it — the analyze
    gate serves work in flight, not the record."""
    for f in case.rglob("*.md"):
        try:
            if "\u2705 Converged" in f.read_text(errors="ignore"):
                return True
        except OSError:
            continue
    return False


def _blank_fences(text: str) -> str:
    """Fenced blocks blanked, inline code kept."""
    kept = []
    fenced = False
    for line in text.splitlines():
        if line.lstrip().startswith("```"):
            fenced = not fenced
            kept.append("")
        elif fenced:
            kept.append("")
        else:
            kept.append(line)
    return "\n".join(kept)


def prose_only(text: str) -> str:
    """Markdown with fenced blocks AND inline code blanked — a token inside
    either is quotation, not content. The same rule audit check 2 applies
    (BL-057): the two scanners must agree on what counts as prose."""
    return "\n".join(INLINE_CODE.sub("", line)
                     for line in _blank_fences(text).splitlines())


def test_files() -> list[Path]:
    """Annotated-test candidates: files under the source roots whose path
    contains "test" (case-insensitive) — `*.Tests/`, `*Tests.cs`,
    `*.spec.ts`, `test_*.py` all match — minus build output and files too
    big to be source."""
    out = []
    for root in source_roots():
        for p in root.rglob("*"):
            if not p.is_file():
                continue
            parts = p.relative_to(ROOT).parts
            if any(part.startswith(".") for part in parts):
                continue
            if any(part in BUILD_DIRS for part in parts):
                continue
            if "test" not in p.relative_to(ROOT).as_posix().lower():
                continue
            try:
                if p.stat().st_size > MAX_BYTES:
                    continue
            except OSError:
                continue
            out.append(p)
    return sorted(out)


def annotated_tests() -> dict[str, list[str]]:
    """R-NNN -> sorted repo-relative test files carrying `per R-NNN`."""
    cov: dict[str, list[str]] = {}
    for p in test_files():
        try:
            text = p.read_text(errors="ignore")
        except OSError:
            continue
        rel = p.relative_to(ROOT).as_posix()
        for rid in per_refs(text):
            cov.setdefault(rid, []).append(rel)
    return {rid: sorted(files) for rid, files in cov.items()}


def job_sdd_lint() -> list[str]:
    """The analyze gate's mechanical passes (core/sdd.md), read-only.
    Scope: docs/cases/** only — docs/superpowers/** is retired history and
    never enters a lint pass.

    Dangling is judged against EVERY case's definitions, not just the
    referencing case's own: a case may lawfully trace a requirement of a
    sibling case riding the same edition. Coverage stays per-case."""
    findings: list[str] = []
    all_reqs: set[str] = set()
    for case in case_dirs():
        all_reqs |= set(case_requirements(case))
    for case in case_dirs():
        if case_is_converged(case):
            continue
        reqs = case_requirements(case)
        case_rel = case.relative_to(ROOT).as_posix()
        for f in sorted(case.rglob("*.md")):
            rel = f.relative_to(ROOT).as_posix()
            prose = prose_only(f.read_text(errors="ignore"))
            for rid in sorted(per_refs(prose)):
                if rid not in all_reqs:
                    findings.append(
                        f"{rel}: dangling: per {rid} resolves to no EARS "
                        f"definition in any docs/cases/*/spec.md")
            for m in PLACEHOLDER.finditer(prose):
                findings.append(
                    f"{rel}: unresolved-placeholder: {m.group(0)}")
        # Coverage applies only where the case declares a plan: tier 0/1 is
        # lawful (core/sdd.md), so a spec-only case yields no coverage noise.
        if (case / "plan.md").is_file():
            plan_refs = per_refs(
                prose_only((case / "plan.md").read_text(errors="ignore")))
            for rid in sorted(set(reqs) - plan_refs):
                findings.append(
                    f"{case_rel}/plan.md: uncovered: {rid} has no per-{rid} task")
    return sorted(findings)


def job_baseline() -> list[str]:
    """Writes docs/ai/baseline.md — the ONE write this engine performs
    (ADR-0003) — and reports nothing. Deterministic: same repo state, same
    bytes (sorted rows, no timestamps), so regeneration over an unchanged
    repo is a zero diff and a hand edit never survives a run.

    Atomic: the content is staged in a sibling temp file and os.replace'd,
    so a crash leaves the target untouched or fully rewritten — exit 3 can
    never mean a half-written artifact."""
    # Keyed by (case, id): R-NNN is unique within a case only. A test
    # marker names an id without a case, so a colliding id maps the test
    # into every case that defines it — the ambiguity is displayed, not
    # resolved silently.
    rows: dict[tuple[str, str], str] = {}
    for case in case_dirs():
        case_rel = case.relative_to(ROOT).as_posix()
        for rid, title in case_requirements(case).items():
            rows[(case_rel, rid)] = title.replace("|", "\\|")
    cov = annotated_tests()
    lines = [
        "<!-- GENERATED by `python3 docs/ai/engine.py baseline` — do not edit.",
        "     Sources: EARS headings in docs/cases/*/spec.md; the literal",
        "     marker `per R-NNN` in test source files. Regenerate on demand;",
        "     this file dies with its sources (core/artifact-lifecycle.md,",
        "     generated class). -->",
        "",
        "# Baseline — what the system must do today",
        "",
        "| Requirement | Definition | Case | Annotated tests |",
        "|---|---|---|---|",
    ]
    for case_rel, rid in sorted(rows):
        tests = ", ".join(f"`{t}`" for t in cov.get(rid, [])) or "—"
        lines.append(f"| {rid} | {rows[(case_rel, rid)]} | `{case_rel}` | {tests} |")
    uncovered = sorted(k for k in rows if k[1] not in cov)
    lines += ["", "## Uncovered — requirements no test carries", ""]
    if uncovered:
        lines += [f"- {rid} — {rows[(case_rel, rid)]} (`{case_rel}`)"
                  for case_rel, rid in uncovered]
    else:
        lines.append("(none)")
    content = "\n".join(lines) + "\n"
    BASELINE.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(dir=BASELINE.parent, prefix=".baseline-",
                               suffix=".tmp")
    try:
        with os.fdopen(fd, "w") as fh:
            fh.write(content)
        os.replace(tmp, BASELINE)
    except BaseException:
        try:
            os.unlink(tmp)
        except OSError:
            pass
        raise
    return []


JOBS = {"anchors": job_anchors, "okf-debt": job_okf_debt,
        "sdd-lint": job_sdd_lint, "baseline": job_baseline}


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
