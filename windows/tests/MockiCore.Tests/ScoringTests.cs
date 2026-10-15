using MockiCore;
using static MockiCore.Tests.T;

namespace MockiCore.Tests;

public class ScoringTests
{
    static Question Q(QuestionKind kind, bool partial = false)
    {
        var json = Templates.Question(kind);
        if (partial)
        {
            var at = json.IndexOf("\"id\":", StringComparison.Ordinal);
            json = json[..at] + "\"partialCredit\": true, " + json[at..];
        }
        return Decode(ExamJson([json])).Questions[0];
    }

    static Dictionary<string, string> D(params (string, string)[] pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);

    [Fact]
    public void FullMarksForEachKind()
    {
        (QuestionKind, Response)[] cases =
        [
            (QuestionKind.Single, new SingleResponse("a")),
            (QuestionKind.Multiple, new MultipleResponse(Set("a", "c"))),
            (QuestionKind.TrueFalse, new SingleResponse("false")),
            (QuestionKind.YesNo, new DropdownsResponse(D(("s1", "Yes"), ("s2", "No")))),
            (QuestionKind.Ordering, new OrderingResponse(["d", "o", "r", "a"])),
            (QuestionKind.Matching, new MatchingResponse(D(("ssh", "22"), ("rdp", "3389"), ("ftp", "21")))),
            (QuestionKind.Dropdowns, new DropdownsResponse(D(("action", "permit"), ("port", "443")))),
            (QuestionKind.FillIn, new FillInResponse(D(("cmd", "  SH   ip route ")))),
            (QuestionKind.Hotspot, new HotspotResponse(Set("router"))),
        ];
        foreach (var (kind, response) in cases)
        {
            var q = Q(kind);
            Assert.True(q.IsCorrect(response), kind.ToString());
            Assert.Equal(1, q.Score(response));
            Assert.False(q.IsCorrect(null));
            Assert.NotEqual("(unanswered)", q.Describe(response));
            Assert.NotEmpty(q.DescribeAnswer());
        }
    }

    [Fact]
    public void AllOrNothingByDefault()
    {
        var q = Q(QuestionKind.Multiple);
        Assert.Equal(0, q.Score(new MultipleResponse(Set("a"))));
        Assert.Equal(0.5, q.Fraction(new MultipleResponse(Set("a"))));
    }

    [Fact]
    public void PartialCredit()
    {
        Assert.Equal(0, Q(QuestionKind.Multiple, true).Score(new MultipleResponse(Set("a", "b"))));
        Assert.Equal(0.5, Q(QuestionKind.Multiple, true).Score(new MultipleResponse(Set("a"))));
        Assert.Equal(0.5, Q(QuestionKind.Ordering, true).Score(new OrderingResponse(["d", "o", "a", "r"])));
        Assert.Equal(0.8, Q(QuestionKind.Ordering, true).Score(new OrderingResponse(["d", "o", "r", "a", "x"])));
        Assert.Equal(1.0 / 3, Q(QuestionKind.Matching, true).Score(new MatchingResponse(D(("ssh", "22"), ("rdp", "21")))), 9);
        Assert.Equal(0.5, Q(QuestionKind.YesNo, true).Score(new DropdownsResponse(D(("s1", "Yes")))));
    }

    [Fact]
    public void MatchingDistractorsMustStayUnplaced()
    {
        var q = Decode(ExamJson(["""{"id":"m","kind":"matching","prompt":"P","items":[{"id":"a","text":"A"},{"id":"x","text":"X"}],"targets":[{"id":"t","text":"T"}],"answer":{"a":"t"}}"""])).Questions[0];
        Assert.True(q.IsCorrect(new MatchingResponse(D(("a", "t")))));
        Assert.False(q.IsCorrect(new MatchingResponse(D(("a", "t"), ("x", "t")))));
    }

    [Fact]
    public void FillInMatching()
    {
        var blank = new Blank("b", null, ["show ip route"], false, "sh(o(w)?)? ip ro(u(te?)?)?");
        Assert.True(blank.Accepts("SHOW IP ROUTE"));
        Assert.True(blank.Accepts("sh ip ro"));
        Assert.False(blank.Accepts("show ip"));
        Assert.False(blank.Accepts("   "));
        var strict = new Blank("b", null, ["Admin"], true, null);
        Assert.False(strict.Accepts("admin"));
        Assert.True(strict.Accepts(" Admin "));
    }

    [Fact]
    public void ScaledScoreAndPoints()
    {
        var exam = Decode(ExamJson([ChoiceQ("q1", extra: ",\"points\":3"), ChoiceQ("q2")], extra: ""","scale":{"min":100,"max":900,"pass":700}"""));
        var s = new Session(exam, new SessionOptions { ShuffleQuestions = false, Seed = 1 });
        s.Jump(s.Questions.ToList().FindIndex(q => q.Id == "q1"));
        s.Choose("a");
        var r = s.Grade();
        Assert.Equal(3, r.Earned);
        Assert.Equal(4, r.Possible);
        Assert.Equal(75, r.Percent);
        Assert.Equal(700, r.ScaledScore);
        Assert.True(r.Passed);
    }
}
