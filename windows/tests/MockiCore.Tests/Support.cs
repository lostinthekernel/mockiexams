using System.Runtime.CompilerServices;
using MockiCore;

namespace MockiCore.Tests;

static class Repo
{
    /// <summary>The repository root: the folder above windows/ that holds samples/.</summary>
    public static string Root
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !Directory.Exists(System.IO.Path.Combine(dir, "samples"))) dir = System.IO.Path.GetDirectoryName(dir);
            return dir ?? throw new InvalidOperationException("samples/ not found");
        }
    }

    public static string Path(string relative) => System.IO.Path.Combine(Root, relative);
}

static class T
{
    /// <summary>Builds exam JSON from question JSON fragments.</summary>
    public static string ExamJson(IEnumerable<string> questions, string extra = "") =>
        $$"""{"id":"t","title":"T"{{extra}},"questions":[{{string.Join(",", questions)}}]}""";

    public static string ChoiceQ(string id = "q1", string kind = "single", string answer = "\"a\"", string extra = "") =>
        $$"""{"id":"{{id}}","kind":"{{kind}}","prompt":"P {{id}}","choices":[{"id":"a","text":"A"},{"id":"b","text":"B"},{"id":"c","text":"C"}],"answer":{{answer}}{{extra}}}""";

    public static Exam Decode(string json)
    {
        try { return ExamFormat.Decode(json); }
        catch (ExamLoadException e)
        {
            Assert.Fail("unexpected issues:\n" + string.Join("\n", e.Issues));
            throw;
        }
    }

    public static void AssertIssue(string json, string path, string containing)
    {
        var (exam, issues) = ExamFormat.Validate(json);
        Assert.Null(exam);
        Assert.True(issues.Any(i => i.Severity == Severity.Error && i.Path == path && i.Message.Contains(containing)),
            $"no error at {path} containing \"{containing}\". Got:\n{string.Join("\n", issues)}");
    }

    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mocki-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static HashSet<string> Set(params string[] items) => [.. items];
}
