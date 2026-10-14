# MockiExams exam format (version 1)

An exam is a folder with an `exam.json` and, optionally, images:

```
my-exam.mockiexam/        (or any folder name)
  exam.json
  images/
    topology.png
```

A single `exam.json` with no images also works. Run `scripts/mockiexam validate <folder>` to check an exam. It reports every problem with its JSON path, so all of them can be fixed in one pass. The JSON Schema is at `Packages/MockiCore/Sources/MockiCore/Resources/mockiexam.schema.json` (or run `scripts/mockiexam schema`). `samples/networking-sim/` uses every feature below.

## Exam

```json
{
  "formatVersion": 1,
  "id": "netplus-practice-1",
  "title": "Network+ Practice 1",
  "description": "Optional text shown in the library.",
  "passPercent": 70,
  "scale": { "min": 100, "max": 900, "pass": 720 },
  "timeLimitMinutes": 90,
  "allowBack": true,
  "partialCredit": false,
  "caseStudies": [],
  "sections": [],
  "questions": []
}
```

| Field | Required | Notes |
| --- | --- | --- |
| `id` | yes | Stable. Installing an exam with an existing id replaces it and keeps its history. |
| `title` | yes | |
| `formatVersion` | no | `1`. |
| `description` | no | |
| `passPercent` | no | 0 to 100, default 70. |
| `scale` | no | Shows a vendor-style scaled score. Pass/fail then uses `scale.pass`. The mapping from percentage is linear, which is an estimate; vendors don't publish their scaling. |
| `timeLimitMinutes` | no | Applies in exam simulation mode. |
| `allowBack` | no | Default `true`. `false` means candidates can't return to earlier questions in exam simulation, as on some vendors' exams. |
| `partialCredit` | no | Default for every question. See scoring. |
| `caseStudies` | no | Shared scenarios. See case studies. |
| `sections` | no | Builds the exam from the bank in a set order. See sections. |
| `questions` | yes | The question bank. |

## Questions

Every question has:

| Field | Required | Notes |
| --- | --- | --- |
| `id` | yes | Unique in the exam. History and "retry missed" use it. |
| `kind` | yes | One of the kinds below. |
| `prompt` | yes | Markdown: `**bold**`, `*italic*`, `` `code` ``, links, and fenced ```` ``` ```` code blocks. |
| `topic` | no | Exam objective or domain. Drives per-topic scores. Default `General`. |
| `explanation` | no | Shown after checking in practice mode, and in the review. |
| `exhibits` | no | `[{ "title": "...", "text": "..." }]` or `[{ "title": "...", "image": "images/x.png" }]`. Text is shown monospaced; images and PDFs open in a zoomable viewer. |
| `caseStudy` | no | Id of a `caseStudies` entry. |
| `points` | no | Default 1. |
| `partialCredit` | no | Overrides the exam default. |

### The kinds

**`single`**: one correct choice.
```json
{ "id": "dns-port", "kind": "single", "prompt": "Which port does DNS use?",
  "choices": [{ "id": "a", "text": "53" }, { "id": "b", "text": "67" }], "answer": "a" }
```

**`multiple`**: choose several. Full marks need exactly the right set.
```json
{ "kind": "multiple", "choices": [...], "answer": ["a", "c"] }
```

**`trueFalse`**
```json
{ "kind": "trueFalse", "prompt": "UDP retransmits lost segments.", "answer": false }
```

**`yesNo`**: a series of statements, each answered Yes or No.
```json
{ "kind": "yesNo", "statements": [
    { "id": "s1", "text": "Gi0/0 is up/up.", "answer": true },
    { "id": "s2", "text": "Gi0/1 has a cabling fault.", "answer": false } ] }
```

**`ordering`**: drag options into an answer area in order. Options not in `answer` are distractors and should be left out.
```json
{ "kind": "ordering", "choices": [{ "id": "d", "text": "Discover" }, { "id": "o", "text": "Offer" },
    { "id": "r", "text": "Request" }, { "id": "a", "text": "Ack" }, { "id": "x", "text": "Release" }],
  "answer": ["d", "o", "r", "a"] }
