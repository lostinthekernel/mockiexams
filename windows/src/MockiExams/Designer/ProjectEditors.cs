using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MockiCore;

namespace MockiExams;

// MARK: - Exam settings

public sealed class ExamSettingsView : ContentControl
{
    readonly DesignerWindow window;

    public ExamSettingsView(DesignerWindow window)
    {
        this.window = window;
        Render();
    }

    ProjectDocument Doc => window.Document;
    ExamDocument E => Doc.Exam;
    void Edit(Action a, string? key = null) => Doc.Edit(a, key);
    void Change(Action a) { Doc.Edit(a); Render(); }

    void Render()
    {
        var timing = new List<Control?>
        {
            UI.LabeledRow("Time limit", UI.Toggle("", E.TimeLimitMinutes is not null, on => Change(() => E.TimeLimitMinutes = on ? 60 : null))),
        };
        if (E.TimeLimitMinutes is { } minutes)
            timing.Add(UI.LabeledRow("Minutes", new NumericUpDown { Value = (decimal)minutes, Minimum = 1, Maximum = 600, Increment = 5, Width = 140, FormatString = "0" }
                .Do(n => n.ValueChanged += (_, _) => { if (n.Value is { } v) Edit(() => E.TimeLimitMinutes = (double)v, "minutes"); })));
        timing.Add(UI.LabeledRow("Allow going back to earlier questions", UI.Toggle("", E.AllowBack ?? true, on => Edit(() => E.AllowBack = on))));
        timing.Add(UI.Caption("Applies in exam simulation. Practice mode always allows going back."));

        var scoring = new List<Control?>
        {
            UI.LabeledRow("Pass mark (%)", new NumericUpDown { Value = E.PassPercent ?? 70, Minimum = 0, Maximum = 100, Width = 140, FormatString = "0" }
                .Do(n => n.ValueChanged += (_, _) => { if (n.Value is { } v) Edit(() => E.PassPercent = (int)v, "pass"); })),
            UI.LabeledRow("Scaled score", UI.Toggle("", E.Scale is not null, on => Change(() => E.Scale = on ? new ScaleDocument { Min = 100, Max = 900, Pass = 700 } : null))),
        };
        if (E.Scale is { } scale)
        {
            TextBox Num(string w, int v, Action<int> set)
            {
                var t = new TextBox { Text = v.ToString(CultureInfo.InvariantCulture), Watermark = w, Width = 90 };
                t.TextChanged += (_, _) => { if (int.TryParse(t.Text, out var n)) Edit(() => set(n), $"scale-{w}"); };
                return t;
            }
            scoring.Add(UI.H(8, UI.Secondary("Min"), Num("Min", scale.Min, n => scale.Min = n), UI.Secondary("Max"), Num("Max", scale.Max, n => scale.Max = n),
                UI.Secondary("Pass"), Num("Pass", scale.Pass, n => scale.Pass = n)));
            scoring.Add(UI.Caption("When set, pass/fail uses the scaled pass score. The scale is a linear estimate."));
        }
        scoring.Add(UI.LabeledRow("Partial credit by default", UI.Toggle("", E.PartialCredit ?? false, on => Edit(() => E.PartialCredit = on))));
        scoring.Add(UI.Caption("Questions can override this. Partial credit awards a share of the points for partly correct answers."));

        var qs = Doc.Questions;
        var summary = new List<Control?>
        {
            UI.LabeledText("Questions in bank", $"{qs.Count}"),
            UI.LabeledText("Total points", UI.Points(qs.Sum(q => q.Points ?? 1))),
        };
        summary.AddRange(QuestionKinds.All.Select(k => (k, n: qs.Count(q => q.Kind == k.Raw()))).Where(p => p.n > 0).Select(p => UI.LabeledText(p.k.Label(), $"{p.n}")));

        Content = UI.Scroll(UI.V(20,
            UI.FormSection("Exam", [
                UI.LabeledRow("Title", UI.Field(E.Title ?? "", t => Edit(() => E.Title = t.Length == 0 ? null : t, "title"), width: 360)),
                UI.LabeledRow("Id", UI.Field(E.Id ?? "", t => Edit(() => E.Id = t.Length == 0 ? null : t, "id"), mono: true, width: 360)),
                UI.Caption("Re-installing an exam with the same id replaces it in the Library and keeps its history."),
                UI.TextArea("Description", E.Description ?? "", t => Edit(() => E.Description = t.Length == 0 ? null : t, "description"), 60),
            ]),
            UI.FormSection("Timing", timing),
            UI.FormSection("Scoring", scoring),
            new BlueprintEditor(window),
            UI.FormSection("Summary", summary)).MaxW(820).Left().Pad(22, 0, 22, 22));
    }
}

