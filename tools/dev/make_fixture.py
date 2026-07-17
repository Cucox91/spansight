#!/usr/bin/env python3
"""Generates the committed NBI test fixture (src/tests/fixtures/nbi_sample_2025.csv).

Synthetic data only — invented structure numbers and plausible-but-fake attribute values in the
real FHWA delimited layout (Coding Guide era). No bulk NBI data enters git (CLAUDE.md rule 4);
the fixture is a few hundred rows and every quarantine reason (FR-0.2) has at least one row.

Deterministic: fixed seed, stable output — re-running must not churn the committed file.
"""

from __future__ import annotations

import csv
import random
from pathlib import Path

OUT = Path(__file__).resolve().parents[2] / "src" / "tests" / "fixtures" / "nbi_sample_2025.csv"

HEADER = [
    "STATE_CODE_001", "COUNTY_CODE_003", "RECORD_TYPE_005A", "FEATURES_DESC_006A",
    "FACILITY_CARRIED_007", "STRUCTURE_NUMBER_008", "LOCATION_009", "LAT_016", "LONG_017",
    "YEAR_BUILT_027", "ADT_029", "STRUCTURE_KIND_043A", "STRUCTURE_TYPE_043B",
    "STRUCTURE_LEN_MT_049", "DECK_COND_058", "SUPERSTRUCTURE_COND_059",
    "SUBSTRUCTURE_COND_060", "CULVERT_COND_062",
]


def dms_lat(dec: float) -> str:
    deg = int(dec)
    rem = (dec - deg) * 60
    minutes = int(rem)
    hundredth_seconds = round((rem - minutes) * 60 * 100)
    if hundredth_seconds >= 6000:  # rounding carried into the next minute
        hundredth_seconds -= 6000
        minutes += 1
    return f"{deg:02d}{minutes:02d}{hundredth_seconds:04d}"


def dms_lon(dec_west_positive: float) -> str:
    deg = int(dec_west_positive)
    rem = (dec_west_positive - deg) * 60
    minutes = int(rem)
    hundredth_seconds = round((rem - minutes) * 60 * 100)
    if hundredth_seconds >= 6000:
        hundredth_seconds -= 6000
        minutes += 1
    return f"{deg:03d}{minutes:02d}{hundredth_seconds:04d}"


# (fips, county, anchor lat, anchor lon(W+), n extra rows)
STATES = [
    ("12", "086", 25.78, 80.21, 14),  # FL Miami-Dade
    ("12", "057", 27.96, 82.44, 6),   # FL Hillsborough
    ("48", "201", 29.77, 95.37, 10),  # TX Harris
    ("06", "037", 34.04, 118.23, 10), # CA Los Angeles
    ("36", "047", 40.68, 73.94, 8),   # NY Kings
    ("42", "003", 40.43, 79.99, 6),   # PA Allegheny
    ("39", "061", 39.10, 84.55, 6),   # OH Hamilton
    ("53", "033", 47.65, 122.35, 6),  # WA King
    ("08", "031", 39.72, 104.99, 5),  # CO Denver
    ("13", "121", 33.75, 84.46, 5),   # GA Fulton
    ("17", "031", 41.85, 87.63, 6),   # IL Cook
    ("29", "510", 38.65, 90.18, 4),   # MO St. Louis city
    ("22", "071", 29.98, 90.09, 4),   # LA Orleans
    ("27", "053", 44.98, 93.25, 4),   # MN Hennepin
    ("47", "157", 35.09, 90.02, 4),   # TN Shelby
]

# (43A, 43B, deck-ish rating anchor) — culvert designs get their rating on item 62.
TYPES = [
    ("3", "02", 5), ("5", "02", 7), ("1", "19", 5), ("1", "11", 6), ("3", "10", 4),
    ("3", "09", 4), ("3", "16", 4), ("1", "01", 5), ("1", "05", 7), ("3", "12", 6),
    ("1", "02", 6), ("4", "02", 6), ("6", "05", 7), ("7", "01", 4), ("8", "11", 5),
]

FEATURES = ["RIVER", "CREEK", "CANAL", "RAILROAD", "US-1", "I-95", "SR-836", "BAYOU", "DITCH", "BRANCH"]
FACILITIES = ["US-1", "I-95", "SR-7", "CR-905", "MAIN ST", "NW 12 AVE", "RIVER RD", "OLD MILL RD"]


def valid_rows() -> list[list[str]]:
    rng = random.Random(20260717)
    rows: list[list[str]] = []
    for fips, county, lat0, lon0, extra in STATES:
        for i in range(extra):
            kind, design, anchor = TYPES[rng.randrange(len(TYPES))]
            lat = lat0 + rng.uniform(-0.18, 0.18)
            lon = lon0 + rng.uniform(-0.18, 0.18)
            year = rng.randrange(1925, 2021)
            adt = rng.choice([1200, 4800, 9800, 15600, 34000, 52000, 88000, 142000])
            ratings = [max(2, min(9, anchor + rng.randrange(-1, 3))) for _ in range(3)]
            if design == "19":  # culvert: 58–60 are N, 62 carries the rating
                deck = sup = sub = "N"
                culvert = str(min(ratings))
            else:
                deck, sup, sub = (str(r) for r in ratings)
                culvert = "N"
            rows.append([
                fips, county, "1",
                f"{rng.choice(FEATURES)}",
                f"{rng.choice(FACILITIES)}",
                f"{fips}{rng.randrange(10_000, 99_999):05d}{i:03d}",
                f"{rng.randrange(1, 20)} MI FROM CITY",
                dms_lat(lat), dms_lon(lon),
                str(year), str(adt), kind, design,
                f"{rng.randrange(120, 18_000) / 10:.1f}",
                deck, sup, sub, culvert,
            ])
    # A quoted field with an embedded comma (parser must handle RFC 4180 quoting).
    rows.append([
        "12", "086", "1", "MIAMI RIVER, NORTH FORK", "NW 27 AVE", "1287001Q001",
        "3 MI W OF DOWNTOWN", dms_lat(25.807), dms_lon(80.24), "1968", "31000",
        "5", "02", "142.3", "6", "6", "5", "N",
    ])
    return rows


