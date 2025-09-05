#!/usr/bin/env python3
import json, re
from pathlib import Path

ROOT   = Path(__file__).resolve().parents[1]
RAW    = ROOT / "dumps" / "raw"
MERGED = ROOT / "dumps" / "merged"
TR_DIR = ROOT / "translations" / "ru"
MERGED.mkdir(parents=True, exist_ok=True)

def norm(s: str) -> str:
    s = s.replace("\u00A0", " ")
    s = re.sub(r"\s{2,}", " ", s).strip()
    s = re.sub(r"\.{3,}", "...", s)
    return s

def load_ru_bank():
    bank = {}
    for p in sorted(TR_DIR.glob("*.json")):
        data = json.loads(p.read_text(encoding="utf-8"))
        for k,v in data.items():
            k2 = norm(k)
            if v and k2 not in bank:
                bank[k2] = v
    return bank

def apply_to_obj(obj, bank, stats):
    # нас интересуют только text.Array[0] (англ. строка)
    if isinstance(obj, dict):
        if "Array" in obj and isinstance(obj["Array"], list) and obj["Array"]:
            if isinstance(obj["Array"][0], str):
                en = obj["Array"][0]
                k  = norm(en)
                if k in bank and bank[k]:
                    obj["Array"][0] = bank[k]
                    stats["replaced"] += 1
                else:
                    stats["missed"] += 1
        for v in obj.values():
            apply_to_obj(v, bank, stats)
    elif isinstance(obj, list):
        for it in obj:
            apply_to_obj(it, bank, stats)

def main():
    bank = load_ru_bank()
    print(f"[INFO] loaded translations: {len(bank)}")
    for p in sorted(RAW.glob("*.json")):
        data = json.loads(p.read_text(encoding="utf-8"))
        stats = {"replaced":0, "missed":0}
        apply_to_obj(data, bank, stats)
        out = MERGED / p.name
        out.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"[OK] {p.name}: replaced={stats['replaced']} missed={stats['missed']} -> {out}")

if __name__ == "__main__":
    main()
