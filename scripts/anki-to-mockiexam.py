"""Turn Anki Basic (Front/Back) cards into a MockiExams question bank.

Each card becomes a single-choice question: the card's own short answer is correct, and the
distractors are other cards' answers from the same topic, so nothing is invented.
Usage: python3 -I scripts/anki-to-mockiexam.py <collection.anki2 copy> "<deck name>" <out exam.json>
Anki keeps the collection open, so copy collection.anki2 (and its -wal file) somewhere first.
"""
import html
import json
import random
import re
import sqlite3
import sys

coll, deck_name, out = sys.argv[1:4]
db = sqlite3.connect(f"file:{coll}?mode=ro", uri=True)
db.create_collation("unicase", lambda a, b: (a.lower() > b.lower()) - (a.lower() < b.lower()))
deck_ids = [i for i, n in db.execute("select id, name from decks") if n.replace("\x1f", "::") == deck_name]
rows = db.execute(
    f"select distinct n.id, n.flds, n.tags from notes n join cards c on c.nid = n.id "
    f"where c.did in ({','.join('?' * len(deck_ids))}) order by n.id", deck_ids).fetchall()


def to_markdown(s):
    s = re.sub(r"<br\s*/?>", "\n", s, flags=re.I)
    s = re.sub(r"</?b>", "**", s, flags=re.I)
    s = re.sub(r"</?i>", "*", s, flags=re.I)
    s = re.sub(r"<[^>]+>", "", s)
    return html.unescape(s).strip()


def plain(s):
    return re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]+>", " ", s))).strip()


def short_answer(back):
    m = re.match(r"\s*<b>(.*?)</b>", back, flags=re.I | re.S)
    text = plain(m.group(1)) if m else plain(re.split(r"<br|\.\s", back, maxsplit=1)[0])
    return text.rstrip(".:").strip()


def overlaps(a, b):
    # One answer restating the other ("Resource-based" vs "A resource-based policy") makes two options correct.
    a, b = a.lower(), b.lower()
    return bool(re.search(rf"\b{re.escape(a)}\b", b) or re.search(rf"\b{re.escape(b)}\b", a))


def topic_name(tag):
    # Short tags like iam, ec2, s3 are acronyms.
    return tag.upper() if len(tag) <= 3 or any(ch.isdigit() for ch in tag) else tag.replace("-", " ").capitalize()


cards, skipped = [], []
for nid, flds, tags in rows:
    front, back = (flds.split("\x1f") + [""])[:2]
    tag_list = [t for t in tags.split() if t.lower() != "aws"]
    answer = short_answer(back)
    if not plain(front) or not answer:
        skipped.append((nid, "empty front or no answer on the back"))
        continue
    cards.append({"nid": nid, "prompt": to_markdown(front), "answer": answer, "back": to_markdown(back),
                  "topic": topic_name(tag_list[0]) if tag_list else "General"})

rng = random.Random(7)
questions = []
for c in cards:
    same = [o["answer"] for o in cards if o["topic"] == c["topic"] and o["nid"] != c["nid"]]
    other = [o["answer"] for o in cards if o["topic"] != c["topic"]]
    pool = list(dict.fromkeys(a for a in same + other if not overlaps(a, c["answer"])))
    picks = [a for a in pool[: len(same)] if a]
    rng.shuffle(picks)
    rest = [a for a in pool if a not in picks]
    rng.shuffle(rest)
    distractors = (picks + rest)[:3]
    if len(distractors) < 3:
        skipped.append((c["nid"], "not enough other answers for distractors"))
        continue
    options = [c["answer"]] + distractors
    rng.shuffle(options)
    letters = "abcd"
    questions.append({
        "id": f"anki-{c['nid']}",
        "kind": "single",
        "topic": c["topic"],
        "prompt": c["prompt"],
        "choices": [{"id": letters[i], "text": t} for i, t in enumerate(options)],
        "answer": letters[options.index(c["answer"])],
        "explanation": c["back"],
    })

slug = re.sub(r"[^a-z0-9]+", "-", deck_name.lower()).strip("-")
json.dump({"formatVersion": 1, "id": f"anki-{slug}", "title": f"Anki: {deck_name}",
           "description": "Imported from Anki flashcards. Wrong options are answers from other cards.",
           "passPercent": 72, "timeLimitMinutes": 60, "questions": questions},
          open(out, "w"), indent=2, ensure_ascii=False)
print(f"{len(rows)} notes, {len(questions)} questions written, {len(skipped)} skipped")
for nid, why in skipped:
    print(f"  skipped {nid}: {why}")
