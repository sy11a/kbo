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

import json
import os
import re
import shutil
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
        m = STATUS.search(front_matter(doc.read_text(encoding="utf-8", errors="ignore")))
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
                text = p.read_text(encoding="utf-8", errors="ignore")
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
        for lineno, line in scannable_lines(doc.read_text(encoding="utf-8", errors="ignore")):
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
    docs = anchored_docs()
    if docs and shutil.which("git") is None:
        # R-665 (v23): a verification job must fail loud, never report
        # clean, when its measuring instrument is absent (BL-069 F1).
        raise RuntimeError(
            "git unavailable — okf-debt cannot measure staleness; "
            "install git or run where it exists")
    for doc in docs:
        rel = doc.relative_to(ROOT).as_posix()
        doc_iso = git_iso(rel)
        if not doc_iso:
            continue                      # untracked, or no git — nothing to compare
        doc_dt = datetime.fromisoformat(doc_iso)
        worst: tuple[str, int] | None = None
        for _lineno, line in scannable_lines(doc.read_text(encoding="utf-8", errors="ignore")):
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
    return dict(EARS_DEF.findall(_blank_fences(spec.read_text(encoding="utf-8", errors="ignore"))))


def case_is_converged(case: Path) -> bool:
    """True when any of the case's documents carries the closing verdict.
    A converged case is history (core/artifact-lifecycle.md): its going out
    of date is the design, so the lint never re-judges it — the analyze
    gate serves work in flight, not the record."""
    for f in case.rglob("*.md"):
        try:
            text = f.read_text(encoding="utf-8", errors="ignore")
        except OSError:
            continue
        # Line-anchored: the marker closes a case only when a line STARTS
        # with it (bold and a trailing date/period are lawful decoration).
        # A spec QUOTING the marker mid-sentence (BL-065 did, explaining a
        # dropped lint) must not read as a closure — the BL-057 lesson:
        # a quotation is never the artifact.
        if re.search(r"(?m)^\s*(?:\*\*)?\u2705 Converged\b", text):
            return True
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
            text = p.read_text(encoding="utf-8", errors="ignore")
        except OSError:
            continue
        rel = p.relative_to(ROOT).as_posix()
        for rid in per_refs(text):
            cov.setdefault(rid, []).append(rel)
    return {rid: sorted(files) for rid, files in cov.items()}



# --- BL-065 (v23): the case-shape lints -------------------------------

CASE_TIER = re.compile(r"^\*\*Tier:\s*(\d)", re.M)
CASE_TYPE = re.compile(r"^\*\*Spec type:\s*([A-Za-z]+)", re.M)
REQ_BULLET = re.compile(
    r"(?ms)^- \*\*(R-\d{3})\*\*(.*?)(?=^- \*\*R-\d{3}\*\*|^R-\d{3}\b|^#|\Z)")
ADR_NAME = re.compile(r"^(\d{4})-[a-z0-9][a-z0-9-]*\.md$")
ADR_STATUSES = {"proposed", "accepted", "deprecated"}
JOURNAL_NAME = re.compile(r"^\d{4}-\d{2}-\d{2}\.md$")
OKF_STATUSES = {"planned", "partial", "implemented", "removed"}


