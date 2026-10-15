using System.Text;
using System.Text.Json.Nodes;
using MockiCore;

Console.OutputEncoding = Encoding.UTF8;

var usage = $"""
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
      kinds: {string.Join(", ", QuestionKinds.All.Select(k => k.Raw()))}
""";

var positional = new List<string>();
var options = new Dictionary<string, string>();
var flags = new HashSet<string>();
for (var i = 0; i < args.Length; i++)
{
    var a = args[i];
    if (a is "--json" or "-h" or "--help") flags.Add(a);
    else if (a.StartsWith('-')) options[a] = i + 1 < args.Length ? args[++i] : "";
    else positional.Add(a);
}
string? Option(params string[] names) => names.Select(n => options.GetValueOrDefault(n)).FirstOrDefault(v => v is not null);

int Fail(string message) { Console.Error.WriteLine(message); return 1; }

(string File, string Base)? Locate(string path)
{
    if (Directory.Exists(path)) return (Path.Combine(path, "exam.json"), Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar));
    if (File.Exists(path)) return (path, Path.GetDirectoryName(Path.GetFullPath(path))!);
    return null;
}

LibraryStore Library() => new(Option("--library"));

if (positional.Count == 0 || flags.Contains("-h") || flags.Contains("--help"))
{
    Console.WriteLine(usage);
    return positional.Count == 0 && flags.Count == 0 ? 1 : 0;
}
var command = positional[0];
var inputs = positional.Skip(1).ToList();

