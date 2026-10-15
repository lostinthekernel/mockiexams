using System.Text.Json;
using MockiCore;
using static MockiCore.Tests.T;

namespace MockiCore.Tests;

public class FormatTests
{
    [Fact]
    public void SamplesValidate()
    {
        var v1 = Decode(File.ReadAllText(Repo.Path("samples/sample.exam.json")));
        Assert.Equal(6, v1.Questions.Count);
        Assert.Equal(600, v1.TimeLimit);

        var baseDir = Repo.Path("samples/networking-sim");
        var (exam, issues) = ExamFormat.Validate(File.ReadAllBytes(Path.Combine(baseDir, "exam.json")), baseDir);
        Assert.Empty(issues);
        Assert.NotNull(exam);
        Assert.Equal(QuestionKinds.All.ToHashSet(), exam!.Questions.Select(q => q.Kind).ToHashSet());
        Assert.Equal(["images/server-rear.png", "images/switch-panel.png", "images/terminal.png", "images/topology.png"],
            exam.AssetPaths.OrderBy(p => p, StringComparer.Ordinal));
        Assert.Equal(new ScoreScale(100, 900, 720), exam.Scale);
    }

    [Fact]
    public void EveryTemplateIsValid()
    {
        var exam = Decode(Templates.Exam);
        Assert.Equal(QuestionKinds.All.Count - 1, exam.Questions.Count);
        foreach (var kind in QuestionKinds.All.Where(k => k != QuestionKind.Hotspot))
            Decode(ExamJson([Templates.Question(kind)]));
        // The hotspot template references an image, so check shape without a base folder.
        Decode(ExamJson([Templates.Question(QuestionKind.Hotspot)]));
    }

    [Fact]
    public void SchemaKindsMatchModel()
    {
        using var schema = JsonDocument.Parse(Templates.Schema);
        var question = schema.RootElement.GetProperty("$defs").GetProperty("question");
        var kinds = question.GetProperty("properties").GetProperty("kind").GetProperty("enum").EnumerateArray().Select(e => e.GetString()!).ToHashSet();
        Assert.Equal(QuestionKinds.All.Select(k => k.Raw()).ToHashSet(), kinds);
        Assert.Equal(KnownKeys.Question, question.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet());
        Assert.Equal(KnownKeys.Exam, schema.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet());
    }

    [Fact]
    public void DefaultsAndAnswerForms()
    {
        var exam = Decode(ExamJson([ChoiceQ(answer: """["a"]"""), ChoiceQ("q2", answer: "\"b\"")]));
        Assert.Equal(70, exam.PassPercent);
        Assert.Null(exam.TimeLimit);
        Assert.True(exam.AllowBack);
        Assert.Equal("General", exam.Questions[0].Topic);
        Assert.Equal(1, exam.Questions[0].Points);
        Assert.False(exam.Questions[0].PartialCredit);
    }

    [Fact]
    public void ReportsAllErrorsWithPaths()
    {
        var json = ExamJson([
            ChoiceQ("q1", answer: "\"z\""),
            ChoiceQ("q1", kind: "multiple", answer: "[]"),
            """{"id":"q3","kind":"essay","prompt":"P"}""",
        ], extra: ",\"passPercent\":150");
        var (_, issues) = ExamFormat.Validate(json);
        var paths = issues.Where(i => i.Severity == Severity.Error).Select(i => i.Path).ToHashSet();
        Assert.Equal(Set("passPercent", "questions[0].answer", "questions[1].id", "questions[1].answer", "questions[2].kind"), paths);
    }