def spec_shape_findings(case: Path) -> list[str]:
    """Case-file shape (sdd-3/7/8/9/11/13/15): only where a spec exists —
    a tier-0 case without one is lawful (core/sdd.md)."""
    spec = case / "spec.md"
    if not spec.is_file():
        return []
    rel = spec.relative_to(ROOT).as_posix()
    text = spec.read_text(encoding="utf-8", errors="ignore")
    prose = prose_only(text)
    out: list[str] = []
    tier_m = CASE_TIER.search(prose)
    if not tier_m:
        out.append(f"{rel}: no declared tier in the header "
                   f"(**Tier: N**) → declare it per core/sdd.md")
    type_m = CASE_TYPE.search(prose)
    if not type_m:
        out.append(f"{rel}: no declared spec type in the header "
                   f"(**Spec type: ...**) → declare feature/bugfix/exploration")
    if type_m and type_m.group(1).lower() == "bugfix":
        for word in ("current", "expected", "unchanged"):
            if not re.search(rf"(?i)\b{word}\b", prose):
                out.append(f"{rel}: bugfix spec states no {word} behavior "
                           f"→ add the current/expected/unchanged statements")
    tier = int(tier_m.group(1)) if tier_m else None
    if tier is not None and tier >= 1:
        has_in = re.search(r"(?i)(\*\*in\b|\bin[- ]scope\b)", prose)
        has_out = re.search(r"(?i)(\*\*out\b|\bout[- ]of[- ]scope\b)", prose)
        if not (has_in and has_out):
            out.append(f"{rel}: no boundary (in-scope and out-of-scope) "
                       f"→ state both halves per core/sdd.md")
        if not ("GIVEN" in prose and "WHEN" in prose and "THEN" in prose):
            out.append(f"{rel}: no GIVEN/WHEN/THEN hurting case "
                       f"→ ship the scenario per core/sdd.md")
        if not re.search(r"(?m)^## Clarifications", prose):
            out.append(f"{rel}: no ## Clarifications session "
                       f"→ record the grill per core/sdd.md")
    for rid, body in REQ_BULLET.findall(prose):
        shalls = len(re.findall(r"\bSHALL\b", body))
        if shalls != 1:
            out.append(f"{rel}: {rid} carries {shalls} SHALLs "
                       f"→ one line, one behavior, one SHALL")
    return out


def tree_shape_findings() -> list[str]:
    """Repo-level shapes: ADRs, journal names, changelog structure, OKF
    front-matter status (adr-2/3/4, jrnl-1, chlog-1, okf-5)."""
    out: list[str] = []
    adr_dir = ROOT / "docs" / "adr"
    if adr_dir.is_dir():
        numbers: list[int] = []
        for f in sorted(adr_dir.glob("*.md")):
            if f.name == "template.md":
                continue
            rel = f.relative_to(ROOT).as_posix()
            m = ADR_NAME.match(f.name)
            if not m:
                out.append(f"{rel}: ADR filename is not NNNN-kebab-title.md "
                           f"→ rename it per core/adr.md")
                continue
            numbers.append(int(m.group(1)))
            text = prose_only(f.read_text(encoding="utf-8", errors="ignore"))
            for sect in ("Status", "Context", "Decision", "Consequences"):
                if not re.search(rf"(?m)^## {sect}\b", text):
                    out.append(f"{rel}: no ## {sect} section "
                               f"→ use docs/adr/template.md's shape")
            sm = re.search(r"(?ms)^## Status\s+(\S[^\n]*)", text)
            if sm:
                status = sm.group(1).strip().lower()
                if status not in ADR_STATUSES and not status.startswith("superseded by "):
                    out.append(f"{rel}: status {status!r} outside the closed set "
                               f"→ proposed/accepted/deprecated/superseded by NNNN")
        for missing in sorted(set(range(1, max(numbers) + 1)) - set(numbers)) if numbers else []:
            out.append(f"docs/adr/: sequence gap — {missing:04d} is missing "
                       f"→ ADRs are numbered gaplessly, never renumbered")
    jrnl = ROOT / "docs" / "journal"
    if jrnl.is_dir():
        for f in sorted(jrnl.glob("*.md")):
            if f.name == "README.md" or JOURNAL_NAME.match(f.name):
                continue
            rel = f.relative_to(ROOT).as_posix()
            out.append(f"{rel}: journal file is not YYYY-MM-DD.md "
                       f"→ one file per working day, per core/dev-journal.md")
    chlog = ROOT / "CHANGELOG.md"
    if chlog.is_file():
        if "## [Unreleased]" not in chlog.read_text(encoding="utf-8", errors="ignore"):
            out.append("CHANGELOG.md: no ## [Unreleased] section "
                       "→ keep the Keep-a-Changelog structure per core/changelog.md")
    okf = ROOT / "docs" / "okf"
    if okf.is_dir():
        for f in sorted(okf.glob("*.md")):
            if f.name in ("glossary.md", "log.md"):
                continue                     # human class, per core/okf.md
            m = STATUS.search(front_matter(f.read_text(encoding="utf-8", errors="ignore")))
            if m and m.group(1).strip().lower() not in OKF_STATUSES:
                rel = f.relative_to(ROOT).as_posix()
                out.append(f"{rel}: front-matter status {m.group(1)!r} outside "
                           f"planned/partial/implemented/removed → fix the field")
    return out

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
            prose = prose_only(f.read_text(encoding="utf-8", errors="ignore"))
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
                prose_only((case / "plan.md").read_text(encoding="utf-8", errors="ignore")))
            for rid in sorted(set(reqs) - plan_refs):
                findings.append(
                    f"{case_rel}/plan.md: uncovered: {rid} has no per-{rid} task")
        findings.extend(spec_shape_findings(case))
    findings.extend(tree_shape_findings())
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
        with os.fdopen(fd, "w", encoding="utf-8") as fh:
            fh.write(content)
        os.replace(tmp, BASELINE)
    except BaseException:
        try:
            os.unlink(tmp)
        except OSError:
            pass
        raise
    return []



