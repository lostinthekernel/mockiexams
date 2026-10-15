using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MockiCore;

namespace MockiExams;

/// <summary>Create a question in steps. Each step is checked before moving on, so new questions start valid.</summary>
public sealed class CreateQuestionFlow : UserControl
{
    static readonly string[] Steps = ["Question type", "Prompt and exhibits", "Answer", "Scoring", "Review and create"];
    static readonly string[] AnswerKeys = ["choices", "items", "targets", "rows", "statements", "blanks", "image", "regions", "answer"];

    readonly DesignerWindow window;
    readonly Action<int> onCreate;
    readonly Action close;
    int step;
    QuestionDocument? draft;
    readonly ContentControl title = new(), stepList = new(), body = new(), issuesBar = new(), footer = new();

    public static void Show(DesignerWindow window, Action<int> onCreate)
    {
        var host = ModalHost.Of(window);
        if (host is null) return;
        Sheet? sheet = null;
        var flow = new CreateQuestionFlow(window, onCreate, () => sheet?.Close());
        var size = window.Bounds.Size;
        sheet = host.Present(flow, Math.Min(1040, size.Width - 40), Math.Min(720, size.Height - 40), () => sheet?.Close());
    }

    CreateQuestionFlow(DesignerWindow window, Action<int> onCreate, Action close)
    {
        this.window = window;
        this.onCreate = onCreate;
        this.close = close;
        var right = UI.Rows("Auto,*,Auto,Auto,Auto", title, body, issuesBar, UI.Divider(), footer);
        right.Background = Palette.ConsolePage;
        Content = UI.Columns("220,Auto,*",
            new Border { Background = Palette.ConsolePanel, Child = stepList },
            UI.Divider(true),
            right);
        RenderAll();
    }

    ProjectDocument Doc => window.Document;

    void RenderAll()
    {
        title.Content = UI.V(4, UI.Caption("Create question"), UI.Txt(Steps[step], 21, FontWeight.SemiBold)).Pad(22, 18, 22, 8);
        stepList.Content = StepList();
        body.Content = StepContent();
        RefreshChecks();
    }

    /// <summary>Re-checks the draft after an edit; the step content keeps focus.</summary>
    void RefreshChecks()
    {
        stepList.Content = StepList();
        var issues = step == 0 ? [] : IssuesFor(step);
        issuesBar.Content = issues.Count == 0 ? null : new Border
        {
            Background = Palette.ErrSoft, Padding = new Thickness(22, 8),
            Child = UI.VList(4, issues.Take(4).Select(i => UI.H(6, Icons.Make("xmark.circle.fill", 14, Palette.Err), UI.Txt(Describe(i), 12.5, color: Palette.Err)))),
        };
        footer.Content = Footer(issues.Count);
    }

