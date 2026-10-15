# MockiExams

A macOS and Windows app for practising IT certification exams, plus a designer for writing them. Exams are plain JSON in a folder with their images, so people, scripts and agents can all produce them.

## Layout

| Path | What it is |
| --- | --- |
| `Packages/MockiCore/` | Swift package with no UI: format, validator, scoring, session engine, library storage, converters, and the `mockiexam` CLI. All logic lives here. |
| `Sources/Simulator/` | The exam runner. The player and the designer preview both use it. |
| `Sources/Player/` | Library window, exam detail, results. |
| `Sources/Designer/` | Document windows that edit `.mockiexam` projects. |
| `Sources/Persistence/` | SwiftData attempt history and the observable library wrapper. |
| `samples/` | `networking-sim/` uses every question kind with images; `converters/` has Aiken, GIFT and CSV inputs. Tests read these. |
| `windows/` | The Windows app: `src/MockiCore` (C# port of the core), `src/mockiexam` (C# CLI), `src/MockiExams` (Avalonia UI), `tests/` (the core tests, ported) and `installer/` (Inno Setup). |
| `FORMAT.md` | The exam format for humans. |
| `Packages/MockiCore/Sources/MockiCore/Resources/mockiexam.schema.json` | JSON Schema for editors and agents. `mockiexam validate` is stricter than the schema and has the final say. |

## Build and test

```sh
swift test --package-path Packages/MockiCore            # core tests, fast, no app needed
xcodegen generate                                       # after adding or removing files
xcodebuild -scheme MockiExams -destination 'platform=macOS' -derivedDataPath build build
open build/Build/Products/Debug/MockiExams.app
```

Windows (built from macOS; needs the .NET SDK in `~/.dotnet` and CrossOver with Inno Setup in a bottle named `mockiexams-build`):

```sh
scripts/build-windows.sh        # tests, then x64 and arm64 installers in build/
scripts/parity-check.sh         # Swift and C# cores must give the same output
```

The app is sandboxed. Its library is `~/Library/Containers/app.mockiexams.MockiExams/Data/Library/Application Support/MockiExams/Library/`, one folder per exam.

## The CLI

`scripts/mockiexam` builds and runs the CLI from source.

```sh
scripts/mockiexam template exam             # a complete example exam
scripts/mockiexam template hotspot          # one question of a kind
scripts/mockiexam schema                    # the JSON Schema
scripts/mockiexam validate my-exam/ --json  # every error and warning with its JSON path; exit 1 on errors
scripts/mockiexam convert questions.csv --title "Net+ Practice" -o my-exam/exam.json
scripts/mockiexam stats my-exam/
scripts/mockiexam install my-exam/          # copies into the app library; the app picks it up live
scripts/mockiexam list
```

`convert` reads Aiken, Moodle GIFT and CSV. `scripts/anki-to-mockiexam.py` turns an Anki Basic deck into single-choice questions (distractors are other cards' answers). For anything else (PDF, web pages, Word files, Moodle XML, QTI, notes), write the JSON directly. The `import-questions` skill in `.claude/skills/` walks through it.

## Rules for changes

- Format changes go in `ExamDocument` (on-disk shape), `ExamFormat` (validation), the schema, `KnownKeys`, `FORMAT.md` and a test. `FormatTests.testSchemaKindsMatchModel` fails if the schema and the model drift.
- The core exists twice, in Swift and in C#. Format, validation, scoring, session and converter changes go in both, and `scripts/parity-check.sh` must pass.
- Scoring lives in `Model/Scoring.swift` and nowhere else.
- New question kinds need: a `QuestionKind` case, a `QuestionBody` case, decoding in `ExamFormat`, scoring, `describe`, a simulator view, a designer editor, a template, and a sample question.
- Verify UI changes by running the app. The unit tests don't cover views.
- The app reads only the open MockiExams format. It doesn't read encrypted or proprietary exam-simulator files, and it isn't meant to.
