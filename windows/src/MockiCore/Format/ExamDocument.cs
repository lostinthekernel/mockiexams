using System.Text.Json;
using System.Text.Json.Serialization;

namespace MockiCore;

// The on-disk JSON shape, field for field. Everything is optional here so the validator can
// report every problem at once; ExamFormat turns a document into a checked Exam.
// Property names match the JSON keys through JsonNamingPolicy.CamelCase (see ExamFormat.Options).

public sealed class ExamDocument
{
    public int? FormatVersion { get; set; }
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public int? PassPercent { get; set; }
    public ScaleDocument? Scale { get; set; }
    public double? TimeLimitMinutes { get; set; }
    public bool? AllowBack { get; set; }
    public bool? PartialCredit { get; set; }
    /// <summary>Target share of the exam per topic, in percent (the exam blueprint).</summary>
    public Dictionary<string, double>? TopicWeights { get; set; }
    public List<CaseStudyDocument>? CaseStudies { get; set; }
    /// <summary>Assembles the exam from the bank; questions in no section are not asked.</summary>
    public List<SectionDocument>? Sections { get; set; }
    public List<QuestionDocument>? Questions { get; set; }

    public ExamDocument() { }

    public ExamDocument(string id, string title, List<QuestionDocument> questions)
    {
        FormatVersion = ExamFormat.CurrentVersion;
        Id = id;
        Title = title;
        Questions = questions;
    }

    public ExamDocument Clone() => JsonSerializer.Deserialize<ExamDocument>(JsonSerializer.Serialize(this, ExamFormat.Options), ExamFormat.Options)!;
}

public sealed class ScaleDocument
{
    public int Min { get; set; }
    public int Max { get; set; }
    public int Pass { get; set; }
}

public sealed class SectionDocument
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public List<string>? Questions { get; set; }
    public bool? Shuffle { get; set; }
}

public sealed class CaseStudyDocument
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Scenario { get; set; }
    public List<ExhibitDocument>? Exhibits { get; set; }
}

public sealed class ExhibitDocument
{
    public string? Title { get; set; }
    public string? Text { get; set; }
    public string? Image { get; set; }
}

public sealed class QuestionDocument
{
    public string? Id { get; set; }
    public string? Kind { get; set; }
    public string? Topic { get; set; }
    public string? Prompt { get; set; }
    public List<ExhibitDocument>? Exhibits { get; set; }
    public string? CaseStudy { get; set; }
    public double? Points { get; set; }
    public bool? PartialCredit { get; set; }
    public List<ChoiceDocument>? Choices { get; set; }
    public List<ChoiceDocument>? Items { get; set; }
    public List<ChoiceDocument>? Targets { get; set; }
    public List<RowDocument>? Rows { get; set; }
    public List<StatementDocument>? Statements { get; set; }
    public List<BlankDocument>? Blanks { get; set; }
    public string? Image { get; set; }
    public List<RegionDocument>? Regions { get; set; }
    public AnswerValue? Answer { get; set; }
    public string? Explanation { get; set; }

    public QuestionDocument() { }

    public QuestionDocument(string id, QuestionKind kind, string prompt)
    {
        Id = id;
        Kind = kind.Raw();
        Prompt = prompt;
    }
}

public sealed class ChoiceDocument
{
    public string? Id { get; set; }
    public string? Text { get; set; }
    public ChoiceDocument() { }
    public ChoiceDocument(string id, string text) { Id = id; Text = text; }
}

public sealed class RowDocument
{
    public string? Id { get; set; }
    public string? Label { get; set; }
    public List<string>? Options { get; set; }
    public string? Answer { get; set; }
}

public sealed class StatementDocument
{
    public string? Id { get; set; }
    public string? Text { get; set; }
    public bool? Answer { get; set; }
}

public sealed class BlankDocument
{
    public string? Id { get; set; }
    public string? Label { get; set; }
    public List<string>? Accept { get; set; }
    public bool? CaseSensitive { get; set; }
    public string? Pattern { get; set; }
    public BlankDocument() { }
    public BlankDocument(string id, List<string> accept) { Id = id; Accept = accept; }
}

