using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MockiCore;

public enum Severity { Error, Warning }

public sealed record Issue(Severity Severity, string Path, string Message)
{
    public override string ToString() => $"{(Severity == Severity.Error ? "error" : "warning")}: {Path}: {Message}";
}

public sealed class ExamLoadException(IReadOnlyList<Issue> issues) : Exception(Describe(issues))
{
    public IReadOnlyList<Issue> Issues { get; } = issues;
    public IEnumerable<Issue> Errors => Issues.Where(i => i.Severity == Severity.Error);

    static string Describe(IReadOnlyList<Issue> issues)
    {
        var errors = issues.Where(i => i.Severity == Severity.Error).ToList();
        var shown = errors.Take(6).Select(e => e.ToString()).ToList();
        if (errors.Count > 6) shown.Add($"…and {errors.Count - 6} more");
        return string.Join("\n", shown);
    }
}

public static class ExamFormat
{
    public const int CurrentVersion = 1;

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Parses and checks an exam. Pass baseDir (the folder holding the JSON) to check that
    /// referenced images exist; without it, asset paths are only checked for shape.</summary>
    public static (Exam? Exam, List<Issue> Issues) Validate(byte[] data, string? baseDir = null)
    {
        var v = new Validator(baseDir);
        var exam = v.Run(data);
        return (v.HasErrors ? null : exam, v.Issues);
    }

    public static (Exam? Exam, List<Issue> Issues) Validate(string json, string? baseDir = null) =>
        Validate(Encoding.UTF8.GetBytes(json), baseDir);

    public static Exam Decode(byte[] data, string? baseDir = null)
    {
        var (exam, issues) = Validate(data, baseDir);
        if (exam is null) throw new ExamLoadException(issues);
        exam.AssetBase = baseDir;
        return exam;
    }

    public static Exam Decode(string json, string? baseDir = null) => Decode(Encoding.UTF8.GetBytes(json), baseDir);

    public static ExamDocument? ReadDocument(byte[] data) => JsonSerializer.Deserialize<ExamDocument>(data, Options);

    /// <summary>Pretty-printed with sorted keys, in the same layout the macOS app writes.</summary>
    public static byte[] Encode(ExamDocument document) => Encoding.UTF8.GetBytes(SwiftJson.Write(JsonSerializer.SerializeToNode(document, Options)));
}

/// <summary>Writes JSON the way Foundation's JSONEncoder does with .prettyPrinted and .sortedKeys,
/// so files saved on Windows and macOS diff cleanly.</summary>
public static class SwiftJson
{
    public static string Write(JsonNode? node)
    {
        var sb = new StringBuilder();
        Write(node, sb, 0);
        return sb.ToString();
    }