// MARK: - Sections

public sealed class SectionsEditor : ContentControl
{
    readonly DesignerWindow window;

    public SectionsEditor(DesignerWindow window)
    {
        this.window = window;
        Render();
    }

    ProjectDocument Doc => window.Document;
    void Change(Action a) { Doc.Edit(a); Render(); }

    string Prompt(string id) => (Doc.Questions.FirstOrDefault(q => q.Id == id)?.Prompt ?? id).Split('\n')[0];

    void Render()
    {
        var sections = Doc.Exam.Sections ?? [];
        var placed = sections.SelectMany(s => s.Questions ?? []).ToHashSet();
        var blocks = new List<Control?>();
        if (sections.Count == 0)
        {
            blocks.Add(UI.FormSection(null, [
                UI.Secondary("No sections: the exam asks every question in the bank, in bank order."),
                UI.Btn("Create a Section with All Questions", () => Change(() => Doc.Exam.Sections =
                    [new SectionDocument { Id = "section-1", Title = "Section 1", Questions = Doc.Questions.Select(q => q.Id).OfType<string>().ToList() }])).Left(),
            ]));
        }
        for (var i = 0; i < sections.Count; i++)
        {
            var s = sections[i];
            var index = i;
            var ids = s.Questions ??= [];
            var rows = new List<Control?>
            {
                UI.LabeledRow("Title", UI.Field(s.Title ?? "", t => Doc.Edit(() => s.Title = t, $"section-title:{index}"), width: 320)),
                UI.LabeledRow("Shuffle within this section", UI.Toggle("", s.Shuffle ?? true, on => Doc.Edit(() => s.Shuffle = on))),
            };
            for (var j = 0; j < ids.Count; j++)
            {
                var id = ids[j];
                var at = j;
                rows.Add(UI.Columns("Auto,*,Auto,Auto",
                    UI.Secondary($"{j + 1}.").Pad(0, 0, 8, 0),
                    Markdown.Label(Markdown.Plain(Prompt(id))).Do(t => { t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis; }),
                    UI.Caption(id).Do(t => t.FontFamily = Fonts.Mono).Pad(8, 0),
                    UI.H(0,
                        UI.IconBtn("chevron.up", () => Change(() => Editors.Swap(ids, at, -1)), "Move up").Disabled(at == 0),
                        UI.IconBtn("chevron.down", () => Change(() => Editors.Swap(ids, at, 1)), "Move down").Disabled(at == ids.Count - 1),
                        UI.IconBtn("minus.circle", () => Change(() => ids.RemoveAt(at)), "Remove from section", color: Palette.Red))));
            }
            var unplaced = Doc.Questions.Select(q => q.Id).OfType<string>().Where(x => !placed.Contains(x)).ToList();
            var add = new MenuFlyout();
            add.Items.Add(LibraryWindow.Item($"All {unplaced.Count} unplaced", null, () => Change(() => ids.AddRange(unplaced)), unplaced.Count > 0));
            add.Items.Add(new Separator());
            foreach (var id in unplaced) add.Items.Add(LibraryWindow.Item($"{id} — {Markdown.Plain(Prompt(id))}", null, () => Change(() => ids.Add(id))));
            rows.Add(new Button { Content = "Add Questions", Flyout = add, IsEnabled = unplaced.Count > 0 }.Left());

            var header = UI.Bar([UI.Txt($"Section {i + 1}", 12.5, FontWeight.SemiBold, Palette.Secondary)],
                [UI.IconBtn("arrow.up", () => Change(() => Editors.Swap(sections, index, -1)), "Move section up").Disabled(i == 0),
                 UI.IconBtn("arrow.down", () => Change(() => Editors.Swap(sections, index, 1)), "Move section down").Disabled(i == sections.Count - 1),
                 UI.IconBtn("trash", () => Change(() => { sections.RemoveAt(index); if (sections.Count == 0) Doc.Exam.Sections = null; }), "Delete section", color: Palette.Red)]);
            blocks.Add(UI.FormSection(null, rows, headerControl: header));
        }
        if (sections.Count > 0)
        {
            var unplaced = Doc.Questions.Count(q => !placed.Contains(q.Id ?? ""));
            blocks.Add(UI.FormSection(null, [
                UI.Btn("Add Section", () => Change(() =>
                {
                    var used = sections.Select(x => x.Id).OfType<string>().ToHashSet();
                    var n = sections.Count + 1;
                    while (used.Contains($"section-{n}")) n++;
                    sections.Add(new SectionDocument { Id = $"section-{n}", Title = $"Section {n}", Questions = [] });
                })).Left(),
                unplaced > 0 ? UI.Caption($"{unplaced} bank question{(unplaced == 1 ? " is" : "s are")} in no section and won't be asked.") : null,
            ]));
        }
        Content = UI.Scroll(UI.VList(18, blocks).MaxW(900).Left().Pad(22, 0, 22, 22));
    }
}

