#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
سازندهٔ دادهٔ اولیهٔ «جدول دانش سئو» (مرحلهٔ شانزدهم).

خروجی: src/Fratak-Shop.Core/Services/Seo/Knowledge/Data/seo-knowledge.tsv.gz
  هر خط: kind<TAB>term<TAB>value<TAB>topic<TAB>weight
  این فایل داخل Fratak-Shop.Core جاسازی (EmbeddedResource) می‌شود و هنگام اجرای برنامه
  SeoKnowledgeSeeder ردیف‌هایش را (به‌جز kind=known) در جدول SeoKnowledgeEntries می‌ریزد.

منبع‌ها:
  ۱) واژه‌نامهٔ hazm (پروژهٔ متن‌باز roshan-research/hazm، مجوز MIT): words.dat (واژه + بسامد + نقش دستوری)،
     verbs.dat (بن ماضی#بن مضارع)، stopwords.dat، iwords.dat (محاوره ← رسمی)، abbreviations.dat.
     اگر پوشهٔ داده داده نشود، چرخ (wheel) hazm با pip دانلود و باز می‌شود.
  ۲) فهرست‌های دست‌نوشتهٔ curated/*.tsv (غلط‌های رایج، واژه‌های مرکب فروشگاهی، نیت جست‌وجو، اسپم و …).

اجرا:
  python3 tools/seo-knowledge/build_seed.py            # دانلود خودکار hazm
  python3 tools/seo-knowledge/build_seed.py /path/to/hazm/data
بعد از تغییر فهرست‌ها، عدد SeedVersion در SeoKnowledgeSeed.cs را یکی بالا ببرید تا سرورها دوباره بذر بریزند.
"""
import glob
import gzip
import math
import os
import subprocess
import sys
import tempfile
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(ROOT, "src", "Fratak-Shop.Core", "Services", "Seo", "Knowledge", "Data", "seo-knowledge.tsv.gz")
ZWNJ = "\u200c"

# واژه‌های مرکبی که شکل جدایشان معنای دیگری هم دارد (نباید خودکار سر هم شوند).
AMBIGUOUS = {"آب‌جو", "بی‌بی", "وقتی‌که", "هیچ‌گونه", "سه‌شنبه", "پنج‌شنبه"}
VERB_TAILS = ("شده", "شدن", "کرده", "کردن", "داده", "دادن", "گرفته", "گرفتن", "زده", "زدن", "شود", "کند")
LIGHT_HEADS = {"که", "و", "را", "این", "آن", "از", "در", "با", "بر", "تا"}
# «یک گونه» (یک نوع) با «یک‌گونه» (یکسان) فرق دارد؛ عدد اول ترکیب = خودکار سر هم نشود.
NUMBER_HEADS = {"یک", "دو", "سه", "چهار", "پنج", "شش", "هفت", "هشت", "نه", "ده", "صد", "هزار", "چند", "هر", "هم"}

ONES = ["", "یک", "دو", "سه", "چهار", "پنج", "شش", "هفت", "هشت", "نه"]
TEENS = ["ده", "یازده", "دوازده", "سیزده", "چهارده", "پانزده", "شانزده", "هفده", "هجده", "نوزده"]
TENS = ["", "", "بیست", "سی", "چهل", "پنجاه", "شصت", "هفتاد", "هشتاد", "نود"]
FA_DIGITS = str.maketrans("0123456789", "۰۱۲۳۴۵۶۷۸۹")


def hazm_data_dir(argv):
    if len(argv) > 1:
        return argv[1]
    tmp = tempfile.mkdtemp(prefix="hazm-")
    subprocess.check_call([sys.executable, "-m", "pip", "download", "hazm==0.10.0", "--no-deps", "-q", "-d", tmp])
    wheel = glob.glob(os.path.join(tmp, "hazm-*.whl"))[0]
    with zipfile.ZipFile(wheel) as z:
        z.extractall(tmp)
    return os.path.join(tmp, "hazm", "data")


def clean(value):
    return (value or "").replace("\t", " ").replace("\r", "").replace("\n", " ").strip()


def number_words():
    """۱ تا ۱۰۰ به حروف ← رقم فارسی. مثال: «بیست و پنج» ← «۲۵»."""
    rows = []
    for n in range(1, 101):
        if n < 10:
            word = ONES[n]
        elif n < 20:
            word = TEENS[n - 10]
        elif n == 100:
            word = "صد"
        else:
            word = TENS[n // 10] + ("" if n % 10 == 0 else " و " + ONES[n % 10])
        rows.append(("number", word, str(n).translate(FA_DIGITS), "", 1.0))
    return rows


def curated_rows():
    rows = []
    for path in sorted(glob.glob(os.path.join(HERE, "curated", "*.tsv"))):
        kind = os.path.splitext(os.path.basename(path))[0]
        with open(path, encoding="utf-8") as f:
            for line in f:
                line = line.rstrip("\n")
                if not line.strip() or line.startswith("#"):
                    continue
                parts = line.split("\t")
                while len(parts) < 4:
                    parts.append("")
                term, value, topic, weight = (clean(p) for p in parts[:4])
                if not term:
                    continue
                # ردیف «همان = همان» (فقط برای یادداشت) بی‌فایده است.
                if kind in ("correction", "compound", "unit", "style") and value == term:
                    continue
                rows.append((kind, term, value, topic, float(weight or 1)))
    return rows


def hazm_rows(data):
    rows = []
    words = []
    with open(os.path.join(data, "words.dat"), encoding="utf-8") as f:
        for line in f:
            parts = line.rstrip("\n").split("\t")
            if len(parts) < 3 or not parts[0]:
                continue
            words.append((parts[0], int(parts[1] or 0), parts[2]))
    plain = {w for w, _, _ in words if ZWNJ not in w}

    for word, freq, pos in words:
        if ZWNJ in word:
            continue
        if freq > 0:
            rows.append(("word", word, pos, "", round(math.log10(freq + 1), 3)))
        else:
            rows.append(("known", word, "", "", 0.0))

    for word, freq, pos in words:
        if ZWNJ not in word:
            continue
        parts = word.split(ZWNJ)
        good = (freq >= 200 and word not in AMBIGUOUS and not word.endswith(VERB_TAILS)
                and parts[0] not in LIGHT_HEADS and parts[0] not in NUMBER_HEADS and parts[-1] not in LIGHT_HEADS)
        if good:
            rows.append(("compound", " ".join(parts), word, "hazm", round(math.log10(freq + 1), 3)))
            joined = "".join(parts)
            if joined not in plain:
                rows.append(("compound", joined, word, "hazm", round(math.log10(freq + 1), 3)))
        elif freq > 0:
            rows.append(("word", word, pos, "", round(math.log10(freq + 1), 3)))
        else:
            rows.append(("known", word, "", "", 0.0))

    with open(os.path.join(data, "verbs.dat"), encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line or "#" not in line:
                continue
            past, present = (p.strip() for p in line.split("#", 1))
            if past and present and " " not in present:
                rows.append(("verb", past, present, "", 1.0))

    with open(os.path.join(data, "stopwords.dat"), encoding="utf-8") as f:
        for line in f:
            if line.strip():
                rows.append(("stop", line.strip(), "", "", 1.0))

    with open(os.path.join(data, "iwords.dat"), encoding="utf-8") as f:
        for line in f:
            parts = line.strip().split(" ", 1)
            if len(parts) == 2 and parts[0] != parts[1]:
                rows.append(("informal", parts[0], parts[1].strip(), "", 0.8))

    with open(os.path.join(data, "abbreviations.dat"), encoding="utf-8") as f:
        for line in f:
            if line.strip():
                rows.append(("abbr", line.strip(), "", "", 1.0))
    return rows


def main(argv):
    data = hazm_data_dir(argv)
    rows = curated_rows() + number_words() + hazm_rows(data)

    # کلید یکتا = (نوع، واژه، موضوع)؛ اولین ردیف (فهرست دست‌نوشته) برنده است.
    seen = set()
    unique = []
    for kind, term, value, topic, weight in rows:
        term, value, topic = clean(term)[:200], clean(value)[:1000], clean(topic)[:100]
        key = (kind, term, topic)
        if key in seen:
            continue
        seen.add(key)
        unique.append((kind, term, value, topic, weight))

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    body = "\n".join(f"{k}\t{t}\t{v}\t{p}\t{w:g}" for k, t, v, p, w in unique) + "\n"
    with gzip.GzipFile(OUT, "wb", compresslevel=9, mtime=0) as f:
        f.write(body.encode("utf-8"))

    counts = {}
    for row in unique:
        counts[row[0]] = counts.get(row[0], 0) + 1
    stored = sum(c for k, c in counts.items() if k != "known")
    print(f"{OUT}: {len(unique)} rows ({stored} stored in DB), {os.path.getsize(OUT)} bytes")
    for kind in sorted(counts):
        print(f"  {kind:<11} {counts[kind]}")


if __name__ == "__main__":
    main(sys.argv)
