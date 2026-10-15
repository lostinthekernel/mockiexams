using MockiCore;
using static MockiCore.Tests.T;

namespace MockiCore.Tests;

public class SessionTests
{
    static readonly DateTime Start = DateTime.UnixEpoch;

    static Exam MakeExam(string extra = "", string[]? questions = null) => Decode(ExamJson(questions ??
    [
        ChoiceQ("s", answer: "\"b\"", extra: ",\"topic\":\"One\""),
        ChoiceQ("m", kind: "multiple", answer: """["a","c"]""", extra: ",\"topic\":\"One\""),
        """{"id":"o","kind":"ordering","topic":"Two","prompt":"P","choices":[{"id":"a","text":"A"},{"id":"b","text":"B"},{"id":"c","text":"C"},{"id":"d","text":"D"}],"answer":["a","b","c","d"]}""",
    ], extra: ",\"passPercent\":60,\"timeLimitMinutes\":2" + extra));

    static Session Make(Exam e, SessionMode mode = SessionMode.Exam, bool shuffle = false, ulong seed = 1,
        HashSet<string>? only = null, HashSet<string>? topics = null, int? limit = null) =>
        new(e, new SessionOptions { Mode = mode, ShuffleQuestions = shuffle, ShuffleChoices = shuffle, QuestionIds = only, Topics = topics, QuestionLimit = limit, Seed = seed }, Start);

    static void Go(Session s, string id) => s.Jump(s.Questions.ToList().FindIndex(q => q.Id == id));

    static string Order(Session s) => string.Join(",", s.Questions.Select(q => q.Id + ":" + FormatTests.BodyText(q.Body)));

    [Fact]
    public void GradesAndTopics()
    {
        var s = Make(MakeExam());
        Go(s, "s"); s.Choose("a"); s.Choose("b");
        Go(s, "m"); s.Choose("a"); s.Choose("b"); s.Choose("c"); s.Choose("b");
        Go(s, "o"); s.Respond(new OrderingResponse(["a", "b", "c", "d"]), s.CurrentQuestion);
        var r = s.Grade(Start.AddSeconds(30));
        Assert.Equal(3, r.CorrectCount);
        Assert.Equal(100, r.Percent);
        Assert.True(r.Passed);
        Assert.Equal(30, r.Duration);
        Assert.Equal([new TopicScore("One", 2, 2), new TopicScore("Two", 1, 1)], r.Topics);
    }

    [Fact]
    public void UnansweredAndEmptyResponses()
    {
        var s = Make(MakeExam());
        Go(s, "m"); s.Choose("a"); s.Choose("a");
        Assert.Null(s.Response(s.CurrentQuestion));
        s.Respond(new FillInResponse(new Dictionary<string, string> { ["x"] = "  " }), s.CurrentQuestion);
        Assert.Null(s.Response(s.CurrentQuestion));
        Assert.Equal(3, s.UnansweredCount);
        Assert.Equal(Set("s", "m", "o"), s.Grade().MissedIds.ToHashSet());
    }

    [Fact]
    public void ShuffleIsSeededAndKeepsAnswers()
    {
        var e = MakeExam();
        Assert.Equal(Order(Make(e, shuffle: true, seed: 42)), Order(Make(e, shuffle: true, seed: 42)));
        var orders = Enumerable.Range(0, 20).Select(seed => FormatTests.BodyText(Make(e, shuffle: true, seed: (ulong)seed).Questions.First(q => q.Id == "s").Body)).ToHashSet();
        Assert.True(orders.Count > 1);
        for (ulong seed = 0; seed < 20; seed++)
        {
            var s = Make(e, shuffle: true, seed: seed);
            Go(s, "s"); s.Choose("b");
            Assert.True(s.CurrentQuestion.IsCorrect(s.Response(s.CurrentQuestion)));
        }
    }

    [Fact]
    public void OrderingPoolAlwaysShuffledButTrueFalseNever()
    {
        var e = MakeExam(questions:
        [
            """{"id":"o","kind":"ordering","prompt":"P","choices":[{"id":"a","text":"A"},{"id":"b","text":"B"},{"id":"c","text":"C"},{"id":"d","text":"D"}],"answer":["a","b","c","d"]}""",
            """{"id":"tf","kind":"trueFalse","prompt":"P","answer":true}""",
        ]);
        var pools = Enumerable.Range(0, 10).Select(seed =>
            string.Join(",", ((OrderingBody)Make(e, shuffle: false, seed: (ulong)seed).Questions[0].Body).Choices.Select(c => c.Id))).ToHashSet();
        Assert.True(pools.Count > 1);
        for (ulong seed = 0; seed < 10; seed++)
        {
            var body = (SingleBody)Make(e, shuffle: true, seed: seed).Questions.First(q => q.Id == "tf").Body;
            Assert.Equal(["true", "false"], body.Choices.Select(c => c.Id));
        }
    }

