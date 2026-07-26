#!/usr/bin/env python3
"""Fail the build on high/critical npm advisories in web/, minus time-boxed exceptions.

`npm audit --audit-level=high` alone cannot express "we looked at this one and the vulnerable
path is not reachable here", so the only ways to keep it green are to suppress everything or
to take an upgrade you are not ready for. This wraps it: the gate still fails on any high or
critical advisory, except those listed in tools/npm-audit-allowlist.json with a reason and a
review_by date. An exception covers one advisory ID on one package — a new advisory against
the same package still fails — and it stops working on its review_by date, so an accepted risk
has to be re-argued rather than forgotten.

Usage:
    python3 tools/npm-audit-gate.py [--web-dir web] [--allowlist tools/npm-audit-allowlist.json]

Exit codes: 0 = clean or fully accepted; 1 = unaccepted advisory, expired exception, or the
audit could not be run.
"""

from __future__ import annotations

import argparse
import datetime
import json
import os
import subprocess
import sys

BLOCKING = ("high", "critical")


def parse_args(argv):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--web-dir", default="web", help="directory holding package-lock.json (default: web)")
    p.add_argument("--allowlist", default=os.path.join("tools", "npm-audit-allowlist.json"))
    p.add_argument("--today", default=None, help="override today's date (YYYY-MM-DD), for testing")
    return p.parse_args(argv)


def run_audit(web_dir):
    """npm audit exits non-zero when it finds anything, so the exit code is not the signal."""
    proc = subprocess.run(
        ["npm", "audit", "--json"],
        cwd=web_dir, capture_output=True, text=True,
    )
    if not proc.stdout.strip():
        raise RuntimeError("npm audit produced no output:\n%s" % (proc.stderr or "(no stderr)"))
    try:
        return json.loads(proc.stdout)
    except json.JSONDecodeError as exc:
        raise RuntimeError("npm audit did not return JSON (%s):\n%s" % (exc, proc.stdout[:2000]))


def advisory_id(via):
    """GHSA id for one `via` object — from its advisory URL, falling back to the numeric source."""
    url = via.get("url") or ""
    tail = url.rstrip("/").rsplit("/", 1)[-1]
    if tail.startswith("GHSA-"):
        return tail
    return "npm-%s" % via.get("source", "unknown")


def advisories_for(name, vulns, seen=None):
    """Advisory ids affecting a package, following `via` strings to the packages they name."""
    seen = seen if seen is not None else set()
    if name in seen:
        return set()
    seen.add(name)
    found = set()
    for via in vulns.get(name, {}).get("via", []):
        if isinstance(via, dict):
            found.add(advisory_id(via))
        else:
            found |= advisories_for(via, vulns, seen)
    return found


def load_allowlist(path, today):
    with open(path, encoding="utf-8") as fh:
        data = json.load(fh)
    accepted, expired = {}, []
    for entry in data.get("accepted", []):
        review_by = datetime.date.fromisoformat(entry["review_by"])
        if review_by < today:
            expired.append((entry, review_by))
        else:
            accepted[entry["advisory"]] = entry
    return accepted, expired


def main(argv=None):
    args = parse_args(argv)
    today = datetime.date.fromisoformat(args.today) if args.today else datetime.date.today()

    try:
        report = run_audit(args.web_dir)
        accepted, expired = load_allowlist(args.allowlist, today)
    except (RuntimeError, OSError, ValueError, KeyError) as exc:
        print("::error::Could not run the web dependency audit: %s" % exc)
        return 1

    vulns = report.get("vulnerabilities", {})
    totals = report.get("metadata", {}).get("vulnerabilities", {})

    unaccepted, waived = [], []
    for name, entry in sorted(vulns.items()):
        severity = entry.get("severity", "")
        if severity not in BLOCKING:
            continue
        ids = advisories_for(name, vulns)
        remaining = sorted(i for i in ids if i not in accepted)
        if remaining or not ids:
            unaccepted.append((name, severity, remaining or ["(unidentified advisory)"]))
        else:
            waived.append((name, severity, sorted(ids)))

    print("npm audit — web/ dependencies")
    print("  totals: %s" % (", ".join("%s %s" % (v, k) for k, v in sorted(totals.items()) if v) or "none"))
    for name, severity, ids in waived:
        entry = accepted[ids[0]]
        print("  ACCEPTED  %-18s %-8s %s (review by %s)" % (name, severity, ", ".join(ids), entry["review_by"]))
    for name, severity, ids in unaccepted:
        print("  BLOCKING  %-18s %-8s %s" % (name, severity, ", ".join(ids)))

    for entry, review_by in expired:
        print("::error::Accepted npm advisory %s (%s) lapsed on %s. Re-review it: either take the "
              "fix, or extend review_by in %s with a fresh justification."
              % (entry["advisory"], entry.get("package", "?"), review_by, args.allowlist))
    for name, severity, ids in unaccepted:
        print("::error::%s advisory in web/ dependency '%s': %s. Fix it (`npm audit fix` in web/, "
              "or upgrade the package), or record a reviewed exception in %s."
              % (severity.capitalize(), name, ", ".join(ids), args.allowlist))

    stale = sorted(set(accepted) - {i for _n, _s, ids in waived for i in ids})
    for advisory in stale:
        print("::warning::Accepted advisory %s no longer appears in npm audit output — remove it "
              "from %s." % (advisory, args.allowlist))

    if expired or unaccepted:
        print("\nFAIL — web dependency audit blocked the build.")
        return 1
    print("\nPASS — no unaccepted high or critical advisories in web/.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
