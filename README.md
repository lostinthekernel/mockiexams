# MockiExams

A macOS app for practising IT certification exams, with a built-in designer for writing your own.

- **Player:** practice mode with instant feedback, or a timed exam simulation in a modern or classic testing-center layout, with a review screen, score report and per-topic results.
- **Designer:** a question bank table with filters and bulk edits, a step-by-step question builder, hotspot drawing on images, case studies, sections, and blueprint coverage.
- **Nine question kinds:** single and multiple choice, true/false, yes/no, dropdowns, fill in the blank, matching, ordering, and hotspots.
- **Open format:** an exam is a folder with `exam.json` and its images. See [FORMAT.md](FORMAT.md).
- **Command line:** `mockiexam` validates, converts (Aiken, Moodle GIFT, CSV), and installs exams.

MockiExams reads only its own open format. It does not open encrypted or proprietary exam-simulator files.

## Requirements

- macOS 15 or later
- Xcode 16 or later
- [XcodeGen](https://github.com/yonaskolb/XcodeGen) (`brew install xcodegen`)

## Build

```sh
xcodegen generate
xcodebuild -scheme MockiExams -configuration Release -destination 'platform=macOS' -derivedDataPath build build
open build/Build/Products/Release/MockiExams.app
```

Or run `xcodegen generate` and open `MockiExams.xcodeproj` in Xcode.

Run the core tests with:

```sh
swift test --package-path Packages/MockiCore
```

## Making exams

```sh
scripts/mockiexam template exam > my-exam/exam.json   # a complete example to edit
scripts/mockiexam convert questions.csv --title "My Practice Exam" -o my-exam/exam.json
scripts/mockiexam validate my-exam/
scripts/mockiexam install my-exam/                    # adds it to the app's library
```

You can also build an exam in the app with File › New, then use Install to Library. `samples/` has a small exam that uses every question kind.

Only import questions you have the right to use, such as your own notes, official sample questions, or openly licensed question banks.

## Project layout

`AGENTS.md` describes the code layout and the rules for changing the format. Coding agents read it, and it works as a contributor guide too.

## License

MIT. See [LICENSE](LICENSE).