// MARK: - Case studies

/// <summary>Case studies as a list beside the selected one's editor.</summary>
public sealed class CaseStudiesPage : Screen
{
    readonly DesignerWindow window;
    int? selected = 0;
    readonly ContentControl editor = new();

    public CaseStudiesPage(DesignerWindow window)
    {
        this.window = window;
        Content = Build();
    }

    ProjectDocument Doc => window.Document;
    List<CaseStudyDocument> List => Doc.Exam.CaseStudies ?? [];

    protected override Control Build()
    {
        ShowEditor();
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("260,Auto,*") };
        var list = LiveRegion(ListView);
        var line = UI.Divider(true);
        Grid.SetColumn(line, 1);
        Grid.SetColumn(editor, 2);
        split.Children.Add(new Border { Background = Palette.ConsolePanel, Child = list });
        split.Children.Add(line);
        split.Children.Add(editor);
        return ConsolePage.Make([Doc.DisplayName], $"Case studies ({List.Count})", "Shared scenarios shown beside every question that belongs to them.",
            [UI.Btn("Delete", Delete, ButtonKind.Destructive).Disabled(selected is null || List.Count == 0),
             UI.Btn("Create Case Study", Add, ButtonKind.Prominent, Palette.BrandFill)],
            split);
    }

    Control ListView()
    {
        var v = new StackPanel { Spacing = 2, Margin = new Thickness(8) };
        for (var i = 0; i < List.Count; i++)
        {
            var c = List[i];
            var index = i;
            var on = selected == i;
            v.Children.Add(new Border
            {
                Padding = new Thickness(10, 6), CornerRadius = new CornerRadius(6), Background = on ? Palette.Accent : Brushes.Transparent,
                Child = UI.V(2,
                    UI.Txt(ProjectDocument.NonEmpty(c.Title) ?? c.Id ?? "Case study", 13, color: on ? Brushes.White : Palette.Text, wrap: false),
                    UI.Txt($"{Doc.Questions.Count(q => q.CaseStudy == c.Id)} questions · {c.Exhibits?.Count ?? 0} exhibits", 11.5, color: on ? Brushes.White : Palette.Secondary, wrap: false)),
            }.OnTap(() => { selected = index; ShowEditor(); Invalidate(); }));
        }
        return UI.Scroll(v);
    }

    void ShowEditor()
    {
        if (selected is { } i && i < List.Count)
        {
            var c = List[i];
            var questions = Doc.Questions.Where(q => q.CaseStudy == c.Id).ToList();
            editor.Content = UI.Scroll(UI.V(18,
                UI.FormSection("Case study", [
                    UI.LabeledRow("Title", UI.Field(c.Title ?? "", t => Doc.Edit(() => c.Title = t.Length == 0 ? null : t, $"case-title:{i}"), width: 360)),
                    UI.LabeledText("Id", c.Id ?? ""),
                    UI.TextArea("Scenario (Markdown; ``` for code blocks)", c.Scenario ?? "", t => Doc.Edit(() => c.Scenario = t.Length == 0 ? null : t, $"case-scenario:{i}"), 220),
                ]),
                new ExhibitsEditor(() => c.Exhibits, v => c.Exhibits = v, Doc, (a, k) => Doc.Edit(a, k), () => TopLevel.GetTopLevel(this)),
                UI.FormSection("Questions in this case study", questions.Count == 0
                    ? [UI.Secondary("None yet. Pick this case study in a question's settings.")]
                    : questions.Select(q => (Control?)UI.Txt(Markdown.Plain((q.Prompt ?? q.Id ?? "").Split('\n')[0]), wrap: false)))).Pad(18, 4, 22, 22));
        }
        else
        {
            editor.Content = UI.Unavailable("No case study selected", "briefcase", "Create one to group questions around a shared scenario.");
        }
    }

    void Add()
    {
        Doc.Edit(() =>
        {
            var l = Doc.Exam.CaseStudies ??= [];
            var used = l.Select(c => c.Id).OfType<string>().ToHashSet();
            var n = 1;
            while (used.Contains($"case-{n}")) n++;
            l.Add(new CaseStudyDocument { Id = $"case-{n}", Title = "New case study", Scenario = "" });
            selected = l.Count - 1;
        });
        Content = Build();
    }

    void Delete()
    {
        if (selected is not { } i || i >= List.Count) return;
        Doc.Edit(() =>
        {
            var l = Doc.Exam.CaseStudies!;
            var id = l[i].Id;
            l.RemoveAt(i);
            if (l.Count == 0) Doc.Exam.CaseStudies = null;
            foreach (var q in Doc.Questions.Where(q => q.CaseStudy == id)) q.CaseStudy = null;
            selected = l.Count == 0 ? null : Math.Max(0, i - 1);
        });
        Content = Build();
    }
}