    Control StepList()
    {
        var v = new StackPanel { Spacing = 2, Margin = new Thickness(16) };
        for (var i = 0; i < Steps.Length; i++)
        {
            var index = i;
            var done = i < step;
            var current = i == step;
            var circle = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1.5),
                BorderBrush = i <= step ? Palette.Brand : Palette.Secondary, Background = current ? Palette.Brand : Brushes.Transparent,
                Child = done ? Icons.Make("checkmark", 12, Palette.Brand).Center() : UI.Txt($"{i + 1}", 11.5, FontWeight.Bold, current ? Brushes.White : Palette.Secondary).Center(),
            };
            var row = UI.Columns("Auto,*", circle.Pad(0, 0, 10, 0).Do(c => c.VerticalAlignment = VerticalAlignment.Top),
                UI.V(1, UI.Txt(Steps[i], 13, current ? FontWeight.SemiBold : FontWeight.Normal, i <= step ? Palette.Text : Palette.Secondary),
                    Summary(i) is { } s ? UI.Caption(s).Do(t => { t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis; }) : null)).Pad(0, 7);
            if (CanJump(index)) row.OnTap(() => { step = index; RenderAll(); });
            v.Children.Add(row);
        }
        return v;
    }

    string? Summary(int i)
    {
        if (draft is null) return null;
        return i switch
        {
            0 => draft.KindValue()?.Label(),
            1 => draft.Exhibits is { } e ? $"{e.Count} exhibit{(e.Count == 1 ? "" : "s")}" : null,
            3 => $"{UI.Points(draft.Points ?? 1)} pt{(draft.Points is null or 1 ? "" : "s")}",
            _ => null,
        };
    }

    bool CanJump(int i) => i <= step || (draft is not null && Enumerable.Range(step, i - step).All(s => IssuesFor(s).Count == 0));

    Control Footer(int stepIssues) => UI.Bar(
        [UI.Btn("Cancel", close)],
        [step > 0 ? UI.Btn("Previous", () => { step--; RenderAll(); }) : null,
         step < Steps.Length - 1
            ? UI.Btn($"Next: {Steps[step + 1]}", () => { step++; RenderAll(); }, ButtonKind.Prominent, Palette.BrandFill).Disabled(draft is null || stepIssues > 0)
            : UI.Btn("Create Question", Create, ButtonKind.Prominent, Palette.BrandFill).Disabled(AllIssues().Count > 0)]).Pad(14, 14);

    void Mutate(Action change, string? key = null)
    {
        change();
        RefreshChecks();
    }

    Control StepContent()
    {
        if (step == 0) return TypePicker();
        if (draft is not { } q) return new Panel();
        Control content = step switch
        {
            1 => UI.V(18,
                UI.FormSection("Question", [
                    UI.Field(q.Id ?? "", t => Mutate(() => q.Id = t.Length == 0 ? null : t), "Id", mono: true),
                    Editors.TopicField(q, Doc, Mutate),
                    (Doc.Exam.CaseStudies ?? []).Count == 0 ? null : UI.LabeledRow("Case study", UI.Picker(
                        new[] { ("", "None") }.Concat((Doc.Exam.CaseStudies ?? []).Select(c => (c.Id ?? "", c.Title ?? c.Id ?? ""))).ToList(),
                        q.CaseStudy ?? "", v => Mutate(() => q.CaseStudy = v.Length == 0 ? null : v), 240)),
                    UI.TextArea("Prompt (Markdown; ``` for code blocks)", q.Prompt ?? "", t => Mutate(() => q.Prompt = t), 120),
                ]),
                new ExhibitsEditor(() => q.Exhibits, v => q.Exhibits = v, Doc, Mutate, () => TopLevel.GetTopLevel(this))),
            2 => UI.V(18,
                UI.FormSection(null, [UI.Secondary(InstructionText)]),
                new KindEditor(q, Doc, Mutate, () => TopLevel.GetTopLevel(this))),
            3 => UI.V(18,
                UI.FormSection("Scoring", [
                    UI.LabeledRow("Points", UI.Field(SwiftJson.Number(q.Points ?? 1), t =>
                    {
                        if (double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var p)) Mutate(() => q.Points = p == 1 ? null : p);
                    }, width: 90)),
                    UI.LabeledRow("Partial credit", UI.Picker(
                        [(-1, $"Exam default ({(Doc.Exam.PartialCredit == true ? "on" : "off")})"), (1, "On: award a share for partly correct answers"), (0, "Off: all or nothing")],
                        q.PartialCredit is { } pc ? (pc ? 1 : 0) : -1, v => Mutate(() => q.PartialCredit = v == -1 ? null : v == 1), 330)),
                ]),
                UI.FormSection("Explanation", [UI.TextArea("Shown after checking in practice, and in the review", q.Explanation ?? "", t => Mutate(() => q.Explanation = t.Length == 0 ? null : t), 100)])),
            _ => UI.FormSection("Summary", [
                UI.LabeledText("Id", q.Id ?? ""),
                UI.LabeledText("Kind", q.KindValue()?.Label() ?? ""),
                UI.LabeledText("Topic", ProjectDocument.NonEmpty(q.Topic) ?? "General"),
                UI.LabeledText("Points", UI.Points(q.Points ?? 1)),
                UI.LabeledText("Exhibits", $"{q.Exhibits?.Count ?? 0}"),
                Markdown.Inline(q.Prompt ?? "").Do(t => t.MaxLines = 4),
                AllIssues().Count == 0 ? UI.H(6, Icons.Make("checkmark.circle.fill", 15, Palette.Ok), UI.Txt("Ready to add to the bank.", color: Palette.Ok)) : null,
            ]),
        };
        return UI.Scroll(content.Pad(22, 4, 22, 18));
    }

    Control TypePicker()
    {
        var wrap = new WrapPanel { Margin = new Thickness(22, 8) };
        foreach (var kind in QuestionKinds.All)
        {
            var chosen = draft?.Kind == kind.Raw();
            var card = new Border
            {
                Width = 230, MinHeight = 110, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(14), CornerRadius = new CornerRadius(8),
                Background = chosen ? Palette.BrandSoft : Palette.ConsolePanel, BorderBrush = chosen ? Palette.Brand : Palette.ConsoleLine,
                BorderThickness = new Thickness(chosen ? 2 : 1),
                Child = UI.V(8, Icons.Make(Icons.Kind(kind), 22, Palette.Brand).Left(), UI.Headline(kind.Label()), UI.Secondary(Icons.Blurb(kind), 12.5)),
            }.OnTap(() => Choose(kind));
            wrap.Children.Add(card);
        }
        return UI.Scroll(wrap);
    }

    string InstructionText => draft?.KindValue() switch
    {
        QuestionKind.Single => "Add the options and select the correct one.",
        QuestionKind.Multiple => "Add the options and tick every correct one.",
        QuestionKind.TrueFalse => "Choose whether the statement is true.",
        QuestionKind.YesNo => "Add statements and set Yes or No for each.",
        QuestionKind.Ordering => "Add every option the candidate sees, then build the correct sequence. Options left out are distractors.",
        QuestionKind.Matching => "Add targets, then items, and pick each item's correct target.",
        QuestionKind.Dropdowns => "Add a list per blank or cell and mark its correct option.",
        QuestionKind.FillIn => "Add a field per answer and list every accepted answer.",
        QuestionKind.Hotspot => "Choose an image, drag to draw areas, and tick the correct one.",
        _ => "",
    };

    void Choose(QuestionKind kind)
    {
        var keep = draft;
        var q = Doc.MakeQuestion(kind);
        if (keep is not null)
        {
            q.Id = keep.Id; q.Prompt = keep.Prompt; q.Topic = keep.Topic; q.Exhibits = keep.Exhibits; q.CaseStudy = keep.CaseStudy;
        }
        else q.Prompt = "";
        // Start from blank text so sample answers from the templates don't slip into the bank.
        foreach (var c in q.Choices ?? []) c.Text = "";
        foreach (var c in q.Items ?? []) c.Text = "";
        foreach (var c in q.Targets ?? []) c.Text = "";
        foreach (var s in q.Statements ?? []) s.Text = "";
        foreach (var r in q.Rows ?? []) r.Label = "";
        foreach (var b in q.Blanks ?? []) { b.Accept = [""]; b.Pattern = null; b.Label = null; }
        if (kind is QuestionKind.Single or QuestionKind.Multiple) q.Answer = null;
        draft = q;
        RenderAll();
    }

    /// <summary>Validation of the draft alone, mapped to the step that can fix each problem.</summary>
    List<Issue> AllIssues()
    {
        if (draft is null) return [];
        var probe = new ExamDocument("t", "T", [draft]) { CaseStudies = Doc.Exam.CaseStudies, PartialCredit = Doc.Exam.PartialCredit };
        var (_, all) = ExamFormat.Validate(ExamFormat.Encode(probe));
        var issues = all.Where(i => i.Severity == Severity.Error && i.Path.StartsWith("questions[0]")).ToList();
        // Image references are checked against the project's assets instead of a folder on disk.
        if (draft.Image is { Length: > 0 } image && !Doc.Assets.ContainsKey(image))
            issues.Add(new Issue(Severity.Error, "questions[0].image", $"File not found: {image}."));
        for (var i = 0; i < (draft.Exhibits?.Count ?? 0); i++)
            if (draft.Exhibits![i].Image is { Length: > 0 } img && !Doc.Assets.ContainsKey(img))
                issues.Add(new Issue(Severity.Error, $"questions[0].exhibits[{i}].image", $"File not found: {img}."));
        if (draft.Id is { } id && Doc.Questions.Any(q => q.Id == id))
            issues.Add(new Issue(Severity.Error, "questions[0].id", $"Another question already uses the id \"{id}\"."));
        return issues;
    }

    List<Issue> IssuesFor(int s) => AllIssues().Where(issue =>
    {
        var rest = issue.Path["questions[0]".Length..].TrimStart('.');
        var field = new string(rest.TakeWhile(c => c != '.' && c != '[').ToArray());
        return s switch
        {
            1 => new[] { "", "id", "prompt", "topic", "exhibits", "caseStudy", "kind" }.Contains(field),
            2 => AnswerKeys.Contains(field),
            3 => new[] { "points", "partialCredit", "explanation" }.Contains(field),
            _ => false,
        };
    }).ToList();

    /// <summary>"questions[0].choices[1].text: Missing" reads as "Choice 2 text: Missing".</summary>
    static string Describe(Issue issue)
    {
        var path = issue.Path["questions[0]".Length..].TrimStart('.');
        if (path.Length == 0) return issue.Message;
        var names = new Dictionary<string, string>
        {
            ["prompt"] = "Prompt", ["id"] = "Id", ["choices"] = "Choice", ["items"] = "Item", ["targets"] = "Target", ["rows"] = "Row",
            ["statements"] = "Statement", ["blanks"] = "Field", ["regions"] = "Area", ["answer"] = "Correct answer", ["image"] = "Image",
            ["exhibits"] = "Exhibit", ["text"] = "text", ["label"] = "label", ["options"] = "options", ["accept"] = "accepted answers", ["points"] = "Points",
        };
        path = Regex.Replace(path, @"\[(\d+)\]", m => $" {int.Parse(m.Groups[1].Value) + 1}");
        var words = path.Split('.').Select(part =>
        {
            var head = part.Split(' ')[0];
            return names.TryGetValue(head, out var n) ? n + part[head.Length..] : part;
        });
        return $"{string.Join(" ", words)}: {issue.Message}";
    }

    void Create()
    {
        if (draft is null || AllIssues().Count > 0) return;
        var q = draft;
        Doc.Edit(() => Doc.Questions.Add(q));
        close();
        onCreate(Doc.Questions.Count - 1);
    }
}
