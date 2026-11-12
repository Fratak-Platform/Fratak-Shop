#!/usr/bin/env python3
"""
دور ۲۸ – سازندهٔ دادهٔ اولیهٔ «دانش دستیار».
ورودی: فایل‌های دست‌نوشتهٔ همین پوشه (glossary/guides/smalltalk/synonyms.tsv).
خروجی: src/Fratak-Shop.Core/Services/Assistant/Knowledge/Data/assistant-knowledge.tsv.gz
هر خط خروجی: نوع ⇥ واژه ⇥ شکل‌های دیگر ⇥ جواب ⇥ موضوع ⇥ وزن
اجرا: python3 tools/assistant-knowledge/build_seed.py
"""
import gzip, io, os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', '..', 'src', 'Fratak-Shop.Core', 'Services', 'Assistant', 'Knowledge', 'Data', 'assistant-knowledge.tsv.gz')

def rows(name):
    with open(os.path.join(HERE, name), encoding='utf-8') as f:
        for line in f:
            line = line.rstrip('\n')
            if not line or line.startswith('#'):
                continue
            yield line.split('\t')

def clean(text):
    return ' '.join(text.replace('\t', ' ').split())

out = []
seen = set()
def add(kind, term, aliases, answer, topic, weight):
    term = clean(term)
    key = (kind, term.lower())
    assert key not in seen, key
    seen.add(key)
    out.append('\t'.join([kind, term, clean(aliases), clean(answer), clean(topic), str(weight)]))

for p in rows('glossary.tsv'):
    add('glossary', p[0], p[1], p[2], p[3], 1)
for p in rows('guides.tsv'):
    add('guide', p[0], p[1], p[2], p[3], 1)
for p in rows('smalltalk.tsv'):
    add('smalltalk', p[0], p[1], p[2], p[3], 1)
for p in rows('synonyms.tsv'):
    add('synonym', p[0], p[1], p[0], 'جست‌وجو', 1)

os.makedirs(os.path.dirname(OUT), exist_ok=True)
data = ('# دانش دستیار – ساختهٔ tools/assistant-knowledge/build_seed.py\n' + '\n'.join(out) + '\n').encode('utf-8')
with open(OUT, 'wb') as raw:
    # mtime=0 ← خروجی یکسان در هر اجرا (diff بی‌سروصدا).
    with gzip.GzipFile(fileobj=raw, mode='wb', mtime=0, filename='') as gz:
        gz.write(data)
print(len(out), 'rows ->', os.path.relpath(OUT))