    [Fact]
    public void RejectsMalformedQuestions()
    {
        AssertIssue("not json", "$", "Not valid JSON");
        AssertIssue(ExamJson([]), "questions", "no questions");
        AssertIssue(ExamJson([ChoiceQ(answer: """["a","b"]""")]), "questions[0].answer", "exactly one");
        AssertIssue(ExamJson([ChoiceQ(kind: "ordering", answer: """["a"]""")]), "questions[0].answer", "two or more");
        AssertIssue(ExamJson(["""{"id":"q","kind":"single","prompt":"P","choices":[{"id":"a","text":"A"}],"answer":"a"}"""]), "questions[0].choices", "at least 2");
        AssertIssue(ExamJson(["""{"id":"q","kind":"trueFalse","prompt":"P","answer":"maybe"}"""]), "questions[0].answer", "true or false");
        AssertIssue(ExamJson(["""{"id":"q","kind":"yesNo","prompt":"P","statements":[{"id":"s","text":"T"}]}"""]), "questions[0].statements[0].answer", "Yes");
        AssertIssue(ExamJson(["""{"id":"q","kind":"matching","prompt":"P","items":[{"id":"i","text":"I"}],"targets":[{"id":"t","text":"T"}],"answer":{"i":"nope"}}"""]), "questions[0].answer", "Unknown target");
        AssertIssue(ExamJson(["""{"id":"q","kind":"dropdowns","prompt":"P","rows":[{"id":"r","label":"L","options":["x","y"],"answer":"z"}]}"""]), "questions[0].rows[0].answer", "one of");
        AssertIssue(ExamJson(["""{"id":"q","kind":"fillIn","prompt":"P","blanks":[{"id":"b","pattern":"("}]}"""]), "questions[0].blanks[0].pattern", "regular expression");
        AssertIssue(ExamJson(["""{"id":"q","kind":"fillIn","prompt":"P","blanks":[{"id":"b"}]}"""]), "questions[0].blanks[0]", "accept");
        AssertIssue(ExamJson(["""{"id":"q","kind":"hotspot","prompt":"P","image":"../x.png","regions":[{"id":"r","x":0,"y":0,"width":0.5,"height":0.5}],"answer":["r"]}"""]), "questions[0].image", "relative path");
        AssertIssue(ExamJson(["""{"id":"q","kind":"hotspot","prompt":"P","image":"x.png","regions":[{"id":"r","x":0.8,"y":0,"width":0.5,"height":0.5}],"answer":["r"]}"""]), "questions[0].regions[0]", "inside the image");
        AssertIssue(ExamJson([ChoiceQ(extra: ",\"caseStudy\":\"nope\"")]), "questions[0].caseStudy", "No case study");
        AssertIssue(ExamJson([ChoiceQ(extra: ",\"exhibits\":[{\"title\":\"x\"}]")]), "questions[0].exhibits[0]", "exactly one");
        AssertIssue(ExamJson([ChoiceQ()], extra: ",\"formatVersion\":2"), "formatVersion", "format 2");
    }

    [Fact]
    public void MissingImageFileIsAnErrorOnlyWithABaseFolder()
    {
        var q = """{"id":"q","kind":"single","prompt":"P","exhibits":[{"image":"images/none.png"}],"choices":[{"id":"a","text":"A"},{"id":"b","text":"B"}],"answer":"a"}""";
        Assert.NotNull(ExamFormat.Validate(ExamJson([q])).Exam);
        var (exam, issues) = ExamFormat.Validate(ExamJson([q]), TempDir());
        Assert.Null(exam);
        Assert.Contains(issues, i => i.Message.Contains("File not found"));
    }

    [Fact]
    public void UnknownKeysAndDuplicatePromptsAreWarnings()
    {
        var json = ExamJson([ChoiceQ("q1", extra: ",\"answers\":[\"a\"]"), ChoiceQ("q2").Replace("P q2", "P q1")], extra: ",\"titel\":\"x\"");
        var (exam, issues) = ExamFormat.Validate(json);
        Assert.NotNull(exam);
        Assert.Equal(Set("titel", "questions[0].answers", "questions[1].prompt"), issues.Select(i => i.Path).ToHashSet());
        Assert.All(issues, i => Assert.Equal(Severity.Warning, i.Severity));
    }

