using MockiCore;
using static MockiCore.Tests.T;

namespace MockiCore.Tests;

public class ConverterTests
{
    static (Exam, Conversion) Convert(string file, SourceFormat format)
    {
        var text = File.ReadAllText(Repo.Path($"samples/converters/{file}"));
        Assert.Equal(format, SourceFormats.Detect(file, text));
        var c = Converter.Convert(text, format, "conv", "Converted");
        return (ExamFormat.Decode(ExamFormat.Encode(c.Document)), c);
    }

    static Dictionary<string, string> D(params (string, string)[] pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);

    [Fact]
    public void Aiken()
    {
        var (exam, c) = Convert("sample.aiken.txt", SourceFormat.Aiken);
        Assert.Equal([QuestionKind.Single, QuestionKind.Multiple], exam.Questions.Select(q => q.Kind));
        Assert.True(exam.Questions[0].IsCorrect(new SingleResponse("b")));
        Assert.True(exam.Questions[1].IsCorrect(new MultipleResponse(Set("a", "c"))));
        Assert.Single(c.Skipped);
    }

    [Fact]
    public void Gift()
    {
        var (exam, c) = Convert("sample.gift", SourceFormat.Gift);
        Assert.Equal(["dns-port", "rfc1918", "udp-reliable", "show-run", "match-ports", "hosts-26"], exam.Questions.Select(q => q.Id));
        Assert.Equal([QuestionKind.Single, QuestionKind.Multiple, QuestionKind.TrueFalse, QuestionKind.FillIn, QuestionKind.Matching, QuestionKind.FillIn], exam.Questions.Select(q => q.Kind));
        Assert.Equal(["Ports", "Ports", "Concepts", "Concepts", "Concepts", "Concepts"], exam.Questions.Select(q => q.Topic));
        Assert.Equal("DNS uses 53/UDP, and TCP for large responses.", exam.Questions[0].Explanation);
        Assert.True(exam.Questions[0].IsCorrect(new SingleResponse("a")));
        Assert.True(exam.Questions[1].IsCorrect(new MultipleResponse(Set("a", "b"))));
        Assert.True(exam.Questions[2].IsCorrect(new SingleResponse("false")));
        Assert.True(exam.Questions[3].IsCorrect(new FillInResponse(D(("answer", "show run")))));
        Assert.True(exam.Questions[4].IsCorrect(new MatchingResponse(D(("i1", "t1"), ("i2", "t2"), ("i3", "t3")))));
        Assert.True(exam.Questions[5].IsCorrect(new FillInResponse(D(("answer", "62")))));
        Assert.Equal(["Skipped: essay questions have no answer key"], c.Skipped.Select(s => s.Message));
    }

    [Fact]
    public void Csv()
    {
        var (exam, c) = Convert("sample.csv", SourceFormat.Csv);
        Assert.Equal(["ssh-port", "private", "tcp-reliable", "show-ver", "by-text"], exam.Questions.Select(q => q.Id));
        Assert.Equal([QuestionKind.Single, QuestionKind.Multiple, QuestionKind.TrueFalse, QuestionKind.FillIn, QuestionKind.Single], exam.Questions.Select(q => q.Kind));
        Assert.True(exam.Questions[0].IsCorrect(new SingleResponse("b")));
        Assert.True(exam.Questions[1].IsCorrect(new MultipleResponse(Set("a", "c"))));
        Assert.True(exam.Questions[3].IsCorrect(new FillInResponse(D(("answer", "sh ver")))));
        Assert.True(exam.Questions[4].IsCorrect(new SingleResponse("b")));
        Assert.Equal("10/8, 172.16/12 and 192.168/16.", exam.Questions[1].Explanation);
        Assert.Single(c.Skipped);
        Assert.Equal("line 7", c.Skipped[0].Path);
    }

    [Fact]
    public void CsvQuotingAndDelimiters()
    {
        var rows = MockiCore.Csv.Parse("a;b\n\"x;1\";\"say \"\"hi\"\"\nthere\"\n");
        Assert.Equal([["a", "b"], ["x;1", "say \"hi\"\nthere"]], rows);
    }
}

public class LibraryTests
{
    [Fact]
    public void InstallFolderWithImagesAndReplaceById()
    {
        var store = new LibraryStore(TempDir());
        var exam = store.Install(Repo.Path("samples/networking-sim"));
        var folder = Path.Combine(store.Directory, "sample-networking-sim");
        Assert.Equal(folder, exam.AssetBase);
        foreach (var path in exam.AssetPaths) Assert.True(File.Exists(Path.Combine(folder, path)), path);

        store.Install(Repo.Path("samples/networking-sim/exam.json"));
        var contents = store.Load();
        Assert.Equal(["sample-networking-sim"], contents.Exams.Select(e => e.Id));
        Assert.Empty(contents.Broken);

        store.Remove(exam.Id);
        Assert.Empty(store.Load().Exams);
    }

    [Fact]
    public void InstallRejectsInvalidAndMissingImages()
    {
        var store = new LibraryStore(TempDir());
        var src = TempDir();
        File.WriteAllText(Path.Combine(src, "exam.json"), """{"id":"x","title":"X","questions":[{"id":"q","kind":"hotspot","prompt":"P","image":"img/a.png","regions":[{"id":"r","x":0,"y":0,"width":1,"height":1}],"answer":["r"]}]}""");
        var e = Assert.Throws<ExamLoadException>(() => store.Install(src));
        Assert.Contains("File not found", e.Message);
        Assert.Empty(store.Load().Exams);
    }

    [Fact]
    public void MigratesFlatV1Files()
    {
        var store = new LibraryStore(TempDir());
        File.Copy(Repo.Path("samples/sample.exam.json"), Path.Combine(store.Directory, "sample-networking-basics.exam.json"));
        var contents = store.Load();
        Assert.Equal(["sample-networking-basics"], contents.Exams.Select(e => e.Id));
        Assert.Equal(["sample-networking-basics"], Directory.GetFileSystemEntries(store.Directory).Select(Path.GetFileName));
        Assert.Single(store.Load().Exams);
    }
}