    [Fact]
    public void TimerOnlyInExamMode()
    {
        var e = MakeExam();
        var s = Make(e, SessionMode.Exam);
        Assert.Equal(90, s.Remaining(Start.AddSeconds(30)));
        Assert.False(s.IsExpired(Start.AddSeconds(119)));
        Assert.True(s.IsExpired(Start.AddSeconds(121)));
        Assert.Null(Make(e, SessionMode.Practice).Remaining(Start.AddSeconds(1000)));
    }

    [Fact]
    public void PracticeRevealLocks()
    {
        var e = MakeExam();
        var s = Make(e, SessionMode.Practice);
        Go(s, "s"); s.Choose("a"); s.Reveal(); s.Choose("b");
        Assert.Equal(new SingleResponse("a"), s.Response(s.CurrentQuestion));
        var x = Make(e, SessionMode.Exam);
        Go(x, "s"); x.Choose("a"); x.Reveal(); x.Choose("b");
        Assert.Equal(new SingleResponse("b"), x.Response(x.CurrentQuestion));
    }

    [Fact]
    public void Filters()
    {
        var e = MakeExam();
        Assert.Equal(["m"], Make(e, only: Set("m")).Questions.Select(q => q.Id));
        Assert.Equal(3, Make(e, only: Set("gone")).Questions.Count);
        Assert.Equal(["o"], Make(e, topics: Set("Two")).Questions.Select(q => q.Id));
        var limited = Make(e, seed: 7, limit: 2);
        Assert.Equal(2, limited.Questions.Count);
        string[] order = ["s", "m", "o"];
        var ids = limited.Questions.Select(q => q.Id).ToList();
        Assert.Equal(ids.OrderBy(id => Array.IndexOf(order, id)), ids);
    }

    [Fact]
    public void NoBackNavigation()
    {
        var s = Make(MakeExam(extra: ",\"allowBack\":false"));
        s.Next(); s.Next();
        Assert.Equal(2, s.Current);
        s.Previous(); s.Jump(0);
        Assert.Equal(2, s.Current);
        Assert.False(s.CanJump(1));
        var p = Make(MakeExam(extra: ",\"allowBack\":false"), SessionMode.Practice);
        p.Next(); p.Previous();
        Assert.Equal(0, p.Current);
    }

    [Fact]
    public void CaseStudiesStayTogether()
    {
        var qs = Enumerable.Range(1, 6).Select(n => ChoiceQ($"q{n}", extra: n is >= 3 and <= 5 ? ",\"caseStudy\":\"cs\"" : "")).ToArray();
        var e = MakeExam(extra: ""","caseStudies":[{"id":"cs","scenario":"S"}]""", questions: qs);
        for (ulong seed = 0; seed < 30; seed++)
        {
            var ids = Make(e, shuffle: true, seed: seed).Questions.Select(q => q.Id).ToList();
            var i = ids.IndexOf("q3");
            Assert.Equal(["q3", "q4", "q5"], ids.Skip(i).Take(3));
        }
    }

    [Fact]
    public void SectionsOrderAndShuffle()
    {
        var qs = Enumerable.Range(1, 6).Select(n => ChoiceQ($"q{n}")).ToArray();
        var e = MakeExam(extra: ""","sections":[{"id":"b","title":"Second","questions":["q4","q5","q6"],"shuffle":false},{"id":"a","title":"First","questions":["q1","q2","q3"]}]""", questions: qs);
        for (ulong seed = 0; seed < 20; seed++)
        {
            var s = Make(e, shuffle: true, seed: seed);
            var ids = s.Questions.Select(q => q.Id).ToList();
            Assert.Equal(["q4", "q5", "q6"], ids.Take(3));
            Assert.Equal(Set("q1", "q2", "q3"), ids.Skip(3).ToHashSet());
            Assert.Equal("Second", s.SectionStart(0));
            Assert.Null(s.SectionStart(1));
            Assert.Equal("First", s.SectionStart(3));
        }
    }

    [Fact]
    public void HotspotSingleTargetActsLikeRadio()
    {
        var e = Decode(ExamJson([Templates.Question(QuestionKind.Hotspot)]));
        var s = Make(e);
        s.ToggleRegion("switch"); s.ToggleRegion("router");
        Assert.Equal(new HotspotResponse(Set("router")), s.Response(s.CurrentQuestion));
        s.ToggleRegion("router");
        Assert.Null(s.Response(s.CurrentQuestion));
    }
}