public sealed class RegionDocument
{
    public string? Id { get; set; }
    public string? Label { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
}

/// <summary>answer is a string, a list of strings, a boolean, or an object, depending on the kind.</summary>
[JsonConverter(typeof(AnswerValueConverter))]
public abstract record AnswerValue
{
    public sealed record String(string Value) : AnswerValue;
    public sealed record Strings(List<string> Value) : AnswerValue
    {
        public bool Equals(Strings? other) => other is not null && Value.SequenceEqual(other.Value);
        public override int GetHashCode() => Value.Count;
    }
    public sealed record Bool(bool Value) : AnswerValue;
    public sealed record Map(Dictionary<string, string> Value) : AnswerValue
    {
        public bool Equals(Map? other) => other is not null && Maps.Same(Value, other.Value);
        public override int GetHashCode() => Value.Count;
    }

    internal List<string>? AsList => this switch
    {
        String s => [s.Value],
        Strings l => l.Value,
        _ => null,
    };

    public IReadOnlyList<string> Ids => AsList ?? [];
}

sealed class AnswerValueConverter : JsonConverter<AnswerValue>
{
    public override AnswerValue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return new AnswerValue.String(reader.GetString()!);
            case JsonTokenType.True:
            case JsonTokenType.False:
                return new AnswerValue.Bool(reader.GetBoolean());
            case JsonTokenType.StartArray:
                var list = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType != JsonTokenType.String) throw Mismatch();
                    list.Add(reader.GetString()!);
                }
                return new AnswerValue.Strings(list);
            case JsonTokenType.StartObject:
                var map = new Dictionary<string, string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var key = reader.GetString()!;
                    reader.Read();
                    if (reader.TokenType != JsonTokenType.String) throw Mismatch();
                    map[key] = reader.GetString()!;
                }
                return new AnswerValue.Map(map);
            default:
                throw Mismatch();
        }
    }

    static JsonException Mismatch() =>
        new("answer must be a string, an array of strings, a boolean, or an object of strings");

    public override void Write(Utf8JsonWriter writer, AnswerValue value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case AnswerValue.String s: writer.WriteStringValue(s.Value); break;
            case AnswerValue.Bool b: writer.WriteBooleanValue(b.Value); break;
            case AnswerValue.Strings l:
                writer.WriteStartArray();
                foreach (var v in l.Value) writer.WriteStringValue(v);
                writer.WriteEndArray();
                break;
            case AnswerValue.Map m:
                writer.WriteStartObject();
                foreach (var kv in m.Value) writer.WriteString(kv.Key, kv.Value);
                writer.WriteEndObject();
                break;
        }
    }
}

/// <summary>Keys allowed at each level; anything else is reported as a likely typo.</summary>
static class KnownKeys
{
    public static readonly HashSet<string> Exam = ["formatVersion", "id", "title", "description", "passPercent", "scale", "timeLimitMinutes", "allowBack", "partialCredit", "topicWeights", "caseStudies", "sections", "questions", "$schema"];
    public static readonly HashSet<string> Section = ["id", "title", "questions", "shuffle"];
    public static readonly HashSet<string> CaseStudy = ["id", "title", "scenario", "exhibits"];
    public static readonly HashSet<string> Exhibit = ["title", "text", "image"];
    public static readonly HashSet<string> Question = ["id", "kind", "topic", "prompt", "exhibits", "caseStudy", "points", "partialCredit", "choices", "items", "targets", "rows", "statements", "blanks", "image", "regions", "answer", "explanation"];
    public static readonly HashSet<string> Choice = ["id", "text"];
    public static readonly HashSet<string> Row = ["id", "label", "options", "answer"];
    public static readonly HashSet<string> Statement = ["id", "text", "answer"];
    public static readonly HashSet<string> Blank = ["id", "label", "accept", "caseSensitive", "pattern"];
    public static readonly HashSet<string> Region = ["id", "label", "x", "y", "width", "height"];
    public static readonly HashSet<string> Scale = ["min", "max", "pass"];
}
