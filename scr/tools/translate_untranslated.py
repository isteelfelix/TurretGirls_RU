"""
translate_untranslated.py

Offline translator: reads `untranslated.json` (a JSON array or set of strings) and
prefills translations using OpenAI's API, producing a merged `ru.json` draft.

Usage:
  - Set environment variable OPENAI_API_KEY with your key.
  - Run: python scr\tools\translate_untranslated.py --input path/to/untranslated.json --out path/to/ru_draft.json --lang ru

Notes:
  - This script only performs batch translation suggestions. Review results manually
    before merging into the final `ru.json` used by the mod.
  - Install dependencies from `scr\tools\requirements.txt`.
"""

import os
import json
import argparse
from typing import List
from pathlib import Path

try:
    # optional: load .env in project root if present
    from dotenv import load_dotenv
except Exception:
    load_dotenv = None

try:
    import openai
except Exception as e:
    raise SystemExit("openai package not installed. Run: pip install -r scr/tools/requirements.txt")


def load_untranslated(path: str) -> List[str]:
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    # accept dict/set/list formats
    if isinstance(data, dict):
        keys = list(data.keys())
    elif isinstance(data, list):
        keys = data
    elif isinstance(data, str):
        keys = [data]
    else:
        # try to coerce
        keys = list(data)
    return keys


def translate_batch(openai_key: str, texts: List[str], target_lang: str = 'ru') -> List[str]:
    openai.api_key = openai_key
    out = []
    for t in texts:
        prompt = f"Translate the following string to {target_lang} preserving formatting and placeholders. Return only the translation.\n\n" + t
        try:
            resp = openai.ChatCompletion.create(
                model='gpt-4o-mini',
                messages=[{"role": "user", "content": prompt}],
                max_tokens=1024,
                temperature=0.2
            )
            text = resp['choices'][0]['message']['content'].strip()
        except Exception as ex:
            print(f"API error for text: {t[:30]}...: {ex}")
            text = ""
        out.append(text)
    return out


def merge_into_ru(existing_ru_path: str, keys: List[str], translations: List[str], out_path: str):
    # Load existing ru.json if present
    ru = {}
    if existing_ru_path and os.path.exists(existing_ru_path):
        try:
            with open(existing_ru_path, 'r', encoding='utf-8') as f:
                ru = json.load(f)
        except Exception as e:
            print(f"Failed to load existing ru.json: {e}")
    for k, v in zip(keys, translations):
        if not v:
            continue
        # Only add if missing to avoid overwriting manual translations
        if k not in ru:
            ru[k] = v
    with open(out_path, 'w', encoding='utf-8') as f:
        json.dump(ru, f, ensure_ascii=False, indent=2)
    print(f"Wrote merged translations to {out_path}")


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    # default paths per project layout
    project_root = Path(__file__).resolve().parents[2]
    default_tmp = project_root / 'translations' / 'tmp' / 'untranslated.json'
    default_out = project_root / 'translations' / 'ru' / 'ru.json'

    p.add_argument('--input', '-i', default=str(default_tmp))
    p.add_argument('--out', '-o', default=str(default_out))
    p.add_argument('--ru', dest='ru', default=str(default_out), help='Path to existing ru.json to merge into (optional)')
    p.add_argument('--lang', default='ru')
    p.add_argument('--model', default='gpt-3.5-turbo', help='OpenAI model to use (cost/quality tradeoff)')
    p.add_argument('--batch-size', type=int, default=40, help='Number of strings per batch to send to API')
    p.add_argument('--confirm', action='store_true', help='Confirm and actually call the API (required to spend credits)')
    args = p.parse_args()

    # load .env in project root if available
    project_root = Path(__file__).resolve().parents[2]
    env_path = project_root / '.env'
    if load_dotenv and env_path.exists():
        load_dotenv(dotenv_path=str(env_path))

    key = os.environ.get('OPENAI_API_KEY')
    if not key:
        print('Please set OPENAI_API_KEY environment variable or place it in .env in project root.')
        raise SystemExit(1)

    keys = load_untranslated(args.input)
    if not keys:
        print('No keys found in input file')
        raise SystemExit(0)

    print(f'Loaded {len(keys)} untranslated keys; will translate {len(keys)} items in batches of {args.batch_size} (confirm={args.confirm})')

    # ensure output dir exists
    out_path = Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)

    # if not confirmed, perform dry-run and exit
    if not args.confirm:
        print('Dry-run: no API calls will be made. Re-run with --confirm to actually call OpenAI and write translations.')
        raise SystemExit(0)

    # batch processing
    translations_all = []
    for i in range(0, len(keys), args.batch_size):
        batch = keys[i:i+args.batch_size]
        print(f'Translating batch {i // args.batch_size + 1}: {len(batch)} items...')
        batch_trans = translate_batch(key, batch, args.lang)
        translations_all.extend(batch_trans)

    merge_into_ru(args.ru, keys, translations_all, args.out)