def bad_rows() -> list[list[str]]:
    """One row per quarantine reason (plus structural faults appended as raw lines below)."""
    return [
        # coordinate_missing_or_zero (zeros)
        ["12", "086", "1", "CANAL", "SW 8 ST", "12ZERO000001", "", "0", "0", "1970", "12000", "3", "02", "88.0", "6", "6", "6", "N"],
        # coordinate_missing_or_zero (blank)
        ["48", "201", "1", "BAYOU", "MAIN ST", "48BLANK00001", "", "", "", "1985", "9000", "5", "02", "64.0", "7", "7", "7", "N"],
        # coordinate_invalid (minutes ≥ 60)
        ["36", "047", "1", "RIVER", "ATLANTIC AVE", "36BADMIN0001", "", "40691560", "073561200", "1931", "27000", "3", "02", "120.0", "4", "4", "4", "N"],
        # coordinate_invalid (too many digits)
        ["06", "037", "1", "CREEK", "SR-1", "06MALFORM001", "", "340415601", "1181330000", "1962", "44000", "3", "02", "97.0", "5", "5", "5", "N"],
        # coordinate_outside_state (Miami coordinates on a CA record)
        ["06", "037", "1", "RIVER", "I-110", "06OUTSIDE001", "", "25470000", "080130000", "1971", "88000", "4", "02", "210.0", "6", "6", "6", "N"],
        # unknown_state_code
        ["83", "001", "1", "CREEK", "CR-1", "83UNKNOWN001", "", "35000000", "090000000", "1965", "4000", "1", "01", "30.0", "5", "5", "5", "N"],
        # year_built_impossible (future)
        ["12", "057", "1", "RIVER", "US-92", "12FUTURE0001", "", "27580000", "082263000", "2199", "41000", "5", "02", "180.0", "7", "7", "7", "N"],
        # year_built_impossible (non-numeric)
        ["39", "061", "1", "CREEK", "US-50", "39BADYEAR001", "", "39061000", "084325200", "18XX", "44000", "3", "02", "77.0", "5", "5", "5", "N"],
        # adt_invalid (negative)
        ["42", "003", "1", "RIVER", "LIBERTY BR", "42NEGADT0001", "", "40254000", "079594500", "1928", "-5", "3", "10", "884.0", "5", "4", "4", "N"],
        # adt_invalid (non-numeric)
        ["53", "033", "1", "SHIP CANAL", "SR-99", "53BADADT0001", "", "47385000", "122204900", "1932", "abc", "1", "11", "355.0", "6", "5", "5", "N"],
        # condition_code_invalid
        ["17", "031", "1", "RIVER", "CERMAK RD", "17BADCOND001", "", "41511000", "087380500", "1906", "14700", "3", "16", "94.0", "X", "4", "3", "N"],
        # structure_length_invalid (negative)
        ["13", "121", "1", "RIVER", "I-20", "13BADLEN0001", "", "33450700", "084274700", "1963", "132000", "3", "02", "-3.0", "6", "5", "5", "N"],
        # duplicate_key (same key as the quoted Miami row above)
        ["12", "086", "1", "MIAMI RIVER DUPE", "NW 27 AVE", "1287001Q001", "", dms_lat(25.807), dms_lon(80.24), "1968", "31000", "5", "02", "142.3", "6", "6", "5", "N"],
    ]


STRUCTURAL_FAULT_LINES = [
    # row_structural_fault: field count mismatch (one extra comma)
    '48,201,1,EXTRA FIELD ROW,MAIN ST,48STRUCT0001,,29461560,095221920,1968,142000,3,02,88.0,6,5,5,N,SURPLUS',
    # row_structural_fault: blank key (no structure number)
    '12,086,1,CANAL,SW 8 ST,,,25470000,080130000,1970,12000,3,02,88.0,6,6,6,N',
]


def main() -> None:
    OUT.parent.mkdir(parents=True, exist_ok=True)
    valid = valid_rows()
    bad = bad_rows()
    with OUT.open("w", newline="") as f:
        writer = csv.writer(f, quoting=csv.QUOTE_MINIMAL)
        writer.writerow(HEADER)
        for row in valid + bad:
            writer.writerow(row)
        f.flush()
    with OUT.open("a", newline="") as f:
        for line in STRUCTURAL_FAULT_LINES:
            f.write(line + "\r\n")

    total = len(valid) + len(bad) + len(STRUCTURAL_FAULT_LINES)
    # duplicate_key surfaces at load time, so "valid" here means rows that pass row validation.
    print(f"rows written (excl. header): {total}")
    print(f"  pass row validation: {len(valid) + 1}  (incl. 1 duplicate caught at load)")
    print(f"  quarantined at validation: {len(bad) - 1}")
    print(f"  structural faults: {len(STRUCTURAL_FAULT_LINES)}")


if __name__ == "__main__":
    main()
