#!/usr/bin/env python3
"""Merge coverlet Cobertura reports, report line coverage, and enforce the NFR-5 threshold.

NFR-5 (REQUIREMENTS.md §6) asks for ">=70% coverage on ingestion/API core". That is a claim
about specific code, not about the repository average, so this script gates on an explicit
scope (GATE_SCOPE below) and prints everything else as information only. Gate 0 closed with a
waiver precisely because the 70% figure lived in the docs while CI measured nothing
(IMPLEMENTATION-PLAN.md §9, retro lesson 2).

Merging matters: `dotnet test` on the solution emits one report per test project, and the same
source line appears in several of them (Api.Tests exercises Core through the API). A line is
covered if *any* report hit it, so lines are merged by (assembly, source file, line number)
taking the maximum hit count. Reports also use different relative roots, so each report's
<source> base is joined back on before paths are made relative to the repo root.

Usage:
    python3 tools/coverage-gate.py --results-dir artifacts/coverage [--threshold 70]

Exit codes: 0 = at or above threshold; 1 = below threshold, no reports found, or a scope entry
matched no measured code (a renamed folder must fail loudly, not silently measure nothing).
"""

from __future__ import annotations

import argparse
import glob
import os
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict

# The code NFR-5 names, as repo-relative path prefixes; a file is attributed to its longest
# matching prefix. Anything not listed here is still measured and printed, but never gates.
#
# "ingestion" is read as the whole ingestion component — SpanSight.Ingestion end to end, its
# CLI entry point included — plus the Core parsing, validation, coordinate-conversion and
# classification code the pipeline runs on every row. "API core" is read as the querying path
# (query builder + read endpoints); SpanSight.Api/Program.cs is host and middleware wiring
# rather than querying, so it stays outside. Also outside: Core/Data (EF model), Core/Ai and
# Api/Ai (Phase 0.5 FR-AI.1, tracked on its own row — AnthropicAssistant is the live-key path
# and cannot be unit-tested), and the Dtos.
GATE_SCOPE = [
    ("src/SpanSight.Core/Ingestion/", "Core — NBI parsing + row validation"),
    ("src/SpanSight.Core/Geo/", "Core — DMS to WGS84 conversion"),
    ("src/SpanSight.Core/Domain/", "Core — condition classification + NBI lookups"),
    ("src/SpanSight.Core/Filtering/", "Core — shared filter predicate"),
    ("src/SpanSight.Ingestion/", "Ingestion — load pipeline + CLI"),
    ("src/SpanSight.Api/Querying/", "API — query builder"),
    ("src/SpanSight.Api/Endpoints/", "API — read endpoints"),
]


def parse_args(argv):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--results-dir", required=True, help="directory dotnet test wrote coverage into")
    p.add_argument("--repo-root", default=os.getcwd(), help="root the report paths are made relative to")
    p.add_argument("--threshold", type=float, default=70.0, help="minimum scoped line coverage %% (default: 70)")
    p.add_argument("--summary-file", default=os.environ.get("GITHUB_STEP_SUMMARY"),
                   help="write a markdown summary here (defaults to $GITHUB_STEP_SUMMARY)")
    return p.parse_args(argv)


def resolve(bases, filename):
    """Turn a report-relative filename into an absolute path using the report's <source> bases."""
    filename = filename.replace("\\", os.sep)
    candidates = [os.path.normpath(os.path.join(b, filename)) for b in bases] or [filename]
    for c in candidates:
        if os.path.exists(c):
            return c
    return candidates[0]


def merge_reports(paths, repo_root):
    """-> {(assembly, repo-relative path, line number): max hits across all reports}."""
    merged = {}
    for path in paths:
        root = ET.parse(path).getroot()
        bases = [s.text for s in root.iter("source") if s.text]
        for package in root.iter("package"):
            assembly = package.get("name") or "(unknown)"
            for cls in package.iter("class"):
                abs_path = resolve(bases, cls.get("filename") or "")
                rel = os.path.relpath(abs_path, repo_root).replace(os.sep, "/")
                for line in cls.iter("line"):
                    key = (assembly, rel, int(line.get("number")))
                    hits = int(line.get("hits") or 0)
                    if hits > merged.get(key, -1):
                        merged[key] = hits
    return merged


def tally(merged):
    """-> {(assembly, path): [covered, total]}."""
    per_file = defaultdict(lambda: [0, 0])
    for (assembly, rel, _line), hits in merged.items():
        entry = per_file[(assembly, rel)]
        entry[1] += 1
        if hits > 0:
            entry[0] += 1
    return per_file


def scope_of(rel):
    """The longest GATE_SCOPE prefix matching this file, or None when it is out of scope."""
    best = None
    for prefix, label in GATE_SCOPE:
        if rel.startswith(prefix) and (best is None or len(prefix) > len(best[0])):
            best = (prefix, label)
    return best


def pct(covered, total):
    return 100.0 * covered / total if total else 0.0


def fmt(covered, total):
    return "n/a" if not total else "%.1f%%" % pct(covered, total)