# --- BL-066 (v23): the audit job and the report emitter ----------------
#
# The mechanical audit checks (SKILL.md's Audit section: 1-10, 13, 14, 16,
# plus the two engine checks 15/17 folded in) executed here, and the
# pinned report printed from the results. The model supplies the semantic
# checks (11, 12, check-9 escalations, constitution candidates) through
# --model-findings; the engine merges them into their sections. Reads
# everything, writes nothing (ADR-0003).

AUDIT_ORDER = ["imports-resolve", "unresolved-placeholders",
               "owned-integrity", "staleness", "okf-index-links",
               "codebase-map", "orphan-docs", "journal-recency",
               "foreign-structures", "keep-list", "project-rules",
               "stray-rulebooks", "glossary-vitality", "skill-bindings",
               "okf-anchors", "legacy-home-violation", "okf-sync-debt"]
AUDIT_MODEL_CHECKS = {"project-rules", "stray-rulebooks"}
SEVERITIES = ("Critical", "Warning", "Info")
MD_LINK = re.compile(r"\]\(([^)#\s]+)\)")
IMPORT_LINE = re.compile(r"(?m)^@(\S+)")
FOREIGN_FIXED = [".cursorrules", ".cursor", ".github/copilot-instructions.md",
                 "wiki", ".superpowers", ".specify", "adrs", "doc/adr",
                 ".claude/plans", "CONTEXT.md", "CONTEXT-MAP.md",
                 "UBIQUITOUS_LANGUAGE.md", "NOTES.md", "docs/agents", ".scratch"]
GENERATED_DIRS = {"bin", "obj", "node_modules", "dist"}
SKILL_NAME_TOKEN = re.compile(r"`([a-z][a-z0-9-]{2,})`")


def _entry_doc() -> Path | None:
    a = ROOT / "AGENTS.md"
    if a.is_file():
        return a
    c = ROOT / "CLAUDE.md"
    if c.is_file() and not c.is_symlink():
        return c
    return None


def _read(p: Path) -> str:
    return p.read_text(encoding="utf-8", errors="ignore")


def _git_out(args: list[str]) -> str | None:
    try:
        r = subprocess.run(["git", *args], cwd=str(ROOT),
                           capture_output=True, text=True, timeout=30)
    except OSError as exc:
        raise RuntimeError(
            "git unavailable — the audit's git-backed checks cannot run; "
            "install git or run where it exists") from exc
    if r.returncode != 0:
        return None
    return r.stdout.strip()


class AuditResult:
    def __init__(self) -> None:
        self.findings: dict[str, list[tuple[str, str]]] = {s: [] for s in SEVERITIES}
        self.dirty: set[str] = set()

    def add(self, severity: str, slug: str, line: str) -> None:
        self.findings[severity].append((slug, line))
        self.dirty.add(slug)


def _all_md(exclude: Path | None = None) -> list[Path]:
    out = []
    for p in ROOT.rglob("*.md"):
        parts = p.relative_to(ROOT).parts
        if any(x in GENERATED_DIRS or x.startswith(".git") for x in parts):
            continue
        if exclude and p == exclude:
            continue
        out.append(p)
    entry = _entry_doc()
    if entry and entry not in out:
        out.append(entry)
    return out