    static void Write(JsonNode? node, StringBuilder sb, int depth)
    {
        var pad = new string(' ', depth * 2);
        var inner = new string(' ', (depth + 1) * 2);
        switch (node)
        {
            case null:
                sb.Append("null");
                break;
            case JsonObject obj:
                sb.Append("{\n");
                var keys = obj.Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
                for (var i = 0; i < keys.Count; i++)
                {
                    sb.Append(inner).Append(Quote(keys[i])).Append(" : ");
                    Write(obj[keys[i]], sb, depth + 1);
                    if (i < keys.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                if (keys.Count == 0) sb.Append('\n');
                sb.Append(pad).Append('}');
                break;
            case JsonArray arr:
                sb.Append("[\n");
                for (var i = 0; i < arr.Count; i++)
                {
                    sb.Append(inner);
                    Write(arr[i], sb, depth + 1);
                    if (i < arr.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                if (arr.Count == 0) sb.Append('\n');
                sb.Append(pad).Append(']');
                break;
            case JsonValue value:
                sb.Append(Scalar(value));
                break;
        }
    }

    static string Scalar(JsonValue value)
    {
        if (value.TryGetValue<string>(out var s)) return Quote(s);
        if (value.TryGetValue<bool>(out var b)) return b ? "true" : "false";
        if (value.TryGetValue<int>(out var i)) return i.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<long>(out var l)) return l.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<double>(out var d)) return Number(d);
        return value.ToJsonString(ExamFormat.Options);
    }

    /// <summary>Whole numbers without a decimal point, otherwise the shortest round-trip form.</summary>
    public static string Number(double d)
    {
        if (d == Math.Floor(d) && Math.Abs(d) < 1e15) return ((long)d).ToString(CultureInfo.InvariantCulture);
        return d.ToString("R", CultureInfo.InvariantCulture).Replace("E", "e");
    }

    static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}

static class Text
{
    /// <summary>Trims spaces and tabs (not newlines), like Swift's .whitespaces.</summary>
    public static string TrimSpaces(this string s) => s.Trim(s.Where(c => char.IsWhiteSpace(c) && c != '\n' && c != '\r').Distinct().ToArray());

    public static string? NonEmpty(this string? s) => string.IsNullOrEmpty(s) ? null : s;

    /// <summary>Swift's Double description: 140 → "140.0", 0.5 → "0.5".</summary>
    public static string SwiftDouble(double d) =>
        d == Math.Floor(d) && Math.Abs(d) < 1e16 ? d.ToString("0.0", CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Swift's Double.formatted(): the user's locale, grouping separators, no trailing zeros.</summary>
    public static string Formatted(double d) => d.ToString("#,##0.###", CultureInfo.CurrentCulture);
}

sealed class Validator(string? baseDir)
{
    public List<Issue> Issues { get; } = [];
    public bool HasErrors => Issues.Any(i => i.Severity == Severity.Error);

    void Error(string path, string message) => Issues.Add(new Issue(Severity.Error, path, message));
    void Warn(string path, string message) => Issues.Add(new Issue(Severity.Warning, path, message));

    public Exam? Run(byte[] data)
    {
        JsonDocument raw;
        try { raw = JsonDocument.Parse(data); }
        catch (JsonException e)
        {
            Error("$", $"Not valid JSON: {e.Message}");
            return null;
        }
        ExamDocument? doc;
        try { doc = JsonSerializer.Deserialize<ExamDocument>(data, ExamFormat.Options); }
        catch (JsonException e)
        {
            Error(SwiftPath(e.Path), $"Wrong type. {e.Message}");
            return null;
        }
        if (doc is null) { Error("$", "Null value."); return null; }
        if (doc.Scale is not null)
        {
            // Foundation requires all three scale keys; report the first one missing, as it does.
            if (raw.RootElement.TryGetProperty("scale", out var scale) && scale.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "min", "max", "pass" })
                    if (!scale.TryGetProperty(key, out _)) { Error($"scale.{key}", "Missing."); return null; }
            }
        }
        CheckKeys(raw.RootElement);
        return Build(doc);
    }

    static string SwiftPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path == "$") return "$";
        return path.StartsWith("$.") ? path[2..] : path.TrimStart('$');
    }

    // MARK: Exam

    Exam? Build(ExamDocument doc)
    {
        if (doc.FormatVersion is { } v && v > ExamFormat.CurrentVersion)
            Error("formatVersion", $"File uses format {v}; this build reads up to {ExamFormat.CurrentVersion}.");
        var id = Required(doc.Id, "id");
        var title = Required(doc.Title, "title");
        var pass = doc.PassPercent ?? 70;
        if (pass is < 0 or > 100) Error("passPercent", $"Must be 0–100, got {pass}.");
        if (doc.Scale is { } s && !(s.Min < s.Max && s.Pass >= s.Min && s.Pass <= s.Max))
            Error("scale", "Needs min < max and min ≤ pass ≤ max.");
        if (doc.TimeLimitMinutes is { } t && t <= 0) Error("timeLimitMinutes", "Must be positive.");
        if (doc.TopicWeights is { } weights)
        {
            foreach (var (topic, w) in weights.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                if (w is < 0 or > 100) Error($"topicWeights.{topic}", $"Must be 0–100, got {Text.SwiftDouble(w)}.");
            var sum = weights.Values.Sum();
            if (weights.Count > 0 && Math.Abs(sum - 100) > 0.5) Warn("topicWeights", $"Weights add up to {Text.Formatted(sum)}, not 100.");
            var used = (doc.Questions ?? []).Select(q => q.Topic?.TrimSpaces().NonEmpty() ?? "General").ToHashSet();
            foreach (var topic in weights.Keys.OrderBy(k => k, StringComparer.Ordinal))
                if (!used.Contains(topic)) Warn($"topicWeights.{topic}", "No question has this topic.");
        }

        var caseIds = new HashSet<string>();
        var caseStudies = new List<CaseStudy>();
        foreach (var (c, i) in (doc.CaseStudies ?? []).Select((c, i) => (c, i)))
        {
            var p = $"caseStudies[{i}]";
            if (Required(c.Id, $"{p}.id") is not { } cid) continue;
            if (!caseIds.Add(cid)) Error($"{p}.id", $"Duplicate case study id \"{cid}\".");
            caseStudies.Add(new CaseStudy(cid, c.Title ?? cid, Required(c.Scenario, $"{p}.scenario") ?? "", Exhibits(c.Exhibits, $"{p}.exhibits")));
        }

        if (doc.Questions is not { Count: > 0 } docs)
        {
            Error("questions", "Exam has no questions.");
            return null;
        }
        var questionIds = new HashSet<string>();
        var prompts = new Dictionary<string, string>();
        var questions = new List<Question>();
        for (var i = 0; i < docs.Count; i++)
        {
            var q = docs[i];
            var p = $"questions[{i}]";
            if (q.Id is { } qid && !questionIds.Add(qid)) Error($"{p}.id", $"Duplicate question id \"{qid}\".");
            if (q.CaseStudy is { } cs && !caseIds.Contains(cs)) Error($"{p}.caseStudy", $"No case study with id \"{cs}\".");
            if (q.Prompt?.Trim() is { Length: > 0 } text)
            {
                if (prompts.TryGetValue(text, out var first)) Warn($"{p}.prompt", $"Same prompt as {first}; likely a duplicate question.");
                else prompts[text] = p;
            }
            if (Question(q, p, doc.PartialCredit ?? false) is { } built) questions.Add(built);
        }

        var sections = Sections(doc.Sections, docs.Select(q => q.Id).OfType<string>().ToList());

        if (id is null || title is null || HasErrors) return null;
        return new Exam
        {
            Id = id, Title = title, Summary = doc.Description, PassPercent = pass,
            Scale = doc.Scale is { } sc ? new ScoreScale(sc.Min, sc.Max, sc.Pass) : null,
            TimeLimit = doc.TimeLimitMinutes * 60, AllowBack = doc.AllowBack ?? true,
            TopicWeights = doc.TopicWeights ?? [],
            CaseStudies = caseStudies, Sections = sections, Questions = questions,
        };
    }

    List<ExamSection> Sections(List<SectionDocument>? docs, List<string> questionIds)
    {
        if (docs is not { Count: > 0 }) return [];
        var known = questionIds.ToHashSet();
        var sectionIds = new HashSet<string>();
        var placed = new Dictionary<string, string>();
        var sections = new List<ExamSection>();
        for (var i = 0; i < docs.Count; i++)
        {
            var s = docs[i];
            var p = $"sections[{i}]";
            if (Required(s.Id, $"{p}.id") is not { } sid) continue;
            if (!sectionIds.Add(sid)) Error($"{p}.id", $"Duplicate section id \"{sid}\".");
            var ids = s.Questions ?? [];
            if (ids.Count == 0) Error($"{p}.questions", "Section has no questions.");
            for (var j = 0; j < ids.Count; j++)
            {
                var q = ids[j];
                if (!known.Contains(q)) Error($"{p}.questions[{j}]", $"No question with id \"{q}\".");
                else if (placed.TryGetValue(q, out var other)) Error($"{p}.questions[{j}]", $"\"{q}\" is already in section \"{other}\".");
                else placed[q] = sid;
            }
            sections.Add(new ExamSection(sid, s.Title.NonEmpty() ?? sid, ids, s.Shuffle ?? true));
        }
        var unplaced = questionIds.Where(q => !placed.ContainsKey(q)).ToList();
        if (unplaced.Count > 0)
            Warn("sections", $"{unplaced.Count} question(s) are in the bank but no section, so they won't be asked: {string.Join(", ", unplaced.Take(8))}{(unplaced.Count > 8 ? ", …" : "")}.");
        if (placed.Count == 0) Error("sections", "No section contains a question.");
        return sections;
    }

    // MARK: Questions

    Question? Question(QuestionDocument q, string p, bool partialDefault)
    {
        var id = Required(q.Id, $"{p}.id");
        var prompt = Required(q.Prompt, $"{p}.prompt");
        var points = q.Points ?? 1;
        if (points <= 0) Error($"{p}.points", "Must be positive.");
        if (Required(q.Kind, $"{p}.kind") is not { } kindName) return null;
        if (QuestionKinds.FromRaw(kindName) is not { } kind)
        {
            Error($"{p}.kind", $"Unknown kind \"{kindName}\". Use one of: {string.Join(", ", QuestionKinds.All.Select(k => k.Raw()))}.");
            return null;
        }
        var exhibits = Exhibits(q.Exhibits, $"{p}.exhibits");
        if (Body(kind, q, p) is not { } body || id is null || prompt is null) return null;
        return new Question
        {
            Id = id, Kind = kind, Prompt = prompt,
            Topic = q.Topic?.TrimSpaces().NonEmpty() ?? "General",
            Explanation = q.Explanation.NonEmpty(), Exhibits = exhibits, CaseStudyId = q.CaseStudy,
            Points = points, PartialCredit = q.PartialCredit ?? partialDefault, Body = body,
        };
    }

    QuestionBody? Body(QuestionKind kind, QuestionDocument q, string p)
    {
        switch (kind)
        {
            case QuestionKind.Single or QuestionKind.Multiple or QuestionKind.Ordering:
            {
                var choices = ChoiceList(q.Choices, $"{p}.choices", 2);
                if (AnswerList(q.Answer, p, choices.Select(c => c.Id).ToHashSet()) is not { } list) return null;
                switch (kind)
                {
                    case QuestionKind.Single:
                        if (list.Count != 1) { Error($"{p}.answer", "single needs exactly one choice id."); return null; }
                        return new SingleBody(choices, list[0]);
                    case QuestionKind.Multiple:
                        if (list.Count == 0 || list.Distinct().Count() != list.Count) { Error($"{p}.answer", "multiple needs one or more distinct choice ids."); return null; }
                        return new MultipleBody(choices, list.ToHashSet());
                    default:
                        if (list.Count < 2 || list.Distinct().Count() != list.Count) { Error($"{p}.answer", "ordering needs two or more distinct choice ids in order."); return null; }
                        return new OrderingBody(choices, list);
                }
            }

            case QuestionKind.TrueFalse:
            {
                bool? value = q.Answer switch
                {
                    AnswerValue.Bool b => b.Value,
                    AnswerValue.String s when s.Value.ToLowerInvariant() is "true" or "false" => s.Value.ToLowerInvariant() == "true",
                    _ => null,
                };
                if (value is null) { Error($"{p}.answer", "trueFalse needs answer true or false."); return null; }
                return new SingleBody([new Choice("true", "True"), new Choice("false", "False")], value.Value ? "true" : "false");
            }

            case QuestionKind.YesNo:
            {
                if (q.Statements is not { Count: > 0 } statements) { Error($"{p}.statements", "yesNo needs a non-empty statements list."); return null; }
                var ids = new HashSet<string>();
                var rows = new List<DropdownRow>();
                for (var i = 0; i < statements.Count; i++)
                {
                    var s = statements[i];
                    var sp = $"{p}.statements[{i}]";
                    var sid = Required(s.Id, $"{sp}.id");
                    if (sid is null) continue;
                    var text = Required(s.Text, $"{sp}.text");
                    if (text is null) continue;
                    if (!ids.Add(sid)) Error($"{sp}.id", $"Duplicate statement id \"{sid}\".");
                    if (s.Answer is not { } a) { Error($"{sp}.answer", "Needs true (Yes) or false (No)."); continue; }
                    rows.Add(new DropdownRow(sid, text, ["Yes", "No"], a ? "Yes" : "No"));
                }
                return rows.Count == statements.Count ? new DropdownsBody(rows) : null;
            }

            case QuestionKind.Matching:
            {
                var items = ChoiceList(q.Items, $"{p}.items", 1);
                var targets = ChoiceList(q.Targets, $"{p}.targets", 1);
                if (q.Answer is not AnswerValue.Map { Value: var map }) { Error($"{p}.answer", "matching needs an object mapping item ids to target ids."); return null; }
                var itemIds = items.Select(c => c.Id).ToHashSet();
                var targetIds = targets.Select(c => c.Id).ToHashSet();
                foreach (var (item, target) in map.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    if (!itemIds.Contains(item)) Error($"{p}.answer", $"Unknown item id \"{item}\".");
                    if (!targetIds.Contains(target)) Error($"{p}.answer", $"Unknown target id \"{target}\".");
                }
                if (map.Count == 0) Error($"{p}.answer", "matching needs at least one placement.");
                return new MatchingBody(items, targets, map);
            }

            case QuestionKind.Dropdowns:
            {
                if (q.Rows is not { Count: > 0 } docs) { Error($"{p}.rows", "dropdowns needs a non-empty rows list."); return null; }
                var ids = new HashSet<string>();
                var rows = new List<DropdownRow>();
                for (var i = 0; i < docs.Count; i++)
                {
                    var r = docs[i];
                    var rp = $"{p}.rows[{i}]";
                    var rid = Required(r.Id, $"{rp}.id");
                    if (rid is null) continue;
                    var label = Required(r.Label, $"{rp}.label");
                    if (label is null) continue;
                    if (!ids.Add(rid)) Error($"{rp}.id", $"Duplicate row id \"{rid}\".");
                    var options = r.Options ?? [];
                    if (options.Count < 2 || options.Distinct().Count() != options.Count) { Error($"{rp}.options", "Needs two or more distinct options."); continue; }
                    if (r.Answer is not { } a || !options.Contains(a)) { Error($"{rp}.answer", "Must be exactly one of the row's options."); continue; }
                    rows.Add(new DropdownRow(rid, label, options, a));
                }
                return rows.Count == docs.Count ? new DropdownsBody(rows) : null;
            }

            case QuestionKind.FillIn:
            {
                if (q.Blanks is not { Count: > 0 } docs) { Error($"{p}.blanks", "fillIn needs a non-empty blanks list."); return null; }
                var ids = new HashSet<string>();
                var blanks = new List<Blank>();
                for (var i = 0; i < docs.Count; i++)
                {
                    var b = docs[i];
                    var bp = $"{p}.blanks[{i}]";
                    if (Required(b.Id, $"{bp}.id") is not { } bid) continue;
                    if (!ids.Add(bid)) Error($"{bp}.id", $"Duplicate blank id \"{bid}\".");
                    var accept = (b.Accept ?? []).Where(a => a.TrimSpaces().Length > 0).ToList();
                    if (accept.Count == 0 && b.Pattern is null) { Error(bp, "Needs accept (non-empty list) or pattern."); continue; }
                    if (b.Pattern is { } pattern && !Scoring.TryRegex(pattern, false, out _)) { Error($"{bp}.pattern", "Not a valid regular expression."); continue; }
                    blanks.Add(new Blank(bid, b.Label, accept, b.CaseSensitive ?? false, b.Pattern));
                }
                return blanks.Count == docs.Count ? new FillInBody(blanks) : null;
            }

            default:
            {
                if (Required(q.Image, $"{p}.image") is not { } image) return null;
                AssetPath(image, $"{p}.image");
                if (q.Regions is not { Count: > 0 } docs) { Error($"{p}.regions", "hotspot needs a non-empty regions list."); return null; }
                var ids = new HashSet<string>();
                var regions = new List<Region>();
                for (var i = 0; i < docs.Count; i++)
                {
                    var r = docs[i];
                    var rp = $"{p}.regions[{i}]";
                    if (Required(r.Id, $"{rp}.id") is not { } rid) continue;
                    if (!ids.Add(rid)) Error($"{rp}.id", $"Duplicate region id \"{rid}\".");
                    if (r is not { X: { } x, Y: { } y, Width: { } w, Height: { } h }) { Error(rp, "Needs x, y, width and height (fractions 0–1 of the image)."); continue; }
                    if (!(x >= 0 && y >= 0 && w > 0 && h > 0 && x + w <= 1.0001 && y + h <= 1.0001)) { Error(rp, "Must lie inside the image: 0 ≤ x, y and x + width, y + height ≤ 1."); continue; }
                    regions.Add(new Region(rid, r.Label, x, y, w, h));
                }
                if (AnswerList(q.Answer, p, ids) is not { Count: > 0 } list)
                {
                    if (q.Answer is not null) Error($"{p}.answer", "hotspot needs one or more region ids.");
                    return null;
                }
                if (regions.Count != docs.Count) return null;
                return new HotspotBody(image, regions, list.ToHashSet());
            }
        }
    }

    // MARK: Pieces

    List<Choice> ChoiceList(List<ChoiceDocument>? docs, string p, int min)
    {
        docs ??= [];
        if (docs.Count < min) Error(p, $"Needs at least {min} entr{(min == 1 ? "y" : "ies")}.");
        var ids = new HashSet<string>();
        var choices = new List<Choice>();
        for (var i = 0; i < docs.Count; i++)
        {
            var c = docs[i];
            var id = Required(c.Id, $"{p}[{i}].id");
            if (id is null) continue;
            var text = Required(c.Text, $"{p}[{i}].text");
            if (text is null) continue;
            if (!ids.Add(id)) Error($"{p}[{i}].id", $"Duplicate id \"{id}\".");
            choices.Add(new Choice(id, text));
        }
        return choices;
    }

    /// <summary>Answer as a list of ids, each of which must be in ids.</summary>
    List<string>? AnswerList(AnswerValue? answer, string p, HashSet<string> ids)
    {
        if (answer is null) { Error($"{p}.answer", "Missing answer."); return null; }
        if (answer.AsList is not { } list) { Error($"{p}.answer", "Must be an id or a list of ids."); return null; }
        var unknown = list.Where(id => !ids.Contains(id)).ToList();
        if (unknown.Count > 0)
        {
            Error($"{p}.answer", $"Unknown id{(unknown.Count == 1 ? "" : "s")} {string.Join(", ", unknown.Select(u => $"\"{u}\""))}. Known: {string.Join(", ", ids.OrderBy(i => i, StringComparer.Ordinal))}.");
            return null;
        }
        return list;
    }

    List<Exhibit> Exhibits(List<ExhibitDocument>? docs, string p)
    {
        var exhibits = new List<Exhibit>();
        for (var i = 0; i < (docs?.Count ?? 0); i++)
        {
            var e = docs![i];
            var ep = $"{p}[{i}]";
            var title = e.Title ?? $"Exhibit {i + 1}";
            switch (e.Text, e.Image)
            {
                case ({ } text, null):
                    exhibits.Add(new Exhibit(title, text, null));
                    break;
                case (null, { } image):
                    AssetPath(image, $"{ep}.image");
                    exhibits.Add(new Exhibit(title, null, image));
                    break;
                default:
                    Error(ep, "Needs exactly one of text or image.");
                    break;
            }
        }
        return exhibits;
    }

    static readonly HashSet<string> ImageExtensions = ["png", "jpg", "jpeg", "gif", "heic", "tiff", "tif", "bmp", "webp", "pdf"];

    void AssetPath(string path, string p)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (path.StartsWith('/') || path.Contains("://") || parts.Contains(".."))
        {
            Error(p, "Must be a relative path inside the exam folder, like \"images/topology.png\".");
            return;
        }
        var last = parts.LastOrDefault() ?? "";
        var dot = last.LastIndexOf('.');
        var ext = dot > 0 ? last[(dot + 1)..].ToLowerInvariant() : "";
        if (!ImageExtensions.Contains(ext)) Warn(p, $"Unusual image extension \".{ext}\".");
        if (baseDir is not null && !File.Exists(Path.Combine(baseDir, path)))
            Error(p, $"File not found: {path} (relative to {baseDir.TrimEnd('/', '\\')}).");
    }

    string? Required(string? value, string p)
    {
        if (string.IsNullOrWhiteSpace(value)) { Error(p, "Missing or empty."); return null; }
        return value;
    }

    // MARK: Raw JSON checks

    void CheckKeys(JsonElement exam)
    {
        if (exam.ValueKind != JsonValueKind.Object) return;
        Unknown(exam, KnownKeys.Exam, "$");
        Each(exam, "sections", KnownKeys.Section, "sections");
        if (exam.TryGetProperty("scale", out var s) && s.ValueKind == JsonValueKind.Object) Unknown(s, KnownKeys.Scale, "scale");
        foreach (var (c, i) in Objects(exam, "caseStudies"))
        {
            Unknown(c, KnownKeys.CaseStudy, $"caseStudies[{i}]");
            Each(c, "exhibits", KnownKeys.Exhibit, $"caseStudies[{i}].exhibits");
        }
        foreach (var (q, i) in Objects(exam, "questions"))
        {
            var p = $"questions[{i}]";
            Unknown(q, KnownKeys.Question, p);
            Each(q, "exhibits", KnownKeys.Exhibit, $"{p}.exhibits");
            foreach (var key in new[] { "choices", "items", "targets" }) Each(q, key, KnownKeys.Choice, $"{p}.{key}");
            Each(q, "rows", KnownKeys.Row, $"{p}.rows");
            Each(q, "statements", KnownKeys.Statement, $"{p}.statements");
            Each(q, "blanks", KnownKeys.Blank, $"{p}.blanks");
            Each(q, "regions", KnownKeys.Region, $"{p}.regions");
        }
    }

    /// <summary>Only arrays whose every element is an object count, like Swift's `as? [[String: Any]]`.</summary>
    static IEnumerable<(JsonElement, int)> Objects(JsonElement parent, string key)
    {
        if (!parent.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) return [];
        var items = arr.EnumerateArray().ToList();
        if (items.Any(e => e.ValueKind != JsonValueKind.Object)) return [];
        return items.Select((e, i) => (e, i));
    }

    void Each(JsonElement parent, string key, HashSet<string> known, string p)
    {
        foreach (var (d, i) in Objects(parent, key)) Unknown(d, known, $"{p}[{i}]");
    }

    void Unknown(JsonElement dict, HashSet<string> known, string p)
    {
        foreach (var key in dict.EnumerateObject().Select(o => o.Name).OrderBy(k => k, StringComparer.Ordinal))
            if (!known.Contains(key)) Warn(p == "$" ? key : $"{p}.{key}", "Unknown key; it is ignored. Check spelling against the schema.");
    }
}
