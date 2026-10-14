---
name: import-questions
description: Convert questions and answers from any source (PDF, web page, Word or text file, spreadsheet, CSV, Aiken, Moodle GIFT or XML, QTI, notes, screenshots) into a MockiExams exam, validate it, and install it in the app. Use when asked to import, convert, add or create practice questions or an exam for MockiExams.
---

# Import questions into MockiExams

The output is an exam folder: `exam.json` plus any images under `images/`. Name it `<something>.mockiexam` if the user wants to open it in the Designer. Run everything from the repo root.

## 1. Check the source

- Use only material the user is entitled to use: their own notes, official sample questions, content they wrote or licensed, open question banks. If the source looks like leaked live exam content, say so and ask before going further.
- Don't try to read encrypted or proprietary exam-simulator files. Ask for the questions in an open form instead.
- Note what the source gives you for each question: prompt, options, correct answer, explanation, topic, images.

## 2. Use a converter when one fits

```sh
scripts/mockiexam convert source.csv  --title "Title" --id my-exam -o out/exam.json   # CSV
scripts/mockiexam convert source.txt  --from aiken --title "Title" -o out/exam.json   # Aiken
scripts/mockiexam convert source.gift --title "Title" -o out/exam.json                # Moodle GIFT
```

Skipped entries are printed on stderr with line numbers. Report them to the user; don't guess at them. CSV columns are listed in `FORMAT.md`. For any other source, write the JSON yourself.

## 3. Writing the JSON

Start from `scripts/mockiexam template exam`, or `template <kind>` for one question. `FORMAT.md` has every field. Map each source question to a kind:

| Source looks like | kind | answer goes in |
| --- | --- | --- |
| One correct option | `single` | `answer: "b"` |
| "Choose two/three", "select all that apply" | `multiple` | `answer: ["a","c"]` |
| True or false | `trueFalse` | `answer: false` |
| Several statements, Yes or No each | `yesNo` | `statements[].answer` |
| Put steps in order (maybe with unused options) | `ordering` | `answer: [ids in order]`; options left out are distractors |
| Drag items onto categories, match pairs | `matching` | `answer: {"itemId": "targetId"}`; items left out are distractors |
| Choose from a list in each blank or table cell, "hot area" | `dropdowns` | `rows[].answer` (one of `rows[].options`) |
| Type a command, number, or word | `fillIn` | `blanks[].accept`, plus optional `pattern` regex for abbreviations |
| Click the right spot on a picture | `hotspot` | `regions` (fractions 0 to 1 of the image) and `answer: [regionIds]` |
| Several questions sharing a scenario | any kind, plus `caseStudy` | scenario goes in `caseStudies[]` |
| CLI output or config shown with the question | any kind | `exhibits: [{ "title", "text" }]` |
| Diagram or screenshot shown with the question | any kind | `exhibits: [{ "title", "image": "images/x.png" }]` |

Rules:

- **Never invent a correct answer.** If the source doesn't give one, or gives one you think is wrong, leave the question out and list it for the user with the reason. Saying "the source's answer looks wrong" is fine; quietly fixing it is not.
- Keep the source's wording. Fix only obvious typos and broken formatting.
- Give every question a stable, readable `id` (`dhcp-order`, not `q17`) and a `topic` matching the exam's objectives or domains.
- Use `explanation` when the source has one. Don't write your own unless the user asks.
- Use `partialCredit: true` on multi-part kinds (matching, ordering, dropdowns, yesNo, multi-blank fillIn) when the user wants it to work like real exams. Use `points` for heavier questions.
- Images: copy them into `images/` and reference them by relative path. To find hotspot regions, measure the target's pixel box and divide by the image width and height. Read the image to check the box covers the right thing.
- Large banks: add `sections` to set the exam's order, and `timeLimitMinutes`, `passPercent` or `scale` when the user names them.

## 4. Validate until clean

```sh
scripts/mockiexam validate out/ --json
```

Fix every `error`. Each one has a JSON path such as `questions[12].answer`. Read the warnings too: unknown keys are usually typos, and duplicate prompts are usually repeated questions. Run it again until `valid` is true.

## 5. Install and report

```sh
scripts/mockiexam install out/
scripts/mockiexam stats out/
```

Tell the user how many questions were imported, the count by kind and topic, and every question you skipped and why. If they want to keep editing in the Designer, rename the folder to `name.mockiexam` and run `open name.mockiexam`.
