import Foundation
import MockiCore

let usage = """
mockiexam — create, check and install MockiExams exams.

USAGE
  mockiexam validate <exam.json | exam-folder>... [--json]
      Check exams. Prints every error and warning with its JSON path.
      Exit status 0 = valid (warnings allowed), 1 = errors.

  mockiexam convert <input> [--from aiken|gift|csv] [--id ID] [--title TITLE] [--topic TOPIC] [-o OUT.json]
      Convert a question source to exam JSON (stdout unless -o). Lists skipped entries
      on stderr, then validates the result.

  mockiexam install <exam.json | exam-folder>... [--library DIR]
      Validate and copy into the app library (with images). The app picks it up on next activation.

  mockiexam list [--library DIR]
  mockiexam stats <exam.json | exam-folder>
  mockiexam schema                   Print the JSON Schema.
  mockiexam template [kind|exam]     Print an example question of a kind, or a whole exam.
      kinds: \(QuestionKind.allCases.map(\.rawValue).joined(separator: ", "))
"""

struct Args {
    var positional: [String] = []
    var options: [String: String] = [:]
    var flags: Set<String> = []

    init(_ raw: [String]) {
        var it = raw.makeIterator()
        while let a = it.next() {
            if a == "--json" || a == "-h" || a == "--help" { flags.insert(a) }
            else if a.hasPrefix("-") { options[a] = it.next() ?? "" }
            else { positional.append(a) }
        }
    }

    func option(_ names: String...) -> String? { names.lazy.compactMap { options[$0] }.first }
}

func fail(_ message: String) -> Never {
    FileHandle.standardError.write(Data((message + "\n").utf8))
    exit(1)
}

func err(_ message: String) { FileHandle.standardError.write(Data((message + "\n").utf8)) }

/// The exam JSON file and the folder images resolve against.
func locate(_ path: String) -> (file: URL, base: URL) {
    let url = URL(filePath: path)
    var isDir: ObjCBool = false
    guard FileManager.default.fileExists(atPath: url.path, isDirectory: &isDir) else { fail("No such file: \(path)") }
    let file = isDir.boolValue ? url.appending(path: "exam.json") : url
    return (file, file.deletingLastPathComponent())
}

func library(_ args: Args) -> LibraryStore {
    LibraryStore(directory: args.option("--library").map { URL(filePath: $0, directoryHint: .isDirectory) })
}

let args = Args(Array(CommandLine.arguments.dropFirst()))
guard let command = args.positional.first, !args.flags.contains("-h"), !args.flags.contains("--help") else {
    print(usage)
    exit(args.positional.isEmpty && args.flags.isEmpty ? 1 : 0)
}
let inputs = Array(args.positional.dropFirst())