def _referenced(candidate: Path, referrers: list[Path]) -> bool:
    rel = candidate.relative_to(ROOT).as_posix()
    name = candidate.name
    for other in referrers:
        if other == candidate:
            continue
        text = _read(other)
        if rel in text:
            return True
        for m in MD_LINK.finditer(text):
            target = m.group(1)
            if target.startswith(("http:", "https:")):
                continue
            try:
                if (other.parent / target).resolve() == candidate.resolve():
                    return True
            except OSError:
                continue
    return False


def audit_checks(skill: Path) -> AuditResult:
    res = AuditResult()
    entry = _entry_doc()

    # 1 imports-resolve (Critical)
    if entry:
        for m in IMPORT_LINE.finditer(_read(entry)):
            target = m.group(1)
            if not (ROOT / target).exists():
                res.add("Critical", "imports-resolve",
                        f"{entry.name}: `@{target}` does not resolve → the "
                        f"file does not exist; remove the import line or restore it")

    # 2 unresolved-placeholders (Critical)
    scan: list[Path] = []
    if entry:
        scan.append(entry)
    scan += sorted((ROOT / "docs").rglob("*.md")) if (ROOT / "docs").is_dir() else []
    scan += sorted((ROOT / ".claude" / "rules").glob("*.md")) if (ROOT / ".claude" / "rules").is_dir() else []
    for f in scan:
        if f == ROOT / "docs" / "adr" / "template.md":
            continue
        for lineno, line in enumerate(prose_only(_read(f)).splitlines(), 1):
            for m in PLACEHOLDER.finditer(line):
                res.add("Critical", "unresolved-placeholders",
                        f"{f.relative_to(ROOT).as_posix()}:{lineno}: bare "
                        f"`{m.group(0)}` token left unfilled → fill it in or "
                        f"regenerate the file from the template")

    # 3 owned-integrity (Critical) + manifest read
    manifest_path = ROOT / "docs" / "ai" / "manifest.json"
    manifest: dict = {}
    if manifest_path.is_file():
        try:
            manifest = json.loads(_read(manifest_path))
        except ValueError:
            res.add("Critical", "owned-integrity",
                    "docs/ai/manifest.json: does not parse as JSON → re-run "
                    "/legislator to regenerate it")
    for rel in manifest.get("ownedFiles", []):
        p = ROOT / rel
        if not p.exists():
            res.add("Critical", "owned-integrity",
                    f"{rel}: named in ownedFiles but missing from disk → "
                    f"re-run /legislator to restore it")
            continue
        if rel.startswith("docs/ai/rules/"):
            src = skill / "assets" / "rules" / rel[len("docs/ai/rules/"):]
        elif rel == "docs/ai/engine.py":
            src = skill / "assets" / "engine" / "engine.py"
        elif rel == "opencode.json":
            src = skill / "assets" / "templates" / "opencode.json.tpl"
        else:
            continue
        if src.is_file() and p.read_bytes() != src.read_bytes():
            res.add("Critical", "owned-integrity",
                    f"{rel}: diverges from the skill source → re-run "
                    f"/legislator to restore it byte-for-byte")

    # 4 staleness (Info)
    version = (skill / "VERSION").read_text(encoding="utf-8").strip() \
        if (skill / "VERSION").is_file() else "?"
    repo_v = manifest.get("legislatorVersion", "?")
    if str(repo_v) != version:
        res.add("Info", "staleness",
                f"docs/ai/manifest.json: legislatorVersion {repo_v}, skill "
                f"source is v{version} → re-run /legislator to upgrade")

    # 5 okf-index-links (Warning)
    index = OKF / "index.md"
    if index.is_file():
        for m in MD_LINK.finditer(_read(index)):
            target = m.group(1)
            if target.startswith(("http:", "https:")):
                continue
            if not (index.parent / target).exists() and not (ROOT / target).exists():
                res.add("Warning", "okf-index-links",
                        f"docs/okf/index.md: link target `{target}` does not "
                        f"resolve → fix or remove the link")

    # 6 codebase-map (Warning)
    cmap = OKF / "codebase-map.md"
    if cmap.is_file():
        rows = re.findall(r"(?m)^\|\s*`([^`]+)`", _read(cmap))
        for row in rows:
            if not (ROOT / row.rstrip("/")).is_dir():
                res.add("Warning", "codebase-map",
                        f"docs/okf/codebase-map.md: row for `{row}` names a "
                        f"directory that no longer exists on disk → remove or "
                        f"update the row")
        mapped = {r.rstrip("/") for r in rows}
        for d in sorted(top_level_dirs() - GENERATED_DIRS):
            if d not in mapped:
                res.add("Warning", "codebase-map",
                        f"docs/okf/codebase-map.md: top-level directory `{d}/` "
                        f"has no row → add one")

    # 7 orphan-docs (Warning)
    exempt_prefixes = ("docs/ai/rules/", "docs/adr/", "docs/journal/",
                       "docs/superpowers/", "docs/cases/")
    all_md = _all_md()
    candidates = []
    if OKF.is_dir():
        candidates += sorted(OKF.glob("*.md"))
    if (ROOT / "docs").is_dir():
        candidates += sorted((ROOT / "docs").glob("*.md"))
    for c in candidates:
        rel = c.relative_to(ROOT).as_posix()
        if rel == "docs/backlog.md" or any(rel.startswith(e) for e in exempt_prefixes):
            continue
        if not _referenced(c, all_md):
            res.add("Warning", "orphan-docs",
                    f"{rel}: not referenced by docs/okf/index.md or any other "
                    f"markdown file in the repo → link it in or delete it")

    # 8 journal-recency (Warning) — git-backed
    jdir = ROOT / "docs" / "journal"
    if jdir.is_dir():
        dates = []
        for f in sorted(jdir.glob("*.md")):
            if f.name == "README.md":
                continue
            m = re.match(r"^(\d{4}-\d{2}-\d{2})", f.stem)
            if not m:
                m = re.search(r"(\d{4}-\d{2}-\d{2})", _read(f))
            if m:
                dates.append(m.group(1))
        dates.sort()
        last_code = _git_out(["log", "-1", "--format=%cs", "--", ".",
                              ":(exclude)docs"])
        if last_code:
            newest = dates[-1] if dates else None
            from datetime import date as _date
            code_d = _date.fromisoformat(last_code)
            if newest is None or (code_d - _date.fromisoformat(newest)).days > 30:
                cited = newest or "no dated entries found"
                res.add("Warning", "journal-recency",
                        f"docs/journal/: newest entry is {cited}, but the last "
                        f"commit touching paths outside docs/ is {last_code} → "
                        f"write the missing entry or record why none is needed")

    # 9 foreign-structures (Info; real CLAUDE.md beside AGENTS.md → Warning)
    kept_paths = {k.get("path") for k in manifest.get("keep", [])}
    for rel in FOREIGN_FIXED:
        p = ROOT / rel
        if rel in kept_paths or not p.exists():
            continue
        if p.is_dir():
            inner = sorted(x for x in p.rglob("*") if x.is_file())
            for x in inner or [p]:
                xrel = x.relative_to(ROOT).as_posix()
                res.add("Info", "foreign-structures",
                        f"{xrel}: foreign AI-layer structure / agent-tooling "
                        f"debris → clean it up or fold it into the AI layer")
        else:
            res.add("Info", "foreign-structures",
                    f"{rel}: foreign AI-layer structure → fold its law into "
                    f".claude/rules/ or clean it up")
    claude_md = ROOT / "CLAUDE.md"
    if claude_md.is_file() and not claude_md.is_symlink() and (ROOT / "AGENTS.md").is_file():
        res.add("Warning", "foreign-structures",
                "CLAUDE.md: is a real file beside AGENTS.md → it should be "
                "the symlink to AGENTS.md (v14 file model)")

    # 10 keep-list (Warning / Info)
    if manifest and "keep" not in manifest:
        res.add("Info", "keep-list",
                "docs/ai/manifest.json: no keep key (pre-keep-schema "
                "manifest) → re-run /legislator to refresh")
    for entry_k in manifest.get("keep", []):
        kpath = entry_k.get("path", "")
        kp = ROOT / kpath
        if not kp.exists():
            res.add("Warning", "keep-list",
                    f"{kpath}: kept path missing from disk → restore it or "
                    f"remove the keep entry")
            continue
        if kpath.startswith(".claude/rules/"):
            continue
        if kp.suffix == ".md" and not _referenced(kp, all_md):
            res.add("Warning", "keep-list",
                    f"{kpath}: kept but referenced from nowhere → link it "
                    f"from docs/okf/index.md or AGENTS.md")

    # 13 glossary-vitality (Warning)
    gl = OKF / "glossary.md"
    src_dirs = top_level_dirs() - GENERATED_DIRS - {"docs"}
    if gl.is_file() and src_dirs:
        body_rows = [ln for ln in _read(gl).splitlines()
                     if ln.startswith("|") and not re.match(r"^\|[\s|-]+$", ln)]
        if len(body_rows) <= 1:                      # header only
            res.add("Warning", "glossary-vitality",
                    "docs/okf/glossary.md: glossary empty in a repo with "
                    "source code → seed it or add the domain's terms")

    # 14 skill-bindings (Info) — machine-relative by design
    sk = ROOT / ".claude" / "rules" / "skills.md"
    if sk.is_file():
        homes = [Path.home() / ".claude" / "skills",
                 Path.home() / ".agents" / "skills",
                 Path.home() / ".config" / "opencode" / "skills"]
        for name in sorted(set(SKILL_NAME_TOKEN.findall(_read(sk)))):
            if any((h / name).exists() for h in homes):
                continue
            res.add("Info", "skill-bindings",
                    f"{name}: sanctioned in .claude/rules/skills.md but not "
                    f"installed on this machine → link it (see the legislator "
                    f"README's \"Skill ecosystem setup\") or remove it from "
                    f"the list")

    # 16 legacy-home-violation (Warning) — git-backed
    legislated = _git_out(["log", "--diff-filter=A", "--format=%cs", "--",
                           "docs/ai/manifest.json"])
    legislated_date = legislated.splitlines()[-1] if legislated else None
    if legislated_date:
        legacy: list[Path] = []
        for sub in ("specs", "plans"):
            d = ROOT / "docs" / "superpowers" / sub
            if d.is_dir():
                legacy += sorted(x for x in d.rglob("*") if x.is_file())
        for f in legacy:
            frel = f.relative_to(ROOT).as_posix()
            born = _git_out(["log", "--diff-filter=A", "--format=%cs",
                             "--", frel])
            born_date = born.splitlines()[-1] if born else None
            if born_date and born_date > legislated_date:
                res.add("Warning", "legacy-home-violation",
                        f"{frel}: born in a legacy home after legislation "
                        f"({born_date}) → move it to its standard home "
                        f"(docs/cases/BL-NNN/)")

    # 15 okf-anchors + engine-absent Info
    if OKF.is_dir():
        if not (ROOT / "docs" / "ai" / "engine.py").is_file():
            res.add("Info", "okf-anchors",
                    "docs/ai/engine.py: engine absent (repo below v20) → "
                    "re-run /legislator to upgrade")
        for line in job_anchors():
            res.add("Warning", "okf-anchors",
                    f"{line.split(' → ')[0]} → the repo no longer contains "
                    f"it; update the document or fix the reference")
        # 17 okf-sync-debt
        for line in job_okf_debt():
            res.add("Warning", "okf-sync-debt",
                    f"{line} → update the document or state why it still holds")
    return res