// MARK: - Assets

public sealed class AssetsView : ContentControl
{
    readonly DesignerWindow window;

    public AssetsView(DesignerWindow window)
    {
        this.window = window;
        // Wine's drag-and-drop bridge crashes Avalonia's drop handling, so file drops are Windows-only there.
        DragDrop.SetAllowDrop(this, !Platform.IsWine);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None);
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var files = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];
            foreach (var path in files)
            {
                try { var data = File.ReadAllBytes(path); Doc.Edit(() => Doc.AddAsset(data, Path.GetFileName(path))); } catch (IOException) { }
            }
            Render();
        });
        Render();
    }

    ProjectDocument Doc => window.Document;

    void Render()
    {
        var wrap = new WrapPanel { Margin = new Thickness(20) };
        foreach (var path in Doc.Assets.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var uses = Doc.Usage(path);
            var data = Doc.Assets[path];
            Control thumb = UI.LoadBitmap(data) is { } bmp ? new Image { Source = bmp, Stretch = Stretch.Uniform, Margin = new Thickness(6) } : Icons.Make("doc", 34, Palette.Secondary).Center();
            var p = path;
            wrap.Children.Add(new Border
            {
                Width = 230, Margin = new Thickness(0, 0, 16, 16), Padding = new Thickness(10), CornerRadius = new CornerRadius(10),
                BorderBrush = Palette.Quaternary, BorderThickness = new Thickness(1), Background = Palette.ConsolePanel,
                Child = UI.V(6,
                    new Border { Height = 140, Background = Palette.Control, CornerRadius = new CornerRadius(8), Child = thumb },
                    UI.Txt(path, 11.5, wrap: false).Do(t => { t.FontFamily = Fonts.Mono; t.TextTrimming = TextTrimming.PrefixCharacterEllipsis; }),
                    UI.Bar([UI.Txt(uses == 0 ? "Unused" : $"Used {uses}×", 11.5, color: uses == 0 ? Palette.Orange : Palette.Secondary),
                            UI.Caption(OverviewPage.FileSize(data.Length))],
                        [UI.IconBtn("trash", () => { Doc.Edit(() => Doc.Assets.Remove(p)); Render(); }, uses > 0 ? "Remove it from questions first" : "Delete", color: Palette.Red).Disabled(uses > 0)])),
            });
        }
        var import = UI.Btn(UI.Label("photo", "Import Images"), async () =>
        {
            await Editors.ImportAssets(TopLevel.GetTopLevel(this), Doc, (a, k) => Doc.Edit(a, k), true);
            Render();
        });
        Control body = Doc.Assets.Count == 0
            ? UI.Unavailable("No images yet", "photo", Platform.IsWine ? "Import network diagrams, device photos, screenshots or PDFs to use as exhibits and hotspots." : "Import network diagrams, device photos, screenshots or PDFs to use as exhibits and hotspots. You can also drag files here.", import)
            : UI.Rows("Auto,*", UI.H(8, import, Platform.IsWine ? null : UI.Caption("Or drag image files onto this page.")).Pad(20, 0, 20, 0), UI.Scroll(wrap));
        Content = body;
    }
}