switch command {
case "validate":
    guard !inputs.isEmpty else { fail("validate needs at least one path.") }
    var anyErrors = false
    var report: [[String: Any]] = []
    for input in inputs {
        let (file, base) = locate(input)
        let data: Data
        do { data = try Data(contentsOf: file) } catch { fail("Can't read \(file.path): \(error.localizedDescription)") }
        let (exam, issues) = ExamFormat.validate(data, baseURL: base)
        anyErrors = anyErrors || exam == nil
        if args.flags.contains("--json") {
            report.append([
                "file": file.path, "valid": exam != nil, "questions": exam?.questions.count ?? 0,
                "issues": issues.map { ["severity": $0.severity.rawValue, "path": $0.path, "message": $0.message] },
            ])
        } else {
            for i in issues { print("\(file.lastPathComponent): \(i)") }
            let errors = issues.count { $0.severity == .error }, warnings = issues.count - errors
            print(exam.map { "\(file.path): OK — \($0.questions.count) questions, \(warnings) warning(s)" }
                ?? "\(file.path): INVALID — \(errors) error(s), \(warnings) warning(s)")
        }
    }
    if args.flags.contains("--json") {
        let data = try JSONSerialization.data(withJSONObject: report, options: [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes])
        print(String(decoding: data, as: UTF8.self))
    }
    exit(anyErrors ? 1 : 0)

case "convert":
    guard let input = inputs.first else { fail("convert needs an input file.") }
    let url = URL(filePath: input)
    guard let text = try? String(contentsOf: url, encoding: .utf8) else { fail("Can't read \(input) as UTF-8 text.") }
    let format: SourceFormat
    if let name = args.option("--from") {
        guard let f = SourceFormat(rawValue: name) else { fail("Unknown --from \(name). Use: \(SourceFormat.allCases.map(\.rawValue).joined(separator: ", ")).") }
        format = f
    } else if let f = SourceFormat.detect(fileName: url.lastPathComponent, text: text) {
        format = f
        err("Detected format: \(f.rawValue)")
    } else {
        fail("Can't tell the format of \(input); pass --from \(SourceFormat.allCases.map(\.rawValue).joined(separator: "|")).")
    }
    let base = url.deletingPathExtension().lastPathComponent
    let title = args.option("--title") ?? base
    let id = args.option("--id") ?? Converter.slug(title)
    let result = Converter.convert(text, format: format, id: id, title: title, topic: args.option("--topic"))
    for s in result.skipped { err("\(input): \(s)") }
    let json = try ExamFormat.encode(result.document)
    if let out = args.option("-o", "--output") {
        try json.write(to: URL(filePath: out))
        err("Wrote \(result.document.questions?.count ?? 0) questions to \(out) (\(result.skipped.count) skipped).")
    } else {
        print(String(decoding: json, as: UTF8.self))
    }
    let (exam, issues) = ExamFormat.validate(json)
    for i in issues { err("validate: \(i)") }
    exit(exam == nil ? 1 : 0)

case "install":
    guard !inputs.isEmpty else { fail("install needs at least one path.") }
    let store = library(args)
    var failed = false
    for input in inputs {
        do {
            let exam = try store.install(URL(filePath: input))
            print("Installed \"\(exam.title)\" (\(exam.questions.count) questions, \(exam.assetPaths.count) images) → \(exam.assetBase?.path ?? "")")
        } catch {
            failed = true
            err("\(input): not installed\n\(error.localizedDescription)")
        }
    }
    exit(failed ? 1 : 0)

case "list":
    let contents = library(args).load()
    for e in contents.exams { print("\(e.id)\t\(e.questions.count) q\t\(e.title)") }
    for b in contents.broken { print("BROKEN\t\(b)") }
    if contents.exams.isEmpty && contents.broken.isEmpty { print("Library is empty: \(library(args).directory.path)") }

case "stats":
    guard let input = inputs.first else { fail("stats needs a path.") }
    let (file, base) = locate(input)
    do {
        let exam = try ExamFormat.decode(Data(contentsOf: file), baseURL: base)
        print("\(exam.title) — \(exam.questions.count) questions, \(Int(exam.totalPoints)) points")
        print("\nBy kind:")
        for kind in QuestionKind.allCases {
            let n = exam.questions.count { $0.kind == kind }
            if n > 0 { print("  \(kind.rawValue.padding(toLength: 10, withPad: " ", startingAt: 0)) \(n)") }
        }
        print("\nBy topic:")
        for topic in exam.topics { print("  \(topic): \(exam.questions.count { $0.topic == topic })") }
        let explained = exam.questions.count { $0.explanation != nil }
        print("\nWith explanation: \(explained)/\(exam.questions.count)")
        print("Case studies: \(exam.caseStudies.count), images: \(exam.assetPaths.count)")
    } catch {
        fail(error.localizedDescription)
    }

case "schema":
    print(Templates.schema)

case "template":
    let which = inputs.first ?? "exam"
    if which == "exam" { print(Templates.exam) }
    else if let kind = QuestionKind(rawValue: which) { print(Templates.question(kind)) }
    else { fail("Unknown template \(which). Use exam or one of: \(QuestionKind.allCases.map(\.rawValue).joined(separator: ", ")).") }

default:
    fail("Unknown command \(command).\n\n\(usage)")
}