def load_model_findings(path: Path) -> dict:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        raise RuntimeError(
            f"model-findings file unreadable or malformed ({path}): {exc}") from exc
    if not isinstance(data, dict) or not isinstance(data.get("findings", []), list) \
            or not isinstance(data.get("candidates", []), list):
        raise RuntimeError(
            f"model-findings file has the wrong shape ({path}): expected "
            f"{{findings: [...], candidates: [...]}}")
    for f in data.get("findings", []):
        if not isinstance(f, dict) or f.get("severity") not in SEVERITIES \
                or not isinstance(f.get("line"), str) or not f.get("check"):
            raise RuntimeError(
                f"model-findings entry malformed ({path}): {f!r}")
    return data


def job_audit(skill: Path, model_findings: Path | None) -> tuple[str, bool]:
    """(report text, any findings) — the emitter (R-661..R-663, R-669)."""
    res = audit_checks(skill)
    model = load_model_findings(model_findings) if model_findings else None
    if model:
        for f in model["findings"]:
            esc = f.get("escalates")
            if esc:
                for sev in SEVERITIES:
                    res.findings[sev] = [(s, l) for s, l in res.findings[sev]
                                         if esc not in l or s != f["check"]]
            text = f["line"].strip()
            if text.startswith("- "):
                text = text[2:]
            prefix = f"[{f['check']}] "
            if text.startswith(prefix):
                text = text[len(prefix):]
            res.findings[f["severity"]].append((f["check"], text))
            res.dirty.add(f["check"])

    manifest_path = ROOT / "docs" / "ai" / "manifest.json"
    repo_v = "?"
    if manifest_path.is_file():
        try:
            repo_v = json.loads(_read(manifest_path)).get("legislatorVersion", "?")
        except ValueError:
            pass
    version = (skill / "VERSION").read_text(encoding="utf-8").strip() \
        if (skill / "VERSION").is_file() else "?"
    state = "up to date" if str(repo_v) == version else "behind"

    lines = [f"# AI-Layer Audit — {ROOT.name}, {datetime.now().date().isoformat()}",
             "",
             f"Constitution: v{repo_v} (skill source: v{version}) — {state}",
             ""]
    any_findings = any(res.findings[s] for s in SEVERITIES)
    if not any_findings:
        lines += ["No findings.", ""]
    else:
        order = {slug: i for i, slug in enumerate(AUDIT_ORDER)}
        for sev in SEVERITIES:
            if not res.findings[sev]:
                continue
            lines.append(f"## {sev}")
            for slug, text in sorted(res.findings[sev],
                                     key=lambda x: (order.get(x[0], 99), x[1])):
                lines.append(f"- [{slug}] {text}")
            lines.append("")
    if model and model.get("candidates"):
        lines.append("## Constitution candidates")
        lines.extend(model["candidates"])
        lines.append("")

    mech = [s for s in AUDIT_ORDER if s not in AUDIT_MODEL_CHECKS]
    clean = [s for s in mech if s not in res.dirty]
    if model is not None:
        clean += [s for s in sorted(AUDIT_MODEL_CHECKS) if s not in res.dirty]
        model_note = None
    else:
        model_note = ("Model checks (project-rules, stray-rulebooks, "
                      "constitution candidates): not supplied — this print "
                      "is the mechanical half only.")
    ordered_clean = [s for s in AUDIT_ORDER if s in clean]
    lines.append("Clean checks: " + (", ".join(ordered_clean) if ordered_clean else "none"))
    if model_note:
        lines.append(model_note)
    if model and isinstance(model.get("verification"), str):
        lines.append("")
        lines.append(model["verification"])
    lines.append("")
    lines.append(f"Emitted by docs/ai/engine.py audit — constitution v{version}.")
    return "\n".join(lines) + "\n", any_findings