    [Fact]
    public void Sections()
    {
        string[] qs = [ChoiceQ("q1"), ChoiceQ("q2"), ChoiceQ("q3")];
        var exam = Decode(ExamJson(qs, extra: ""","sections":[{"id":"s2","title":"Two","questions":["q3"]},{"id":"s1","questions":["q1"]}]"""));
        Assert.Equal(["q3", "q1"], exam.ExamQuestions.Select(q => q.Id));
        Assert.Equal(3, exam.Questions.Count);
        Assert.Equal(["Two", "s1"], exam.Sections.Select(s => s.Title));
        var (_, issues) = ExamFormat.Validate(ExamJson(qs, extra: ""","sections":[{"id":"s","questions":["q1"]}]"""));
        Assert.Contains(issues, i => i.Severity == Severity.Warning && i.Message.Contains("won't be asked"));

        AssertIssue(ExamJson(qs, extra: ""","sections":[{"id":"s","questions":["q1","zz"]}]"""), "sections[0].questions[1]", "No question");
        AssertIssue(ExamJson(qs, extra: ""","sections":[{"id":"a","questions":["q1"]},{"id":"b","questions":["q1"]}]"""), "sections[1].questions[0]", "already in section");
        AssertIssue(ExamJson(qs, extra: ""","sections":[{"id":"a","questions":[]}]"""), "sections[0].questions", "no questions");
    }

    [Fact]
    public void EncodeRoundTrips()
    {
        var baseDir = Repo.Path("samples/networking-sim");
        var bytes = File.ReadAllBytes(Path.Combine(baseDir, "exam.json"));
        var original = ExamFormat.ReadDocument(bytes)!;
        var again = ExamFormat.Decode(ExamFormat.Encode(original), baseDir);
        var first = ExamFormat.Decode(bytes, baseDir);
        Assert.Equal(Fingerprint(first), Fingerprint(again));
    }

    /// <summary>Everything about an exam, flattened, for structural equality.</summary>
    internal static string Fingerprint(Exam e) => JsonSerializer.Serialize(new
    {
        e.Id, e.Title, e.Summary, e.PassPercent, e.Scale, e.TimeLimit, e.AllowBack, TopicWeights = e.TopicWeights.OrderBy(kv => kv.Key, StringComparer.Ordinal), e.CaseStudies, e.Sections,
        Questions = e.Questions.Select(q => new { q.Id, q.Kind, q.Prompt, q.Topic, q.Explanation, q.Exhibits, q.CaseStudyId, q.Points, q.PartialCredit, Body = BodyText(q.Body) }),
    });

    internal static string BodyText(QuestionBody b) => b switch
    {
        SingleBody s => JsonSerializer.Serialize(new { s.Choices, s.Answer }),
        MultipleBody m => JsonSerializer.Serialize(new { m.Choices, Answer = m.Answer.Order() }),
        OrderingBody o => JsonSerializer.Serialize(new { o.Choices, o.Answer }),
        MatchingBody m => JsonSerializer.Serialize(new { m.Items, m.Targets, Answer = m.Answer.OrderBy(kv => kv.Key) }),
        DropdownsBody d => JsonSerializer.Serialize(d.Rows),
        FillInBody f => JsonSerializer.Serialize(f.Blanks),
        HotspotBody h => JsonSerializer.Serialize(new { h.Image, h.Regions, Answer = h.Answer.Order() }),
        _ => "",
    };
}

public class BlueprintTests
{
    [Fact]
    public void TopicWeights()
    {
        string[] qs = [ChoiceQ("q1", extra: ",\"topic\":\"A\""), ChoiceQ("q2", extra: ",\"topic\":\"B\",\"points\":3")];
        var exam = Decode(ExamJson(qs, extra: ""","topicWeights":{"A":40,"B":60}"""));
        Assert.Equal(40, exam.Weight("A"));
        var plain = Decode(ExamJson(qs));
        Assert.Equal(75, plain.Weight("B"));

        var (_, issues) = ExamFormat.Validate(ExamJson(qs, extra: ""","topicWeights":{"A":40,"C":10}"""));
        Assert.Contains(issues, i => i.Path == "topicWeights" && i.Message.Contains("not 100"));
        Assert.Contains(issues, i => i.Path == "topicWeights.C" && i.Severity == Severity.Warning);
        AssertIssue(ExamJson(qs, extra: ""","topicWeights":{"A":140}"""), "topicWeights.A", "0–100");
    }
}
