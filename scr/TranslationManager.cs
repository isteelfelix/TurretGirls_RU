#!/usr/bin/env python3
import json, re
from pathlib import Path

RAW = Path(__file__).resolve().parents[1] / "dumps" / "raw"
OUT_DIR = Path(__file__).resolve().parents[1] / "translations" / "tmp"
OUT_DIR.mkdir(parents=True, exist_ok=True)
TEMPLATE = OUT_DIR / "ru_template.json"

def normalize(s: str) -> str:
    # чистим пробелы, многоточия и неразрывные
    s = re.sub(r"\s{2,}", " ", s.replace("\u00A0", " ")).strip()
    s = re.sub(r"\.{3,}", "...", s)
    return s

def collect_english_strings(obj):
    """
    Ищем массив text.Array и берём только [0] (английский).
    """
    if isinstance(obj, dict):
        for k, v in obj.items():
            if k == "Array" and isinstance(v, list) and v:
                if isinstance(v[0], str):
                    yield v[0]
            else:
                yield from collect_english_strings(v)
    elif isinstance(obj, list):
        for i in obj:
            yield from collect_english_strings(i)

def main():
    bank = {}
    for p in sorted(RAW.glob("*.json")):
        try:
            data = json.loads(p.read_text(encoding="utf-8"))
        except Exception as e:
            print(f"[WARN] {p.name}: {e}")
            continue
        for s in collect_english_strings(data):
            key = normalize(s)
            if key and key not in bank:
                bank[key] = ""
    ordered = dict(sorted(bank.items(), key=lambda kv: (len(kv[0]), kv[0])))
    TEMPLATE.write_text(json.dumps(ordered, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"[OK] Template written: {TEMPLATE} ({len(ordered)} keys)")

if __name__ == "__main__":
    main()