JOBS = {"anchors": job_anchors, "okf-debt": job_okf_debt,
        "sdd-lint": job_sdd_lint, "baseline": job_baseline}


def main(argv: list[str]) -> int:
    if len(argv) >= 2 and argv[1] == "audit":
        args = argv[2:]
        skill: Path | None = None
        model_findings: Path | None = None
        root: Path | None = None
        while args:
            flag = args.pop(0)
            if flag == "--skill" and args:
                skill = Path(args.pop(0))
            elif flag == "--model-findings" and args:
                model_findings = Path(args.pop(0))
            elif flag == "--root" and args:
                root = Path(args.pop(0))
            else:
                print("usage: python3 engine.py audit --skill <skill-path> "
                      "[--model-findings <json>] [--root <repo>]",
                      file=sys.stderr)
                return 2
        if skill is None or not skill.is_dir():
            print("audit requires --skill <skill-path> (the legislator "
                  "package root)", file=sys.stderr)
            return 2
        global ROOT, OKF, CASES
        ROOT = (root or Path.cwd()).resolve()
        OKF = ROOT / "docs" / "okf"
        CASES = ROOT / "docs" / "cases"
        try:
            report, any_findings = job_audit(skill, model_findings)
        except Exception as exc:               # noqa: BLE001 — deliberate
            print(f"engine failed: {type(exc).__name__}: {exc}", file=sys.stderr)
            return 3
        print(report, end="")
        return 1 if any_findings else 0
    if len(argv) != 2 or argv[1] not in JOBS:
        print(f"usage: python3 {Path(__file__).name} "
              f"{{{'|'.join(sorted(JOBS) + ['audit'])}}}", file=sys.stderr)
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
