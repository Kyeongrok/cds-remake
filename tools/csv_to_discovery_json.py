"""Convert CdsHelper/발견물힌트.csv → CdsHelper/발견물.json (one-shot)."""
import csv
import json
import sys
from pathlib import Path


def parse_int(value):
    value = (value or "").strip()
    if not value:
        return None
    try:
        return int(value)
    except ValueError:
        return None


def empty_to_none(value):
    value = (value or "").strip()
    return value if value else None


def main():
    repo_root = Path(__file__).resolve().parent.parent
    csv_path = repo_root / "CdsHelper" / "발견물힌트.csv"
    json_path = repo_root / "CdsHelper" / "발견물.json"

    if not csv_path.exists():
        print(f"CSV not found: {csv_path}", file=sys.stderr)
        sys.exit(1)

    rows = []
    with csv_path.open(encoding="utf-8-sig", newline="") as f:
        reader = csv.reader(f)
        next(reader)  # header
        for r in reader:
            if not r or not r[0].strip():
                continue
            id_ = parse_int(r[0])
            if id_ is None:
                continue
            rows.append({
                "id": id_,
                "name": (r[1] if len(r) > 1 else "").strip(),
                "hint": empty_to_none(r[2] if len(r) > 2 else None),
                "condition": empty_to_none(r[3] if len(r) > 3 else None),
                "appearCondition": empty_to_none(r[4] if len(r) > 4 else None),
                "bookName": empty_to_none(r[5] if len(r) > 5 else None),
                "latFrom": parse_int(r[6] if len(r) > 6 else None),
                "latTo": parse_int(r[7] if len(r) > 7 else None),
                "lonFrom": parse_int(r[8] if len(r) > 8 else None),
                "lonTo": parse_int(r[9] if len(r) > 9 else None),
            })

    with json_path.open("w", encoding="utf-8") as f:
        json.dump(rows, f, ensure_ascii=False, indent=2)

    print(f"Wrote {len(rows)} rows → {json_path}")


if __name__ == "__main__":
    main()
