using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MockiCore;

public enum SourceFormat { Aiken, Gift, Csv }

public static class SourceFormats
{
    public static string Raw(this SourceFormat f) => f switch { SourceFormat.Aiken => "aiken", SourceFormat.Gift => "gift", _ => "csv" };
    public static SourceFormat? FromRaw(string raw) => raw switch { "aiken" => SourceFormat.Aiken, "gift" => SourceFormat.Gift, "csv" => SourceFormat.Csv, _ => null };

    /// <summary>Guesses from the extension, then from content.</summary>
    public static SourceFormat? Detect(string fileName, string text)
    {
        var dot = fileName.LastIndexOf('.');
        var ext = dot > 0 ? fileName[(dot + 1)..].ToLowerInvariant() : "";
        switch (ext)
        {
            case "gift": return SourceFormat.Gift;
            case "csv" or "tsv": return SourceFormat.Csv;
            case "aiken": return SourceFormat.Aiken;
        }
        if (Regex.IsMatch(text, @"^ANSWER:\s*[A-Z]", RegexOptions.Multiline)) return SourceFormat.Aiken;
        if (Regex.IsMatch(text, @"\{[^}]*[=~][^}]*\}")) return SourceFormat.Gift;
        var first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (first is not null && first.Contains(',') && first.ToLowerInvariant().Contains("question")) return SourceFormat.Csv;
        return null;
    }
}

public sealed record Conversion(ExamDocument Document, List<Issue> Skipped);

public static class Converter
{
    public static Conversion Convert(string text, SourceFormat format, string id, string title, string? topic = null)
    {
        var c = format switch
        {
            SourceFormat.Aiken => Aiken.Convert(text),
            SourceFormat.Gift => Gift.Convert(text),
            _ => Csv.Convert(text),
        };
        c.Document.Id = id;
        c.Document.Title = title;
        if (topic is not null)
            foreach (var q in c.Document.Questions ?? []) q.Topic ??= topic;
        return c;
    }

    internal static string LetterId(int i) => ((char)(97 + i % 26)).ToString() + (i >= 26 ? (i / 26).ToString(CultureInfo.InvariantCulture) : "");