switch (command)
{
    case "validate":
    {
        if (inputs.Count == 0) return Fail("validate needs at least one path.");
        var anyErrors = false;
        var report = new JsonArray();
        foreach (var input in inputs)
        {
            if (Locate(input) is not var (file, baseDir)) return Fail($"No such file: {input}");
            byte[] data;
            try { data = File.ReadAllBytes(file); } catch (Exception e) { return Fail($"Can't read {file}: {e.Message}"); }
            var (exam, issues) = ExamFormat.Validate(data, baseDir);
            anyErrors |= exam is null;
            if (flags.Contains("--json"))
            {
                report.Add(new JsonObject
                {
                    ["file"] = Path.GetFullPath(file), ["valid"] = exam is not null, ["questions"] = exam?.Questions.Count ?? 0,
                    ["issues"] = new JsonArray(issues.Select(i => (JsonNode)new JsonObject
                    {
                        ["severity"] = i.Severity == Severity.Error ? "error" : "warning", ["path"] = i.Path, ["message"] = i.Message,
                    }).ToArray()),
                });
            }
            else
            {
                foreach (var i in issues) Console.WriteLine($"{Path.GetFileName(file)}: {i}");
                var errors = issues.Count(i => i.Severity == Severity.Error);
                var warnings = issues.Count - errors;
                Console.WriteLine(exam is not null
                    ? $"{Path.GetFullPath(file)}: OK — {exam.Questions.Count} questions, {warnings} warning(s)"
                    : $"{Path.GetFullPath(file)}: INVALID — {errors} error(s), {warnings} warning(s)");
            }
        }
        if (flags.Contains("--json")) Console.WriteLine(SwiftJson.Write(report));
        return anyErrors ? 1 : 0;
    }

    case "convert":
    {
        if (inputs.Count == 0) return Fail("convert needs an input file.");
        var input = inputs[0];
        string text;
        try { text = File.ReadAllText(input, new UTF8Encoding(false, true)); } catch (Exception) { return Fail($"Can't read {input} as UTF-8 text."); }
        SourceFormat format;
        if (Option("--from") is { } name)
        {
            if (SourceFormats.FromRaw(name) is not { } f) return Fail($"Unknown --from {name}. Use: aiken, gift, csv.");
            format = f;
        }
        else if (SourceFormats.Detect(Path.GetFileName(input), text) is { } f)
        {
            format = f;
            Console.Error.WriteLine($"Detected format: {f.Raw()}");
        }
        else return Fail($"Can't tell the format of {input}; pass --from aiken|gift|csv.");
        var title = Option("--title") ?? Path.GetFileNameWithoutExtension(input);
        var id = Option("--id") ?? Converter.Slug(title);
        var result = Converter.Convert(text, format, id, title, Option("--topic"));
        foreach (var s in result.Skipped) Console.Error.WriteLine($"{input}: {s}");
        var json = ExamFormat.Encode(result.Document);
        if (Option("-o", "--output") is { } output)
        {
            File.WriteAllBytes(output, json);
            Console.Error.WriteLine($"Wrote {result.Document.Questions?.Count ?? 0} questions to {output} ({result.Skipped.Count} skipped).");
        }
        else Console.WriteLine(Encoding.UTF8.GetString(json));
        var (exam, issues) = ExamFormat.Validate(json);
        foreach (var i in issues) Console.Error.WriteLine($"validate: {i}");
        return exam is null ? 1 : 0;
    }

    case "install":
    {
        if (inputs.Count == 0) return Fail("install needs at least one path.");
        var store = Library();
        var failed = false;
        foreach (var input in inputs)
        {
            try
            {
                var exam = store.Install(input);
                Console.WriteLine($"Installed \"{exam.Title}\" ({exam.Questions.Count} questions, {exam.AssetPaths.Count} images) → {exam.AssetBase}");
            }
            catch (Exception e) when (e is ExamLoadException or IOException)
            {
                failed = true;
                Console.Error.WriteLine($"{input}: not installed\n{e.Message}");
            }
        }
        return failed ? 1 : 0;
    }

    case "list":
    {
        var store = Library();
        var contents = store.Load();
        foreach (var e in contents.Exams) Console.WriteLine($"{e.Id}\t{e.Questions.Count} q\t{e.Title}");
        foreach (var b in contents.Broken) Console.WriteLine($"BROKEN\t{b}");
        if (contents.Exams.Count == 0 && contents.Broken.Count == 0) Console.WriteLine($"Library is empty: {store.Directory}");
        return 0;
    }

    case "stats":
    {
        if (inputs.Count == 0) return Fail("stats needs a path.");
        if (Locate(inputs[0]) is not var (file, baseDir)) return Fail($"No such file: {inputs[0]}");
        try
        {
            var exam = ExamFormat.Decode(File.ReadAllBytes(file), baseDir);
            Console.WriteLine($"{exam.Title} — {exam.Questions.Count} questions, {(int)exam.TotalPoints} points");
            Console.WriteLine("\nBy kind:");
            foreach (var kind in QuestionKinds.All)
            {
                var n = exam.Questions.Count(q => q.Kind == kind);
                if (n > 0) Console.WriteLine($"  {kind.Raw(),-10} {n}");
            }
            Console.WriteLine("\nBy topic:");
            foreach (var topic in exam.Topics) Console.WriteLine($"  {topic}: {exam.Questions.Count(q => q.Topic == topic)}");
            Console.WriteLine($"\nWith explanation: {exam.Questions.Count(q => q.Explanation is not null)}/{exam.Questions.Count}");
            Console.WriteLine($"Case studies: {exam.CaseStudies.Count}, images: {exam.AssetPaths.Count}");
            return 0;
        }
        catch (ExamLoadException e) { return Fail(e.Message); }
    }

    case "schema":
        Console.WriteLine(Templates.Schema);
        return 0;

    case "template":
    {
        var which = inputs.FirstOrDefault() ?? "exam";
        if (which == "exam") Console.WriteLine(Templates.Exam);
        else if (QuestionKinds.FromRaw(which) is { } kind) Console.WriteLine(Templates.Question(kind));
        else return Fail($"Unknown template {which}. Use exam or one of: {string.Join(", ", QuestionKinds.All.Select(k => k.Raw()))}.");
        return 0;
    }

    default:
        return Fail($"Unknown command {command}.\n\n{usage}");
}