def main(argv=None):
    args = parse_args(argv)
    repo_root = os.path.abspath(args.repo_root)

    reports = sorted(glob.glob(os.path.join(args.results_dir, "**", "coverage.cobertura.xml"), recursive=True))
    if not reports:
        print("ERROR: no coverage.cobertura.xml under %s — did the test step collect coverage?"
              % args.results_dir, file=sys.stderr)
        return 1

    per_file = tally(merge_reports(reports, repo_root))

    # Gate scope, grouped by the labels above and ordered as declared.
    by_label = defaultdict(lambda: [0, 0])
    scoped_files = defaultdict(list)
    per_assembly = defaultdict(lambda: [0, 0])
    out_of_scope = defaultdict(lambda: [0, 0])

    for (assembly, rel), (covered, total) in per_file.items():
        entry = per_assembly[assembly]
        entry[0] += covered
        entry[1] += total
        hit = scope_of(rel)
        if hit:
            label = hit[1]
            by_label[label][0] += covered
            by_label[label][1] += total
            scoped_files[label].append((rel, covered, total))
        else:
            out_of_scope[assembly][0] += covered
            out_of_scope[assembly][1] += total

    labels = []
    for _prefix, label in GATE_SCOPE:
        if label not in labels:
            labels.append(label)

    empty = [label for label in labels if by_label[label][1] == 0]

    gate_covered = sum(by_label[label][0] for label in labels)
    gate_total = sum(by_label[label][1] for label in labels)
    gate_pct = pct(gate_covered, gate_total)
    passed = gate_total > 0 and gate_pct >= args.threshold and not empty

    repo_covered = sum(v[0] for v in per_assembly.values())
    repo_total = sum(v[1] for v in per_assembly.values())

    text, markdown = render(args, reports, labels, by_label, scoped_files, per_assembly,
                            out_of_scope, gate_covered, gate_total, gate_pct,
                            repo_covered, repo_total, empty, passed)
    print(text)
    sys.stdout.flush()  # keep the failure lines below the table in interleaved CI logs
    if args.summary_file:
        with open(args.summary_file, "a", encoding="utf-8") as fh:
            fh.write(markdown)

    for label in empty:
        print("ERROR: gate scope entry '%s' matched no measured code — has the folder moved?"
              % label, file=sys.stderr)
    if not passed:
        print("ERROR: scoped line coverage %s is below the NFR-5 threshold of %.0f%%."
              % (fmt(gate_covered, gate_total), args.threshold), file=sys.stderr)
        print("HINT: the gate assumes the whole suite ran. Without a container runtime the "
              "Testcontainers integration tests skip and the API rows collapse.", file=sys.stderr)
        return 1
    return 0


def render(args, reports, labels, by_label, scoped_files, per_assembly, out_of_scope,
           gate_covered, gate_total, gate_pct, repo_covered, repo_total, empty, passed):
    verdict = "PASS" if passed else "FAIL"
    t = []
    m = []

    t.append("Coverage gate (NFR-5) — merged from %d report(s)" % len(reports))
    t.append("")
    t.append("Gated scope — ingestion/API core")
    t.append("  %-46s %8s %8s %8s" % ("AREA", "COVERED", "LINES", "LINE %"))
    m.append("## Test coverage — NFR-5 gate: **%s**\n" % verdict)
    m.append("Scoped line coverage **%s** vs a **%.0f%%** threshold "
             "(%d/%d lines), merged from %d report(s).\n"
             % (fmt(gate_covered, gate_total), args.threshold, gate_covered, gate_total, len(reports)))
    m.append("### Gated scope — ingestion/API core\n")
    m.append("| Area | Covered | Lines | Line % |")
    m.append("|---|---:|---:|---:|")
    for label in labels:
        covered, total = by_label[label]
        t.append("  %-46s %8d %8d %8s" % (label, covered, total, fmt(covered, total)))
        m.append("| %s | %d | %d | %s |" % (label, covered, total, fmt(covered, total)))
    t.append("  %-46s %8d %8d %8s" % ("TOTAL (gated)", gate_covered, gate_total, fmt(gate_covered, gate_total)))
    m.append("| **Total (gated)** | **%d** | **%d** | **%s** |"
             % (gate_covered, gate_total, fmt(gate_covered, gate_total)))
    m.append("")

    t.append("")
    t.append("Per assembly — all measured code, informational only (not the gate)")
    t.append("  %-46s %8s %8s %8s" % ("ASSEMBLY", "COVERED", "LINES", "LINE %"))
    m.append("### Per assembly — informational, **not** the gate\n")
    m.append("| Assembly | Covered | Lines | Line % | of which outside the gate |")
    m.append("|---|---:|---:|---:|---:|")
    for assembly in sorted(per_assembly):
        covered, total = per_assembly[assembly]
        oc, ot = out_of_scope[assembly]
        t.append("  %-46s %8d %8d %8s   (outside gate: %d/%d)"
                 % (assembly, covered, total, fmt(covered, total), oc, ot))
        m.append("| %s | %d | %d | %s | %d/%d |" % (assembly, covered, total, fmt(covered, total), oc, ot))
    t.append("  %-46s %8d %8d %8s" % ("TOTAL (repo-wide)", repo_covered, repo_total, fmt(repo_covered, repo_total)))
    m.append("| **Total (repo-wide)** | **%d** | **%d** | **%s** | |"
             % (repo_covered, repo_total, fmt(repo_covered, repo_total)))
    m.append("")

    m.append("<details><summary>Gated files</summary>\n")
    m.append("| File | Covered | Lines | Line % |")
    m.append("|---|---:|---:|---:|")
    for label in labels:
        for rel, covered, total in sorted(scoped_files[label]):
            m.append("| `%s` | %d | %d | %s |" % (rel, covered, total, fmt(covered, total)))
    m.append("\n</details>\n")

    t.append("")
    if empty:
        t.append("FAIL — gate scope entries matched no measured code: %s" % ", ".join(empty))
    t.append("%s — scoped %s vs threshold %.0f%% (repo-wide %s, informational)"
             % (verdict, fmt(gate_covered, gate_total), args.threshold, fmt(repo_covered, repo_total)))
    m.append("Measurement scope is set by `coverlet.runsettings` (EF migrations and generated "
             "code excluded); the gated scope is `GATE_SCOPE` in `tools/coverage-gate.py`.\n")
    return "\n".join(t), "\n".join(m) + "\n"


if __name__ == "__main__":
    sys.exit(main())