```

**`matching`**: drag each item onto a target. A target can take several items. Items missing from `answer` are distractors and must stay unplaced.
```json
{ "kind": "matching",
  "items":   [{ "id": "ssh", "text": "SSH" }, { "id": "rdp", "text": "RDP" }],
  "targets": [{ "id": "22", "text": "22" }, { "id": "3389", "text": "3389" }, { "id": "21", "text": "21" }],
  "answer":  { "ssh": "22", "rdp": "3389" } }
```

**`dropdowns`**: a list to choose from in each row, for "hot area" and fill-the-table items.
```json
{ "kind": "dropdowns", "rows": [
    { "id": "action", "label": "Action", "options": ["permit", "deny"], "answer": "permit" },
    { "id": "port", "label": "Port", "options": ["80", "443"], "answer": "443" } ] }
```

**`fillIn`**: typed answers, one or more fields. Spacing is normalized, and matching ignores case unless `caseSensitive` is set. `pattern` is a regular expression the whole answer must match, for accepting abbreviations.
```json
{ "kind": "fillIn", "blanks": [
    { "id": "cmd", "label": "Command", "accept": ["show ip route"], "pattern": "sh(o(w)?)? ip ro(u(te?)?)?" } ] }
```

**`hotspot`**: click the right area of an image. Regions are rectangles given as fractions (0 to 1) of the image's width and height, measured from the top left. With one correct region a click picks it; with several, clicks toggle.
```json
{ "kind": "hotspot", "image": "images/switch-panel.png",
  "regions": [
    { "id": "console", "label": "Console port", "x": 0.862, "y": 0.33, "width": 0.047, "height": 0.173 },
    { "id": "usb", "label": "USB port", "x": 0.92, "y": 0.35, "width": 0.043, "height": 0.127 } ],
  "answer": ["console"] }
```

## Case studies

A scenario shown beside each question that references it. Questions from one case study stay together, in file order, when an exam is shuffled.

```json
"caseStudies": [{ "id": "branch", "title": "Branch office",
  "scenario": "**Overview.** ...", "exhibits": [{ "title": "Topology", "image": "images/topology.png" }] }]
```

## Sections

When present, sections decide which bank questions are asked and in what order. A question can be in at most one section. Bank questions outside every section are kept but not asked, and the validator warns about them. `shuffle: false` keeps a section in order even when the run shuffles.

```json
"sections": [
  { "id": "s1", "title": "Part 1", "questions": ["dns-port", "dhcp-order"] },
  { "id": "s2", "title": "Labs", "questions": ["acl-hotarea"], "shuffle": false } ]
```

## Scoring

A question earns a share of its `points` from 0 to 1. Without partial credit it's all or nothing. With partial credit:

- `multiple` and `hotspot`: (right picks minus wrong picks) divided by the number of correct ones, never below 0.
- `ordering`: positions that match, divided by the longer of the two sequences.
- `matching`: items placed correctly (including distractors left out) divided by the number of items.
- `dropdowns`, `yesNo`, `fillIn`: correct rows or fields divided by the total.

The exam percentage is points earned divided by points possible.

## Converting other formats

`scripts/mockiexam convert` reads:

- **Aiken**: a question, `A.`/`A)` options, then `ANSWER: B` (or `ANSWER: A, C`).
- **Moodle GIFT**: single and multiple choice (including `~%50%` weights), true/false, short answer, matching, exact numeric answers, `$CATEGORY:` as the topic, and `####` as the explanation.
- **CSV**: a header row with `question` (or `prompt`) and `answer`, options in `A` to `J` (or `option a`, `option1` and so on), and optionally `id`, `topic`, `explanation`, `kind`. Answers are letters (`B`, `A,C`) or option text. With no options, `true`/`false` makes a true/false question and anything else becomes a fill-in (separate alternatives with `|`). Comma, semicolon and tab delimiters are detected.

Examples are in `samples/converters/`.
