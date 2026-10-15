using System.Text.Json;
using MockiCore;

namespace MockiExams;

/// <summary>A .mockiexam project: exam.json plus the images it references, held in memory while editing.
/// The JSON is kept as the raw ExamDocument so a half-finished project still opens.</summary>
public sealed class ProjectDocument
{
    public ExamDocument Exam { get; private set; }
    /// <summary>Relative path (e.g. images/topology.png) → file data.</summary>
    public Dictionary<string, byte[]> Assets { get; private set; }
    /// <summary>The project folder on disk, or null until first saved.</summary>
    public string? Path { get; set; }
    /// <summary>Unsaved changes: the undo history has moved away from where it was at the last save.</summary>
    public bool IsDirty => (undo.Count > 0 ? undo.Peek() : null) != savedAt || forcedDirty;
    object? savedAt;
    bool forcedDirty;
    public event Action? Changed;

    public ProjectDocument()
    {
        Exam = new ExamDocument("new-exam", "Untitled Exam", []) { PassPercent = 70, TimeLimitMinutes = 60 };
        Assets = [];
    }

    ProjectDocument(ExamDocument exam, Dictionary<string, byte[]> assets, string path)
    {
        Exam = exam;
        Assets = assets;
        Path = path;
    }

    static readonly HashSet<string> Ignored = [".DS_Store", "Thumbs.db", "desktop.ini"];