    public static string Slug(string s)
    {
        var sb = new StringBuilder();
        foreach (var rune in s.ToLowerInvariant().EnumerateRunes())
            sb.Append(Text2.IsAlphanumeric(rune) ? rune.ToString() : "-");
        return string.Join("-", sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Foundation's components(separatedBy: .newlines): every newline character splits.</summary>
    internal static string[] Lines(string text) => text.Split(NewlineChars);

    static readonly char[] NewlineChars = [.. new[] { 0x0A, 0x0D, 0x0B, 0x0C, 0x85, 0x2028, 0x2029 }.Select(c => (char)c)];
}

static class Text2
{
    /// <summary>Foundation's CharacterSet.alphanumerics: letters, marks and digits.</summary>
    public static bool IsAlphanumeric(Rune r) => Rune.GetUnicodeCategory(r) switch
    {
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber => true,
        _ => false,
    };

    public static string TrimAll(this string s) => s.Trim();
}

/// <summary>Aiken: question lines, then `A. text` / `A) text` options, then `ANSWER: B` (or `ANSWER: A, C`).</summary>
static class Aiken
{
    static readonly Regex Option = new(@"^([A-Za-z])[\.\)]\s+(.+)$");
    static readonly Regex AnswerLine = new(@"^ANSWER:\s*(.+)$", RegexOptions.IgnoreCase);

    public static Conversion Convert(string text)
    {
        var questions = new List<QuestionDocument>();
        var skipped = new List<Issue>();
        var prompt = new List<string>();
        var options = new List<(string Letter, string Text)>();
        var startLine = 1;
        var lines = Converter.Lines(text);

        for (var n = 0; n < lines.Length; n++)
        {
            var line = lines[n].TrimSpaces();
            if (line.Length == 0 && options.Count == 0) continue;
            if (prompt.Count == 0 && options.Count == 0) startLine = n + 1;
            Match m;
            if ((m = Option.Match(line)).Success && prompt.Count > 0)
            {
                options.Add((m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value));
            }
            else if ((m = AnswerLine.Match(line)).Success)
            {
                var letters = m.Groups[1].Value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Select(l => l.ToUpperInvariant()).ToList();
                var index = new Dictionary<string, int>();
                for (var i = 0; i < options.Count; i++) index.TryAdd(options[i].Letter, i);
                var ids = letters.Where(index.ContainsKey).Select(l => Converter.LetterId(index[l])).ToList();
                if (prompt.Count == 0 || options.Count < 2 || ids.Count != letters.Count || ids.Count == 0)
                {
                    skipped.Add(new Issue(Severity.Warning, $"line {startLine}", "Skipped: needs a question, 2+ options and an ANSWER naming them."));
                }
                else
                {
                    var q = new QuestionDocument($"q{questions.Count + 1}", ids.Count == 1 ? QuestionKind.Single : QuestionKind.Multiple, string.Join("\n", prompt))
                    {
                        Choices = options.Select((o, i) => new ChoiceDocument(Converter.LetterId(i), o.Text)).ToList(),
                        Answer = ids.Count == 1 ? new AnswerValue.String(ids[0]) : new AnswerValue.Strings(ids),
                    };
                    questions.Add(q);
                }
                prompt.Clear();
                options.Clear();
            }
            else if (options.Count == 0)
            {
                prompt.Add(line);
            }
            else
            {
                // Continuation of the previous option.
                options[^1] = (options[^1].Letter, options[^1].Text + " " + line);
            }
        }
        if (prompt.Count > 0)
            skipped.Add(new Issue(Severity.Warning, $"line {startLine}", "Skipped: question has no ANSWER line."));
        return new Conversion(new ExamDocument("", "", questions), skipped);
    }
}

/// <summary>Moodle GIFT: single/multiple choice (incl. `~%50%` weights), true/false, short answer,
/// matching, simple numeric, `$CATEGORY:` topics and `####` general feedback.</summary>
static class Gift
{
    public static Conversion Convert(string text)
    {
        var questions = new List<QuestionDocument>();
        var skipped = new List<Issue>();
        string? topic = null;
        var block = new List<string>();
        var blockLine = 1;

        void Flush()
        {
            var source = string.Join("\n", block).Trim();
            block.Clear();
            if (source.Length == 0) return;
            var (q, failure) = Parse(source, questions.Count + 1);
            if (q is not null)
            {
                q.Topic = topic;
                questions.Add(q);
            }
            else
            {
                skipped.Add(new Issue(Severity.Warning, $"line {blockLine}", $"Skipped: {failure}"));
            }
        }

        var lines = Converter.Lines(text);
        for (var n = 0; n < lines.Length; n++)
        {
            var line = lines[n];
            var t = line.TrimSpaces();
            if (t.StartsWith("//")) continue;
            if (t.StartsWith("$CATEGORY:"))
            {
                Flush();
                var last = t["$CATEGORY:".Length..].Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.TrimSpaces();
                topic = last is not null && !last.StartsWith('$') ? last : null;
                continue;
            }
            if (t.Length == 0) { Flush(); continue; }
            if (block.Count == 0) blockLine = n + 1;
            block.Add(line);
        }
        Flush();
        return new Conversion(new ExamDocument("", "", questions), skipped);
    }

    static (QuestionDocument?, string?) Parse(string source, int index)
    {
        var s = source;
        var id = $"q{index}";
        if (s.StartsWith("::"))
        {
            var end = s.IndexOf("::", 2, StringComparison.Ordinal);
            if (end >= 0)
            {
                var title = Unescape(s[2..end]);
                id = Converter.Slug(title).NonEmpty() ?? id;
                s = s[(end + 2)..];
            }
        }
        if (s.StartsWith('['))
        {
            var close = s.IndexOf(']');
            if (close >= 0) s = s[(close + 1)..];
        }

        var open = UnescapedIndex('{', s, 0);
        var closeBrace = open < 0 ? -1 : UnescapedIndex('}', s, open);
        if (open < 0 || closeBrace < 0) return (null, "no {answer} block");
        var before = Unescape(s[..open]).Trim();
        var after = Unescape(s[(closeBrace + 1)..]).Trim();
        var prompt = after.Length == 0 ? before : $"{before} _____ {after}";
        var body = s[(open + 1)..closeBrace].Trim();

        string? explanation = null;
        var fb = body.IndexOf("####", StringComparison.Ordinal);
        if (fb >= 0)
        {
            explanation = Unescape(body[(fb + 4)..]).Trim();
            body = body[..fb];
        }

        QuestionDocument Make(QuestionKind kind) => new(id, kind, prompt) { Explanation = explanation.NonEmpty() };

        var upper = StripFeedback(body).Trim().ToUpperInvariant();
        if (upper is "T" or "TRUE" or "F" or "FALSE")
        {
            var q = Make(QuestionKind.TrueFalse);
            q.Answer = new AnswerValue.Bool(upper.StartsWith('T'));
            return (q, null);
        }
        if (body.Length == 0) return (null, "essay questions have no answer key");
        if (body.StartsWith('#'))
        {
            var value = StripFeedback(body[1..]).TrimSpaces();
            if (!Regex.IsMatch(value, @"^-?\d+(\.\d+)?$")) return (null, "numeric ranges and tolerances aren't supported; use an exact number");
            var q = Make(QuestionKind.FillIn);
            q.Blanks = [new BlankDocument("answer", [value])];
            return (q, null);
        }

        var answers = Tokens(body);
        if (answers.Any(a => a.Text.Contains("->")))
        {
            var items = new List<ChoiceDocument>();
            var targets = new List<string>();
            var map = new Dictionary<string, string>();
            for (var i = 0; i < answers.Count; i++)
            {
                var parts = answers[i].Text.Split("->");
                var left = parts[0].TrimSpaces();
                var right = string.Join("->", parts.Skip(1)).TrimSpaces();
                if (!targets.Contains(right)) targets.Add(right);
                var itemId = $"i{i + 1}";
                if (left.Length > 0)
                {
                    items.Add(new ChoiceDocument(itemId, left));
                    map[itemId] = $"t{targets.IndexOf(right) + 1}";
                }
            }
            var q = Make(QuestionKind.Matching);
            q.Items = items;
            q.Targets = targets.Select((t, i) => new ChoiceDocument($"t{i + 1}", t)).ToList();
            q.Answer = new AnswerValue.Map(map);
            return (q, null);
        }

        if (!answers.Any(a => a.Marker == '~'))
        {
            var q = Make(QuestionKind.FillIn);
            q.Blanks = [new BlankDocument("answer", answers.Select(a => a.Text).ToList())];
            return (q, null);
        }

        var correct = answers.Select((a, i) => (a, i)).Where(p => p.a.Marker == '=' || (p.a.Weight ?? 0) > 0).Select(p => Converter.LetterId(p.i)).ToList();
        if (correct.Count == 0) return (null, "no correct answer marked");
        var weighted = answers.Any(a => a.Weight is not null);
        var choice = Make(weighted || correct.Count > 1 ? QuestionKind.Multiple : QuestionKind.Single);
        choice.Choices = answers.Select((a, i) => new ChoiceDocument(Converter.LetterId(i), a.Text)).ToList();
        choice.Answer = choice.Kind == "single" ? new AnswerValue.String(correct[0]) : new AnswerValue.Strings(correct);
        return (choice, null);
    }

    sealed record Token(char Marker, string Text, double? Weight);

    static readonly Regex WeightPrefix = new(@"^%(-?\d+(?:\.\d+)?)%");

    /// <summary>Splits `=a ~b ~%50%c#feedback` into answers, honoring `\=`/`\~` escapes.</summary>
    static List<Token> Tokens(string body)
    {
        var raw = new List<(char Marker, StringBuilder Text)>();
        (char Marker, StringBuilder Text)? current = null;
        var escaped = false;
        foreach (var ch in body)
        {
            if (escaped) { current?.Text.Append(ch); escaped = false; continue; }
            if (ch == '\\') { current?.Text.Append(ch); escaped = true; continue; }
            if (ch is '=' or '~')
            {
                if (current is { } c) raw.Add(c);
                current = (ch, new StringBuilder());
            }
            else
            {
                current?.Text.Append(ch);
            }
        }
        if (current is { } last) raw.Add(last);
        return raw.Select(t =>
        {
            var text = StripFeedback(t.Text.ToString()).Trim();
            double? weight = null;
            var m = WeightPrefix.Match(text);
            if (m.Success)
            {
                weight = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                text = text[m.Length..].TrimSpaces();
            }
            return new Token(t.Marker, Unescape(text), weight);
        }).ToList();
    }

    static string StripFeedback(string s)
    {
        var escaped = false;
        for (var i = 0; i < s.Length; i++)
        {
            if (escaped) { escaped = false; continue; }
            if (s[i] == '\\') { escaped = true; continue; }
            if (s[i] == '#') return s[..i];
        }
        return s;
    }

    static int UnescapedIndex(char target, string s, int start)
    {
        var escaped = false;
        for (var i = start; i < s.Length; i++)
        {
            if (escaped) { escaped = false; continue; }
            if (s[i] == '\\') { escaped = true; continue; }
            if (s[i] == target) return i;
        }
        return -1;
    }

    static string Unescape(string s)
    {
        var sb = new StringBuilder();
        var escaped = false;
        foreach (var ch in s)
        {
            if (escaped) { sb.Append(ch == 'n' ? '\n' : ch); escaped = false; }
            else if (ch == '\\') escaped = true;
            else sb.Append(ch);
        }
        return sb.ToString();
    }
}

/// <summary>One question per row. Columns (header names, any case): `question` or `prompt`; options in
/// `A`…`J` (or `option a`, `option1`…); `answer` as letters (`B`, `A,C`) or option text;
/// optional `id`, `topic`, `explanation`, `kind`. With no options, `answer` is a fill-in
/// (alternatives split by `|`), or `true`/`false` for a true/false question.</summary>
static class Csv
{
    public static Conversion Convert(string text)
    {
        var rows = Parse(text);
        if (rows.Count == 0)
            return new Conversion(new ExamDocument("", "", []), [new Issue(Severity.Error, "line 1", "Empty file.")]);
        var names = rows[0].Select(n => n.TrimSpaces().ToLowerInvariant()).ToList();
        int? Column(params string[] candidates)
        {
            var i = names.FindIndex(candidates.Contains);
            return i < 0 ? null : i;
        }
        var qCol = Column("question", "prompt", "question text");
        var aCol = Column("answer", "answers", "correct", "correct answer", "key");
        var idCol = Column("id");
        var topicCol = Column("topic", "category", "domain", "objective");
        var exCol = Column("explanation", "rationale", "feedback");
        var kindCol = Column("kind", "type");
        var optionCols = new List<int>();
        for (var i = 0; i < 10; i++)
        {
            var letter = ((char)(97 + i)).ToString();
            if (Column(letter, $"option {letter}", $"choice {letter}", $"option{i + 1}", $"option {i + 1}", $"choice{i + 1}") is { } c) optionCols.Add(c);
        }

        var questions = new List<QuestionDocument>();
        var skipped = new List<Issue>();
        if (qCol is null || aCol is null)
        {
            skipped.Add(new Issue(Severity.Error, "line 1", $"Header needs a question (or prompt) column and an answer column. Found: {string.Join(", ", names)}."));
            return new Conversion(new ExamDocument("", "", []), skipped);
        }

        for (var n = 0; n < rows.Count - 1; n++)
        {
            var row = rows[n + 1];
            string? Cell(int? i) => i is { } c && c < row.Count ? row[c].Trim().NonEmpty() : null;
            var line = $"line {n + 2}";
            if (Cell(qCol) is not { } prompt)
            {
                if (row.Any(c => c.TrimSpaces().Length > 0)) skipped.Add(new Issue(Severity.Warning, line, "Skipped: empty question."));
                continue;
            }
            if (Cell(aCol) is not { } answer)
            {
                skipped.Add(new Issue(Severity.Warning, line, "Skipped: no answer."));
                continue;
            }
            var id = Cell(idCol) ?? $"q{n + 1}";
            var options = optionCols.Select(c => Cell(c)).OfType<string>().ToList();
            QuestionDocument q;

            if (options.Count == 0)
            {
                if (answer.ToLowerInvariant() is "true" or "false")
                {
                    q = new QuestionDocument(id, QuestionKind.TrueFalse, prompt) { Answer = new AnswerValue.Bool(answer.ToLowerInvariant() == "true") };
                }
                else
                {
                    q = new QuestionDocument(id, QuestionKind.FillIn, prompt)
                    {
                        Blanks = [new BlankDocument("answer", answer.Split('|').Select(a => a.TrimSpaces()).ToList())],
                    };
                }
            }
            else
            {
                var ids = new List<string>();
                var exact = options.FindIndex(o => string.Equals(o, answer, StringComparison.OrdinalIgnoreCase));
                if (exact >= 0)
                {
                    ids = [Converter.LetterId(exact)];
                }
                else
                {
                    char[] seps = [',', ';', '|', ' '];
                    var parts = answer.IndexOfAny(seps) >= 0
                        ? answer.Split(seps, StringSplitOptions.RemoveEmptyEntries).ToList()
                        : answer.Length <= options.Count && answer.All(char.IsLetter) ? answer.Select(c => c.ToString()).ToList() : [answer];
                    foreach (var p in parts)
                    {
                        var a = p.Length == 1 ? char.ToUpperInvariant(p[0]) : '\0';
                        if (p.Length == 1 && a >= 65 && a - 65 < options.Count)
                        {
                            ids.Add(Converter.LetterId(a - 65));
                        }
                        else if (options.FindIndex(o => string.Equals(o, p, StringComparison.OrdinalIgnoreCase)) is var i and >= 0)
                        {
                            ids.Add(Converter.LetterId(i));
                        }
                        else
                        {
                            ids = [];
                            break;
                        }
                    }
                }
                if (ids.Count == 0)
                {
                    skipped.Add(new Issue(Severity.Warning, line, $"Skipped: answer \"{answer}\" doesn't name an option."));
                    continue;
                }
                var declared = QuestionKinds.FromRaw(Cell(kindCol));
                var kind = declared == QuestionKind.Ordering ? QuestionKind.Ordering : (ids.Count == 1 ? QuestionKind.Single : QuestionKind.Multiple);
                q = new QuestionDocument(id, kind, prompt)
                {
                    Choices = options.Select((o, i) => new ChoiceDocument(Converter.LetterId(i), o)).ToList(),
                    Answer = kind == QuestionKind.Single ? new AnswerValue.String(ids[0]) : new AnswerValue.Strings(ids),
                };
            }
            q.Topic = Cell(topicCol);
            q.Explanation = Cell(exCol);
            questions.Add(q);
        }
        return new Conversion(new ExamDocument("", "", questions), skipped);
    }

    /// <summary>RFC 4180 with quoted fields; delimiter is whichever of `,` `;` tab is most common in line 1.</summary>
    internal static List<List<string>> Parse(string text)
    {
        var nl = text.IndexOf('\n');
        var firstLine = nl < 0 ? text : text[..nl];
        var delimiter = ',';
        foreach (var d in new[] { ';', '\t' })
            if (firstLine.Count(c => c == d) > firstLine.Count(c => c == delimiter)) delimiter = d;
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = false;
                }
                else field.Append(ch);
            }
            else if (ch == '"' && field.Length == 0) quoted = true;
            else if (ch == delimiter) { row.Add(field.ToString()); field.Clear(); }
            else if (ch == '\n') { row.Add(field.ToString()); rows.Add(row); row = []; field.Clear(); }
            else if (ch != '\r') field.Append(ch);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }
}
