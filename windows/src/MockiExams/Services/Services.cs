using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Threading;
using MockiCore;

namespace MockiExams;

public enum Appearance { System, Light, Dark }

public enum ExamStyle { Modern, Classic }

public static class ExamStyles
{
    public static string Label(this ExamStyle s) => s == ExamStyle.Modern ? "Modern" : "Classic testing center";
    public static readonly (ExamStyle, string)[] Options = [(ExamStyle.Modern, "Modern"), (ExamStyle.Classic, "Classic testing center")];
}

static class AppPaths
{
    public static string Root
    {
        get
        {
            // MOCKIEXAMS_DATA points test runs at a scratch folder.
            var dir = Environment.GetEnvironmentVariable("MOCKIEXAMS_DATA") is { Length: > 0 } custom
                ? custom
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MockiExams");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Writes through a temporary file so a crash never leaves half a file.</summary>
    public static void WriteAtomically(string path, string text)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, true);
    }
}

/// <summary>MockiExams › Settings: appearance, exam screen style and full screen.</summary>
public sealed class AppSettings
{
    public static AppSettings Shared { get; } = Load();
    public static event Action? Changed;

    public Appearance Appearance { get; set; } = Appearance.System;
    public ExamStyle ExamStyle { get; set; } = ExamStyle.Modern;
    public bool FullScreenExams { get; set; } = true;

    static string FilePath => Path.Combine(AppPaths.Root, "settings.json");

    static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), AppPaths.Json) ?? new(); }
        catch (Exception) { return new(); }
    }

    public void Save()
    {
        try { AppPaths.WriteAtomically(FilePath, JsonSerializer.Serialize(this, AppPaths.Json)); } catch (Exception) { }
        Changed?.Invoke();
    }
}

/// <summary>One finished run, with the same fields the macOS app keeps.</summary>
public sealed record Attempt
{
    public required string ExamId { get; init; }
    public required string ExamTitle { get; init; }
    public DateTime Date { get; init; }
    public double Duration { get; init; }
    public string Mode { get; init; } = "practice";
    public int Percent { get; init; }
    public int? ScaledScore { get; init; }
    public bool Passed { get; init; }
    public int QuestionCount { get; init; }
    public List<string> MissedQuestionIds { get; init; } = [];
    public List<TopicScore> Topics { get; init; } = [];

    public SessionMode SessionMode => Mode == "exam" ? SessionMode.Exam : SessionMode.Practice;

    public static Attempt From(SessionResult result) => new()
    {
        ExamId = result.Exam.Id, ExamTitle = result.Exam.Title, Date = result.FinishedAt, Duration = result.Duration,
        Mode = result.Mode.Raw(), Percent = result.Percent, ScaledScore = result.ScaledScore, Passed = result.Passed,
        QuestionCount = result.Items.Count, MissedQuestionIds = result.MissedIds.ToList(), Topics = result.Topics.ToList(),
    };
}

public sealed class HistoryStore
{
    public static HistoryStore Shared { get; } = new();
    public event Action? Changed;

    readonly List<Attempt> attempts;
    static string FilePath => Path.Combine(AppPaths.Root, "history.json");

    HistoryStore()
    {
        try { attempts = JsonSerializer.Deserialize<List<Attempt>>(File.ReadAllText(FilePath), AppPaths.Json) ?? []; }
        catch (Exception) { attempts = []; }
    }

    public IReadOnlyList<Attempt> For(string examId) => attempts.Where(a => a.ExamId == examId).OrderByDescending(a => a.Date).ToList();

    public void Add(SessionResult result)
    {
        attempts.Add(Attempt.From(result));
        try { AppPaths.WriteAtomically(FilePath, JsonSerializer.Serialize(attempts, AppPaths.Json)); } catch (Exception) { }
        Changed?.Invoke();
    }

    /// <summary>Topic accuracy pooled over every attempt, weakest first.</summary>
    public static List<TopicScore> WeakTopics(IEnumerable<Attempt> attempts)
    {
        var pooled = new Dictionary<string, TopicScore>();
        foreach (var s in attempts.SelectMany(a => a.Topics))
        {
            var t = pooled.GetValueOrDefault(s.Topic) ?? new TopicScore(s.Topic);
            pooled[s.Topic] = t with { Earned = t.Earned + s.Earned, Possible = t.Possible + s.Possible };
        }
        return pooled.Values.OrderBy(t => t.Fraction).ThenBy(t => t.Topic, StringComparer.Ordinal).ToList();
    }
}

/// <summary>Observable view of the on-disk library. All file work lives in LibraryStore.</summary>
public sealed class LibraryService
{
    public static LibraryService Shared { get; } = new();
    /// <summary>Raised after a reload; the argument is an exam id to select, if any.</summary>
    public event Action<string?>? Changed;

    public LibraryStore Store { get; } = new(Path.Combine(AppPaths.Root, "Library"));
    public List<Exam> Exams { get; private set; } = [];
    /// <summary>Exams that no longer load, as "name: reason".</summary>
    public List<string> Broken { get; private set; } = [];

    readonly FileSystemWatcher? watcher;
    DispatcherTimer? debounce;

    LibraryService()
    {
        Reload();
        try
        {
            // Reloads when anything is added to or removed from the library folder (e.g. mockiexam install).
            watcher = new FileSystemWatcher(Store.Directory) { IncludeSubdirectories = false, EnableRaisingEvents = true };
            watcher.Created += (_, _) => Schedule();
            watcher.Deleted += (_, _) => Schedule();
            watcher.Renamed += (_, _) => Schedule();
        }
        catch (Exception) { }
    }

    void Schedule() => Dispatcher.UIThread.Post(() =>
    {
        debounce?.Stop();
        debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        debounce.Tick += (_, _) => { debounce?.Stop(); Reload(); };
        debounce.Start();
    });

    public void Reload(string? select = null)
    {
        var contents = Store.Load();
        Exams = contents.Exams;
        Broken = contents.Broken;
        Changed?.Invoke(select);
    }

    /// <summary>Validates each exam (JSON file, folder, or .mockiexam package) and copies it in;
    /// returns one message per item that was rejected.</summary>
    public List<string> Import(IEnumerable<string> paths)
    {
        var failures = new List<string>();
        foreach (var path in paths)
        {
            try { Store.Install(path); }
            catch (Exception e) when (e is ExamLoadException or IOException or UnauthorizedAccessException)
            {
                var message = e.Message;
                if (message.Contains("File not found") && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    message += "\n\nThis exam uses images. Import the folder that contains exam.json and its images instead.";
                failures.Add($"{Path.GetFileName(path.TrimEnd('\\', '/'))}: {message}");
            }
        }
        Reload();
        return failures;
    }

    public void Remove(Exam exam)
    {
        try { Store.Remove(exam.Id); } catch (IOException) { }
        Reload();
    }
}