    public static ProjectDocument Load(string folder)
    {
        var json = System.IO.Path.Combine(folder, "exam.json");
        if (!File.Exists(json)) throw new IOException($"{System.IO.Path.GetFileName(folder.TrimEnd('\\', '/'))} has no exam.json, so it isn't a MockiExams project.");
        var exam = ExamFormat.ReadDocument(File.ReadAllBytes(json)) ?? throw new IOException("exam.json is empty.");
        var assets = new Dictionary<string, byte[]>();
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var rel = System.IO.Path.GetRelativePath(folder, file).Replace('\\', '/');
            if (rel == "exam.json" || Ignored.Contains(System.IO.Path.GetFileName(file))) continue;
            assets[rel] = File.ReadAllBytes(file);
        }
        return new ProjectDocument(exam, assets, folder);
    }

    /// <summary>Writes the project as a plain folder: the save format, and the copy used for preview and install.</summary>
    public void WriteTo(string folder)
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(System.IO.Path.Combine(folder, "exam.json"), ExamFormat.Encode(Exam));
        foreach (var (path, data) in Assets)
        {
            var file = System.IO.Path.Combine(folder, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, data);
        }
    }

    /// <summary>Saves through a sibling folder so a failed save never leaves a half-written project.</summary>
    public void Save(string folder)
    {
        var staging = folder.TrimEnd('\\', '/') + ".saving";
        WriteTo(staging);
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        Directory.Move(staging, folder);
        Path = folder;
        savedAt = undo.Count > 0 ? undo.Peek() : null;
        forcedDirty = false;
        lastKey = null;
        Changed?.Invoke();
    }

    static string Scratch(string kind) => System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mocki-{kind}-{Guid.NewGuid()}");

    /// <summary>Validates against a scratch copy on disk, so image references are checked too.</summary>
    public (MockiCore.Exam? Exam, List<Issue> Issues) Validate()
    {
        var folder = Scratch("validate");
        try
        {
            WriteTo(folder);
            var (exam, issues) = ExamFormat.Validate(File.ReadAllBytes(System.IO.Path.Combine(folder, "exam.json")), folder);
            foreach (var path in Assets.Keys.OrderBy(k => k, StringComparer.Ordinal).Where(p => Usage(p) == 0))
                issues.Add(new Issue(Severity.Warning, "assets", $"{path} isn't used by any question."));
            if (exam is not null) exam.AssetBase = null;
            return (exam, issues);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, [new Issue(Severity.Error, "$", e.Message)]);
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (Exception) { }
        }
    }

    /// <summary>A playable copy on disk (for preview and install); null with issues if the project doesn't validate.</summary>
    public (MockiCore.Exam? Exam, IReadOnlyList<Issue> Issues) Materialize()
    {
        var folder = Scratch("preview");
        try
        {
            WriteTo(folder);
            return (ExamFormat.Decode(File.ReadAllBytes(System.IO.Path.Combine(folder, "exam.json")), folder), []);
        }
        catch (ExamLoadException e) { return (null, e.Issues); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return (null, [new Issue(Severity.Error, "$", e.Message)]); }
    }

    // MARK: Undo

    sealed record Snapshot(string Exam, Dictionary<string, byte[]> Assets);
    readonly Stack<Snapshot> undo = new();
    readonly Stack<Snapshot> redo = new();
    string? lastKey;
    DateTime lastEdit;

    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    Snapshot Take() => new(JsonSerializer.Serialize(Exam, ExamFormat.Options), new Dictionary<string, byte[]>(Assets));

    void Restore(Snapshot s)
    {
        Exam = JsonSerializer.Deserialize<ExamDocument>(s.Exam, ExamFormat.Options)!;
        Assets = new Dictionary<string, byte[]>(s.Assets);
    }

    /// <summary>Applies a change as one undoable step. Typing in the same field within a moment
    /// (same key) joins the previous step, so undo removes a word, not a letter.</summary>
    public void Edit(Action change, string? key = null)
    {
        var before = Take();
        change();
        // A change that changes nothing (a control echoing its own value) is not an edit.
        if (JsonSerializer.Serialize(Exam, ExamFormat.Options) == before.Exam && SameAssets(before.Assets)) return;
        var now = DateTime.UtcNow;
        if (key is null || key != lastKey || (now - lastEdit).TotalSeconds > 1.5)
        {
            undo.Push(before);
            redo.Clear();
        }
        lastKey = key;
        lastEdit = now;
        // Typing joined to the step that was current at save time still changes the saved file.
        if (savedAt is not null && undo.Count > 0 && undo.Peek() == savedAt) forcedDirty = true;
        Changed?.Invoke();
    }

    bool SameAssets(Dictionary<string, byte[]> other) =>
        other.Count == Assets.Count && other.All(kv => Assets.TryGetValue(kv.Key, out var v) && ReferenceEquals(v, kv.Value));

    public void Undo()
    {
        if (undo.Count == 0) return;
        redo.Push(Take());
        Restore(undo.Pop());
        lastKey = null;
        forcedDirty = false;
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (redo.Count == 0) return;
        undo.Push(Take());
        Restore(redo.Pop());
        lastKey = null;
        forcedDirty = false;
        Changed?.Invoke();
    }

    // MARK: Editing helpers

    public List<QuestionDocument> Questions
    {
        get => Exam.Questions ??= [];
        set => Exam.Questions = value;
    }

    public string UniqueQuestionId(string baseId)
    {
        var used = Questions.Select(q => q.Id).OfType<string>().ToHashSet();
        if (!used.Contains(baseId)) return baseId;
        for (var n = 2; ; n++) if (!used.Contains($"{baseId}-{n}")) return $"{baseId}-{n}";
    }

    /// <summary>A new question of a kind, seeded from the format templates, not yet in the bank.</summary>
    public QuestionDocument MakeQuestion(QuestionKind kind)
    {
        var q = JsonSerializer.Deserialize<QuestionDocument>(Templates.Question(kind), ExamFormat.Options) ?? new QuestionDocument("q", kind, "");
        q.Id = UniqueQuestionId($"q{Questions.Count + 1}");
        q.Topic = null;
        q.Explanation = null;
        if (kind == QuestionKind.Hotspot)
        {
            q.Image = Assets.Keys.OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault(k => !k.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
            q.Regions = [];
            q.Answer = new AnswerValue.Strings([]);
        }
        return q;
    }

    /// <summary>The questions the exam asks: section order when there are sections, otherwise the bank.</summary>
    public List<QuestionDocument> AskedQuestions
    {
        get
        {
            if (Exam.Sections is not { Count: > 0 } sections) return Questions;
            var byId = new Dictionary<string, QuestionDocument>();
            foreach (var q in Questions) if (q.Id is { } id) byId.TryAdd(id, q);
            return sections.SelectMany(s => (s.Questions ?? []).Where(byId.ContainsKey).Select(id => byId[id])).ToList();
        }
    }

    public List<string> Topics
    {
        get
        {
            var fromQuestions = Questions.Select(q => NonEmpty(q.Topic) ?? "General");
            return fromQuestions.Concat((Exam.TopicWeights ?? []).Keys.OrderBy(k => k, StringComparer.Ordinal)).Distinct().ToList();
        }
    }

    /// <summary>Appends converted or copied questions, renaming ids that clash with the bank.</summary>
    public int Append(IEnumerable<QuestionDocument> incoming)
    {
        var n = 0;
        foreach (var q in incoming)
        {
            q.Id = UniqueQuestionId(NonEmpty(q.Id) ?? $"q{Questions.Count + 1}");
            Questions.Add(q);
            n++;
        }
        return n;
    }

    public string? DuplicateQuestion(string id)
    {
        var i = Questions.FindIndex(q => q.Id == id);
        if (i < 0) return null;
        var copy = JsonSerializer.Deserialize<QuestionDocument>(JsonSerializer.Serialize(Questions[i], ExamFormat.Options), ExamFormat.Options)!;
        copy.Id = UniqueQuestionId(id + "-copy");
        Questions.Insert(i + 1, copy);
        return copy.Id;
    }

    public void DeleteQuestion(string id)
    {
        Questions.RemoveAll(q => q.Id == id);
        foreach (var s in Exam.Sections ?? []) s.Questions?.RemoveAll(x => x == id);
    }

    /// <summary>Renames a question and updates every section that lists it.</summary>
    public void RenameQuestion(string oldId, string newId)
    {
        var q = Questions.FirstOrDefault(x => x.Id == oldId);
        if (q is null) return;
        q.Id = newId;
        foreach (var s in Exam.Sections ?? [])
            if (s.Questions is { } ids) for (var i = 0; i < ids.Count; i++) if (ids[i] == oldId) ids[i] = newId;
    }

    public int Usage(string asset)
    {
        var exhibits = (Exam.CaseStudies ?? []).SelectMany(c => c.Exhibits ?? []).Concat(Questions.SelectMany(q => q.Exhibits ?? []));
        return exhibits.Count(e => e.Image == asset) + Questions.Count(q => q.Image == asset);
    }

    public string AddAsset(byte[] data, string name)
    {
        var ext = System.IO.Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        var stem = Converter.Slug(System.IO.Path.GetFileNameWithoutExtension(name));
        if (stem.Length == 0) stem = "image";
        var path = $"images/{stem}.{ext}";
        var n = 2;
        while (Assets.TryGetValue(path, out var existing) && !existing.AsSpan().SequenceEqual(data))
        {
            path = $"images/{stem}-{n}.{ext}";
            n++;
        }
        Assets[path] = data;
        return path;
    }

    public static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    public string DisplayName => NonEmpty(Exam.Title) ?? "Untitled Exam";
}

public static class QuestionDocuments
{
    public static List<string> AnswerIds(this QuestionDocument q) => q.Answer switch
    {
        AnswerValue.String s => [s.Value],
        AnswerValue.Strings l => l.Value.ToList(),
        _ => [],
    };

    public static QuestionKind? KindValue(this QuestionDocument q) => QuestionKinds.FromRaw(q.Kind);

    public static string NextId(string prefix, IEnumerable<string?> used)
    {
        var taken = used.OfType<string>().ToHashSet();
        for (var n = 1; ; n++) if (!taken.Contains($"{prefix}{n}")) return $"{prefix}{n}";
    }

    public static string NextLetter(IEnumerable<string?> used)
    {
        var taken = used.OfType<string>().ToHashSet();
        for (var i = 0; i < 702; i++)
        {
            var id = i < 26 ? ((char)(97 + i)).ToString() : $"c{i}";
            if (!taken.Contains(id)) return id;
        }
        return Guid.NewGuid().ToString()[..6];
    }
}
