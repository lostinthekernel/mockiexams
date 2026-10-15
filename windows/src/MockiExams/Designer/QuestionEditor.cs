using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MockiCore;

namespace MockiExams;

/// <summary>Applies a change to the project as an undoable step (key joins rapid typing into one step).</summary>
public delegate void Mutate(Action change, string? key = null);

/// <summary>The full editor for one bank question, below the question table.</summary>
public sealed class QuestionEditor : ContentControl
{
    readonly DesignerWindow window;
    readonly QuestionDocument q;
    string? idError;

    public QuestionEditor(DesignerWindow window, QuestionDocument question)
    {
        this.window = window;
        q = question;
        Render();
    }

    ProjectDocument Doc => window.Document;
    void Edit(Action change, string? key = null) => Doc.Edit(change, key);

    void Render()
    {
        var idField = new TextBox { Text = q.Id ?? "", FontFamily = Fonts.Mono, Watermark = "Id" };
        var rename = UI.Btn("Rename", () => CommitId(idField.Text ?? ""));
        rename.IsVisible = false;
        idField.TextChanged += (_, _) => rename.IsVisible = (idField.Text ?? "") != (q.Id ?? "");
        idField.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitId(idField.Text ?? ""); e.Handled = true; } };

        var kind = q.KindValue();
        var kindPicker = UI.Picker(QuestionKinds.All.Select(k => (k, k.Label())).ToList(), kind ?? QuestionKind.Single, ChangeKind, 200);
        var points = new TextBox { Text = SwiftJson.Number(q.Points ?? 1), Width = 90, Watermark = "Points" };
        points.TextChanged += (_, _) =>
        {
            if (double.TryParse(points.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var p))
                Edit(() => q.Points = p == 1 ? null : p, $"points:{q.Id}");
        };
        var partial = UI.Picker([(-1, $"Exam default ({(Doc.Exam.PartialCredit == true ? "on" : "off")})"), (1, "On"), (0, "Off")],
            q.PartialCredit is { } pc ? (pc ? 1 : 0) : -1, v => Edit(() => q.PartialCredit = v == -1 ? null : v == 1), 200);

        var cases = Doc.Exam.CaseStudies ?? [];
        Control? casePicker = null;
        if (cases.Count > 0)
        {
            var options = new List<(string, string)> { ("", "None") };
            options.AddRange(cases.Select(c => (c.Id ?? "", c.Title ?? c.Id ?? "")));
            casePicker = UI.LabeledRow("Case study", UI.Picker(options, q.CaseStudy ?? "", v => Edit(() => q.CaseStudy = v.Length == 0 ? null : v), 240));
        }

        Content = UI.V(18,
            UI.FormSection("Question", [
                UI.Columns("*,Auto", idField, rename.Pad(8, 0, 0, 0)),
                idError is null ? null : UI.Txt(idError, 11.5, color: Palette.Red),
                UI.LabeledRow("Kind", kindPicker),
                Editors.TopicField(q, Doc, (a, k) => Edit(a, k)),
                UI.LabeledRow("Points", points),
                UI.LabeledRow("Partial credit", partial),
                casePicker,
                UI.TextArea("Prompt (Markdown; ``` for code blocks)", q.Prompt ?? "", t => Edit(() => q.Prompt = t.Length == 0 ? null : t, $"prompt:{q.Id}"), 90),
            ]),
            new KindEditor(q, Doc, (a, k) => Edit(a, k), StorageOwner),
            new ExhibitsEditor(() => q.Exhibits, v => q.Exhibits = v, Doc, (a, k) => Edit(a, k), StorageOwner),
            UI.FormSection("Explanation", [
                UI.TextArea("Shown after checking in practice, and in the review", q.Explanation ?? "", t => Edit(() => q.Explanation = t.Length == 0 ? null : t, $"explanation:{q.Id}"), 70),
            ])).Pad(16, 14);
    }

    TopLevel? StorageOwner() => TopLevel.GetTopLevel(this);

    void CommitId(string text)
    {
        var newId = text.Trim();
        var old = q.Id ?? "";
        if (newId == old) { idError = null; return; }
        if (newId.Length == 0) { idError = "Id can't be empty."; Render(); return; }
        if (Doc.Questions.Any(x => x.Id == newId)) { idError = $"Another question already uses \"{newId}\"."; Render(); return; }
        idError = null;
        Edit(() => Doc.RenameQuestion(old, newId));
        Render();
    }

    /// <summary>Switches kind, keeping shared fields and starting the kind-specific part from its template.</summary>
    void ChangeKind(QuestionKind kind)
    {
        if (kind == q.KindValue()) return;
        Edit(() =>
        {
            var fresh = JsonSerializer.Deserialize<QuestionDocument>(Templates.Question(kind), ExamFormat.Options)!;
            q.Kind = kind.Raw();
            q.Choices = fresh.Choices; q.Items = fresh.Items; q.Targets = fresh.Targets; q.Rows = fresh.Rows;
            q.Statements = fresh.Statements; q.Blanks = fresh.Blanks; q.Image = fresh.Image; q.Regions = fresh.Regions; q.Answer = fresh.Answer;
            if (kind == QuestionKind.Hotspot)
            {
                q.Image = Doc.Assets.Keys.OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault();
                q.Regions = [];
                q.Answer = new AnswerValue.Strings([]);
            }
        });
        Render();
    }
}

public static class Editors
{
    public static Control TopicField(QuestionDocument q, ProjectDocument doc, Mutate edit)
    {
        var field = new TextBox { Text = q.Topic ?? "", Watermark = "Topic / objective" };
        field.TextChanged += (_, _) => edit(() => q.Topic = string.IsNullOrEmpty(field.Text) ? null : field.Text, $"topic:{q.GetHashCode()}");
        var topics = doc.Questions.Select(x => x.Topic).OfType<string>().Where(t => t.Trim().Length > 0).Distinct().Order().ToList();
        if (topics.Count == 0) return field;
        var menu = new MenuFlyout();
        foreach (var t in topics)
        {
            var item = new MenuItem { Header = t };
            item.Click += (_, _) => field.Text = t;
            menu.Items.Add(item);
        }
        var pick = new Button { Content = Icons.Make("chevron.down", 12), Flyout = menu, Padding = new Thickness(8, 6) }.Tip("Use an existing topic");
        return UI.Columns("*,Auto", field, pick.Pad(6, 0, 0, 0));
    }

    /// <summary>Move up, move down and delete for one row of a list editor.</summary>
    public static Control RowButtons(int index, int count, Action<int, int> move, Action delete) => UI.H(0,
        UI.IconBtn("chevron.up", () => move(index, -1), "Move up").Disabled(index == 0),
        UI.IconBtn("chevron.down", () => move(index, 1), "Move down").Disabled(index == count - 1),
        UI.IconBtn("minus.circle", delete, "Delete", color: Palette.Red));

    public static void Swap<T>(List<T> list, int a, int d) => (list[a], list[a + d]) = (list[a + d], list[a]);

    /// <summary>Picks image or PDF files and adds them to the project's assets; returns the new paths.</summary>
    public static async Task<List<string>> ImportAssets(TopLevel? top, ProjectDocument doc, Mutate edit, bool multiple)
    {
        var added = new List<string>();
        if (top is null) return added;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import images", AllowMultiple = multiple,
            FileTypeFilter = [new FilePickerFileType("Images and PDFs") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp", "*.tif", "*.tiff", "*.pdf"] }],
        });
        foreach (var f in files)
        {
            if (f.TryGetLocalPath() is not { } path) continue;
            try
            {
                var data = File.ReadAllBytes(path);
                edit(() => added.Add(doc.AddAsset(data, System.IO.Path.GetFileName(path))));
            }
            catch (IOException) { }
        }
        return added;
    }
}

/// <summary>The answer part of a question, one form per kind. Used by the editor and the create flow.</summary>
public sealed class KindEditor : ContentControl
{
    readonly QuestionDocument q;
    readonly ProjectDocument doc;
    readonly Mutate edit;
    readonly Func<TopLevel?> top;
    string? selectedRegion;

    public KindEditor(QuestionDocument question, ProjectDocument doc, Mutate edit, Func<TopLevel?> top)
    {
        q = question;
        this.doc = doc;
        this.edit = edit;
        this.top = top;
        Render();
    }

    /// <summary>A structural change: apply it, then redraw this editor.</summary>
    void Change(Action change) { edit(change); Render(); }

    TextBox Text(string value, string watermark, Action<string> set, string key, bool mono = false)
    {
        var t = new TextBox { Text = value, Watermark = watermark, VerticalAlignment = VerticalAlignment.Center };
        if (mono) t.FontFamily = Fonts.Mono;
        t.TextChanged += (_, _) => edit(() => set(t.Text ?? ""), key);
        return t;
    }

    void Render()
    {
        Content = q.KindValue() switch
        {
            QuestionKind.Single => Choices(false),
            QuestionKind.Multiple => Choices(true),
            QuestionKind.Ordering => Ordering(),
            QuestionKind.TrueFalse => TrueFalse(),
            QuestionKind.YesNo => YesNo(),
            QuestionKind.Matching => Matching(),
            QuestionKind.Dropdowns => Dropdowns(),
            QuestionKind.FillIn => FillIn(),
            QuestionKind.Hotspot => Hotspot(),
            _ => UI.Txt($"Unknown kind \"{q.Kind}\". Pick a kind above.", color: Palette.Red),
        };
    }

    // MARK: Single / multiple

    Control Choices(bool multi)
    {
        var choices = q.Choices ??= [];
        var rows = new List<Control?>();
        for (var i = 0; i < choices.Count; i++)
        {
            var c = choices[i];
            var id = c.Id ?? "";
            var correct = q.AnswerIds().Contains(id);
            var mark = new Button
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(2),
                Content = Icons.Mark(multi, correct, 17, correct ? Palette.Green : Palette.Secondary),
            }.Tip(correct ? "Correct answer" : "Mark as correct");
            mark.Click += (_, _) => Change(() =>
            {
                var ids = q.AnswerIds();
                if (multi) { if (!ids.Remove(id)) ids.Add(id); q.Answer = new AnswerValue.Strings(ids); }
                else q.Answer = new AnswerValue.String(id);
            });
            var index = i;
            rows.Add(UI.Columns("Auto,*,Auto", mark.Pad(0, 0, 6, 0), Text(c.Text ?? "", $"Choice {i + 1}", t => c.Text = t, $"choice:{q.GetHashCode()}:{i}"),
                Editors.RowButtons(i, choices.Count, (a, d) => Change(() => Editors.Swap(choices, a, d)), () => Change(() =>
                {
                    choices.RemoveAt(index);
                    var ids = q.AnswerIds().Where(x => x != id).ToList();
                    q.Answer = multi ? new AnswerValue.Strings(ids) : ids.Count > 0 ? new AnswerValue.String(ids[0]) : null;
                }))));
        }
        rows.Add(UI.Btn("Add Choice", () => Change(() => choices.Add(new ChoiceDocument(QuestionDocuments.NextLetter(choices.Select(x => x.Id)), "")))).Left());
        return UI.FormSection(multi ? "Choices — tick every correct answer" : "Choices — select the correct answer", rows);
    }

    // MARK: Ordering

    Control Ordering()
    {
        var choices = q.Choices ??= [];
        string Label(string id) => choices.FirstOrDefault(c => c.Id == id)?.Text ?? id;
        var options = new List<Control?>();
        for (var i = 0; i < choices.Count; i++)
        {
            var c = choices[i];
            var index = i;
            options.Add(UI.Columns("*,Auto,Auto", Text(c.Text ?? "", $"Option {i + 1}", t => c.Text = t, $"option:{q.GetHashCode()}:{i}"),
                q.AnswerIds().Contains(c.Id ?? "") ? null : UI.Caption("distractor").Pad(8, 0),
                Editors.RowButtons(i, choices.Count, (a, d) => Change(() => Editors.Swap(choices, a, d)), () => Change(() =>
                {
                    var id = c.Id;
                    choices.RemoveAt(index);
                    q.Answer = new AnswerValue.Strings(q.AnswerIds().Where(x => x != id).ToList());
                }))));
        }
        options.Add(UI.Btn("Add Option", () => Change(() =>
        {
            var id = QuestionDocuments.NextLetter(choices.Select(x => x.Id));
            choices.Add(new ChoiceDocument(id, ""));
            q.Answer = new AnswerValue.Strings([.. q.AnswerIds(), id]);
        })).Left());

        var order = q.AnswerIds();
        var sequence = new List<Control?>();
        for (var i = 0; i < order.Count; i++)
        {
            var id = order[i];
            sequence.Add(UI.Columns("Auto,*,Auto", UI.Secondary($"{i + 1}.").Pad(0, 0, 8, 0), UI.Txt(Label(id)),
                Editors.RowButtons(i, order.Count, (a, d) => Change(() => { var o = q.AnswerIds(); Editors.Swap(o, a, d); q.Answer = new AnswerValue.Strings(o); }),
                    () => Change(() => q.Answer = new AnswerValue.Strings(q.AnswerIds().Where(x => x != id).ToList())))));
        }
        var unused = choices.Select(c => c.Id).OfType<string>().Where(id => !order.Contains(id)).ToList();
        if (unused.Count > 0)
        {
            var menu = new MenuFlyout();
            foreach (var id in unused)
            {
                var item = new MenuItem { Header = Label(id) };
                item.Click += (_, _) => Change(() => q.Answer = new AnswerValue.Strings([.. q.AnswerIds(), id]));
                menu.Items.Add(item);
            }
            sequence.Add(new Button { Content = "Add to Sequence", Flyout = menu }.Left());
        }
        sequence.Add(UI.Caption("Options left out of the sequence are distractors."));
        return UI.V(18, UI.FormSection("Options (all items the candidate sees)", options), UI.FormSection("Correct order", sequence));
    }

    // MARK: True / false, yes / no

    Control TrueFalse()
    {
        var value = q.Answer is AnswerValue.Bool b ? b.Value : true;
        return UI.FormSection("Answer", [UI.LabeledRow("The statement is", UI.Segmented([(true, "True"), (false, "False")], value, v => Change(() => q.Answer = new AnswerValue.Bool(v)), 80))]);
    }

    Control YesNo()
    {
        var statements = q.Statements ??= [];
        var rows = new List<Control?>();
        for (var i = 0; i < statements.Count; i++)
        {
            var s = statements[i];
            var index = i;
            rows.Add(UI.Columns("*,Auto,Auto", Text(s.Text ?? "", $"Statement {i + 1}", t => s.Text = t, $"statement:{q.GetHashCode()}:{i}"),
                UI.Segmented([(true, "Yes"), (false, "No")], s.Answer ?? true, v => Change(() => s.Answer = v), 52).Pad(8, 0),
                Editors.RowButtons(i, statements.Count, (a, d) => Change(() => Editors.Swap(statements, a, d)), () => Change(() => statements.RemoveAt(index)))));
        }
        rows.Add(UI.Btn("Add Statement", () => Change(() => statements.Add(new StatementDocument { Id = QuestionDocuments.NextId("s", statements.Select(x => x.Id)), Text = "", Answer = true }))).Left());
        return UI.FormSection("Statements", rows);
    }

    // MARK: Matching

    Control Matching()
    {
        var items = q.Items ??= [];
        var targets = q.Targets ??= [];
        Dictionary<string, string> Map() => q.Answer is AnswerValue.Map m ? new Dictionary<string, string>(m.Value) : [];

        var targetRows = new List<Control?>();
        for (var i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            var index = i;
            targetRows.Add(UI.Columns("*,Auto", Text(t.Text ?? "", $"Target {i + 1}", v => t.Text = v, $"target:{q.GetHashCode()}:{i}"),
                Editors.RowButtons(i, targets.Count, (a, d) => Change(() => Editors.Swap(targets, a, d)), () => Change(() =>
                {
                    var id = t.Id;
                    targets.RemoveAt(index);
                    q.Answer = new AnswerValue.Map(Map().Where(kv => kv.Value != id).ToDictionary());
                }))));
        }
        targetRows.Add(UI.Btn("Add Target", () => Change(() => targets.Add(new ChoiceDocument(QuestionDocuments.NextId("t", targets.Select(x => x.Id)), "")))).Left());

        var itemRows = new List<Control?>();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var id = item.Id ?? "";
            var index = i;
            var options = new List<(string, string)> { ("", "Distractor (leave unplaced)") };
            options.AddRange(targets.Select(t => (t.Id ?? "", (t.Text ?? "").NonEmptyOr(t.Id ?? ""))));
            itemRows.Add(UI.Columns("*,Auto,Auto,Auto", Text(item.Text ?? "", $"Item {i + 1}", v => item.Text = v, $"item:{q.GetHashCode()}:{i}"),
                Icons.Make("arrow.right", 14, Palette.Secondary).Pad(8, 0),
                UI.Picker(options, Map().GetValueOrDefault(id) ?? "", v => edit(() =>
                {
                    var m = Map();
                    if (v.Length == 0) m.Remove(id); else m[id] = v;
                    q.Answer = new AnswerValue.Map(m);
                }), 230),
                Editors.RowButtons(i, items.Count, (a, d) => Change(() => Editors.Swap(items, a, d)), () => Change(() =>
                {
                    items.RemoveAt(index);
                    q.Answer = new AnswerValue.Map(Map().Where(kv => kv.Key != id).ToDictionary());
                }))));
        }
        itemRows.Add(UI.Btn("Add Item", () => Change(() => items.Add(new ChoiceDocument(QuestionDocuments.NextId("i", items.Select(x => x.Id)), "")))).Left());
        return UI.V(18, UI.FormSection("Targets (where items are dropped)", targetRows), UI.FormSection("Items and their correct target", itemRows));
    }

    // MARK: Dropdowns

    Control Dropdowns()
    {
        var rows = q.Rows ??= [];
        var list = new List<Control?>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var index = i;
            var options = row.Options ??= [];
            var block = new List<Control?>
            {
                UI.Columns("*,Auto", Text(row.Label ?? "", "Label", v => row.Label = v, $"row:{q.GetHashCode()}:{i}"),
                    Editors.RowButtons(i, rows.Count, (a, d) => Change(() => Editors.Swap(rows, a, d)), () => Change(() => rows.RemoveAt(index)))),
            };
            for (var j = 0; j < options.Count; j++)
            {
                var o = j;
                var isAnswer = row.Answer == options[j];
                var mark = new Button { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(2), Content = Icons.Mark(false, isAnswer, 16, isAnswer ? Palette.Green : Palette.Secondary) }.Tip("Mark as correct");
                mark.Click += (_, _) => Change(() => row.Answer = options[o]);
                var field = new TextBox { Text = options[j] };
                field.TextChanged += (_, _) => edit(() =>
                {
                    var value = field.Text ?? "";
                    if (row.Answer == options[o]) row.Answer = value;
                    options[o] = value;
                }, $"rowopt:{q.GetHashCode()}:{index}:{o}");
                block.Add(UI.Columns("Auto,*,Auto", mark.Pad(0, 0, 6, 0), field, UI.IconBtn("minus.circle", () => Change(() => options.RemoveAt(o)), "Delete", color: Palette.Red)).Pad(16, 0, 0, 0));
            }
            block.Add(UI.Btn("Add Option", () => Change(() => options.Add($"Option {options.Count + 1}"))).Left().Pad(16, 0, 0, 0));
            list.Add(UI.V(6, block.ToArray()).Pad(0, 4));
        }
        list.Add(UI.Btn("Add Dropdown", () => Change(() => rows.Add(new RowDocument { Id = QuestionDocuments.NextId("r", rows.Select(x => x.Id)), Label = "", Options = ["Option 1", "Option 2"], Answer = "Option 1" }))).Left());
        return UI.FormSection("Dropdown lists", list);
    }

    // MARK: Fill in

    Control FillIn()
    {
        var blanks = q.Blanks ??= [];
        var list = new List<Control?>();
        for (var i = 0; i < blanks.Count; i++)
        {
            var b = blanks[i];
            var index = i;
            var accept = b.Accept ??= [];
            var block = new List<Control?>
            {
                UI.Columns("*,Auto,Auto", Text(b.Label ?? "", "Field label (optional)", v => b.Label = v.Length == 0 ? null : v, $"blank:{q.GetHashCode()}:{i}"),
                    UI.Toggle("Case sensitive", b.CaseSensitive ?? false, on => edit(() => b.CaseSensitive = on ? true : null)).Pad(8, 0),
                    Editors.RowButtons(i, blanks.Count, (a, d) => Change(() => Editors.Swap(blanks, a, d)), () => Change(() => blanks.RemoveAt(index)))),
            };
            for (var j = 0; j < accept.Count; j++)
            {
                var a = j;
                block.Add(UI.Columns("*,Auto", Text(accept[j], "Accepted answer", v => accept[a] = v, $"accept:{q.GetHashCode()}:{index}:{a}", mono: true),
                    UI.IconBtn("minus.circle", () => Change(() => accept.RemoveAt(a)), "Delete", color: Palette.Red)).Pad(16, 0, 0, 0));
            }
            block.Add(UI.Btn("Add Accepted Answer", () => Change(() => accept.Add(""))).Left().Pad(16, 0, 0, 0));
            block.Add(Text(b.Pattern ?? "", "Or a regular expression the whole answer must match (optional)", v => b.Pattern = v.Length == 0 ? null : v, $"pattern:{q.GetHashCode()}:{i}", mono: true).Pad(16, 0, 0, 0));
            list.Add(UI.V(6, block.ToArray()).Pad(0, 4));
        }
        list.Add(UI.Btn("Add Field", () => Change(() => blanks.Add(new BlankDocument(QuestionDocuments.NextId("b", blanks.Select(x => x.Id)), [""])))).Left());
        return UI.FormSection("Fields", list, "Spacing is normalized before comparing, so \"show  ip route \" matches \"show ip route\".");
    }

    // MARK: Hotspot

    Control Hotspot()
    {
        var regions = q.Regions ??= [];
        var rows = new List<Control?>
        {
            new AssetPicker(() => q.Image ?? "", v => q.Image = v, doc, edit, top, Render),
        };
        if (q.Image is { } path && doc.Assets.TryGetValue(path, out var data))
        {
            rows.Add(new HotspotCanvas(data, regions, q.AnswerIds().ToHashSet(), selectedRegion, s => { selectedRegion = s; Render(); }, edit, Render));
            rows.Add(UI.Caption("Drag on the image to draw an area. Drag an area to move it, or its corner to resize. Green areas are correct."));
        }
        var areas = new List<Control?>();
        foreach (var r in regions.ToList())
        {
            var id = r.Id ?? "";
            var correct = UI.Toggle("", q.AnswerIds().Contains(id), on => Change(() =>
            {
                var ids = q.AnswerIds().Where(x => x != id).ToList();
                if (on) ids.Add(id);
                q.Answer = new AnswerValue.Strings(ids);
            })).Tip("Correct area");
            var row = UI.Columns("Auto,*,Auto,Auto", correct,
                Text(r.Label ?? "", "Label", v => r.Label = v.Length == 0 ? null : v, $"region:{q.GetHashCode()}:{id}"),
                UI.Caption($"x {r.X:0.00}  y {r.Y:0.00}  {r.Width:0.00}×{r.Height:0.00}").Do(t => t.FontFamily = Fonts.Mono).Pad(8, 0),
                UI.IconBtn("minus.circle", () => Change(() =>
                {
                    regions.Remove(r);
                    q.Answer = new AnswerValue.Strings(q.AnswerIds().Where(x => x != id).ToList());
                }), "Delete", color: Palette.Red));
            areas.Add(new Border
            {
                Padding = new Thickness(4), CornerRadius = new CornerRadius(6),
                Background = selectedRegion == id ? Palette.AccentSoft : Brushes.Transparent, Child = row,
            }.Do(b => b.PointerPressed += (_, _) => { if (selectedRegion != id) { selectedRegion = id; Render(); } }));
        }
        if (regions.Count == 0) areas.Add(UI.Secondary("No areas yet. Draw one on the image."));
        return UI.V(18, UI.FormSection("Image and clickable areas", rows), UI.FormSection("Areas", areas));
    }
}

/// <summary>Draw, move and resize hotspot rectangles on an image. Coordinates are stored as fractions.</summary>
public sealed class HotspotCanvas : ContentControl
{
    enum Mode { Draw, Move, Resize }

    readonly List<RegionDocument> regions;
    readonly HashSet<string> correct;
    readonly string? selected;
    readonly Action<string?> select;
    readonly Mutate edit;
    readonly Action changed;
    readonly FractionPanel overlay = new() { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Cross) };
    Mode? mode;
    Point start;
    Rect original;
    RegionDocument? target;
    Rect? draft;
    Control? draftBox;

    public HotspotCanvas(byte[] image, List<RegionDocument> regions, HashSet<string> correct, string? selected, Action<string?> select, Mutate edit, Action changed)
    {
        this.regions = regions;
        this.correct = correct;
        this.selected = selected;
        this.select = select;
        this.edit = edit;
        this.changed = changed;
        var bitmap = UI.LoadBitmap(image);
        if (bitmap is null)
        {
            Content = UI.Unavailable("Can't display this image", "photo");
            return;
        }
        DrawBoxes();
        overlay.PointerPressed += Pressed;
        overlay.PointerMoved += Moved;
        overlay.PointerReleased += Released;
        Content = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(6), ClipToBounds = true,
            BorderBrush = Palette.Quaternary, BorderThickness = new Thickness(1), MinHeight = 120,
            Child = new Grid { Children = { new Image { Source = bitmap, Stretch = Stretch.Uniform, MaxHeight = 460, MaxWidth = 860 }, overlay } },
        };
    }

    static Rect R(RegionDocument r) => new(r.X ?? 0, r.Y ?? 0, r.Width ?? 0, r.Height ?? 0);

    void DrawBoxes()
    {
        overlay.Children.Clear();
        foreach (var r in regions)
        {
            var id = r.Id ?? "";
            var color = correct.Contains(id) ? Palette.Green : Palette.Accent;
            var isSelected = selected == id;
            var box = new Grid
            {
                IsHitTestVisible = false,
                Children =
                {
                    new Border { BorderBrush = color, BorderThickness = new Thickness(isSelected ? 3 : 2), Background = new SolidColorBrush(((ISolidColorBrush)color).Color, isSelected ? 0.28 : 0.16) },
                    new Border
                    {
                        Background = color, CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 1), Margin = new Thickness(2),
                        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                        Child = UI.Txt(r.Label ?? id, 10, FontWeight.Bold, Brushes.White, wrap: false),
                    },
                    isSelected
                        ? new Rectangle { Width = 10, Height = 10, Fill = Brushes.White, Stroke = color, StrokeThickness = 2, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -5, -5) }
                        : new Panel(),
                },
            };
            var rect = R(r);
            FractionPanel.Place(box, rect.X, rect.Y, rect.Width, rect.Height);
            overlay.Children.Add(box);
        }
        if (draft is { } d)
        {
            draftBox = new Rectangle { Stroke = Palette.Accent, StrokeThickness = 2, StrokeDashArray = [4, 3], IsHitTestVisible = false };
            FractionPanel.Place(draftBox, d.X, d.Y, d.Width, d.Height);
            overlay.Children.Add(draftBox);
        }
    }

    Point Fraction(PointerEventArgs e)
    {
        var p = e.GetPosition(overlay);
        var s = overlay.Bounds.Size;
        return new Point(Math.Clamp(p.X / Math.Max(1, s.Width), 0, 1), Math.Clamp(p.Y / Math.Max(1, s.Height), 0, 1));
    }

    void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(overlay).Properties.IsLeftButtonPressed) return;
        e.Pointer.Capture(overlay);
        start = Fraction(e);
        var size = overlay.Bounds.Size;
        if (selected is not null && regions.FirstOrDefault(r => r.Id == selected) is { } sel)
        {
            var rect = R(sel);
            var corner = new Point(rect.Right * size.Width, rect.Bottom * size.Height);
            var here = new Point(start.X * size.Width, start.Y * size.Height);
            if (Math.Abs(here.X - corner.X) < 12 && Math.Abs(here.Y - corner.Y) < 12)
            {
                mode = Mode.Resize; target = sel; original = rect;
                return;
            }
        }
        var hit = regions.Where(r => R(r).Contains(start)).OrderBy(r => R(r).Width * R(r).Height).FirstOrDefault();
        if (hit is not null)
        {
            mode = Mode.Move; target = hit; original = R(hit);
            if (hit.Id != selected) select(hit.Id);
            return;
        }
        mode = Mode.Draw;
    }

    void Moved(object? sender, PointerEventArgs e)
    {
        if (mode is null) return;
        var p = Fraction(e);
        double dx = p.X - start.X, dy = p.Y - start.Y;
        switch (mode)
        {
            case Mode.Draw:
                draft = new Rect(Math.Min(start.X, p.X), Math.Min(start.Y, p.Y), Math.Abs(dx), Math.Abs(dy));
                DrawBoxes();
                break;
            case Mode.Move when target is not null:
                var x = Math.Clamp(original.X + dx, 0, 1 - original.Width);
                var y = Math.Clamp(original.Y + dy, 0, 1 - original.Height);
                Set(target, new Rect(x, y, original.Width, original.Height));
                break;
            case Mode.Resize when target is not null:
                Set(target, new Rect(original.X, original.Y,
                    Math.Max(0.01, Math.Min(1 - original.X, original.Width + dx)), Math.Max(0.01, Math.Min(1 - original.Y, original.Height + dy))));
                break;
        }
    }

    void Released(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        var p = Fraction(e);
        var size = overlay.Bounds.Size;
        var moved = Math.Abs(p.X - start.X) * size.Width + Math.Abs(p.Y - start.Y) * size.Height > 3;
        if (mode == Mode.Draw)
        {
            if (moved && draft is { } d && d.Width * size.Width > 6 && d.Height * size.Height > 6)
            {
                string id = "r1";
                for (var n = 1; ; n++) { id = $"r{n}"; if (regions.All(r => r.Id != id)) break; }
                var label = $"Area {regions.Count + 1}";
                draft = null;
                edit(() => regions.Add(new RegionDocument { Id = id, Label = label, X = Round3(d.X), Y = Round3(d.Y), Width = Round3(d.Width), Height = Round3(d.Height) }));
                mode = null;
                select(id);
                return;
            }
            draft = null;
            if (!moved) { mode = null; select(null); return; }
        }
        var wasEditing = mode is Mode.Move or Mode.Resize && moved;
        mode = null;
        target = null;
        DrawBoxes();
        if (wasEditing) changed();
    }

    void Set(RegionDocument r, Rect rect)
    {
        edit(() => { r.X = Round3(rect.X); r.Y = Round3(rect.Y); r.Width = Round3(rect.Width); r.Height = Round3(rect.Height); }, $"hotspot-drag:{r.GetHashCode()}");
        DrawBoxes();
    }

    static double Round3(double v) => Math.Round(v * 1000, MidpointRounding.AwayFromZero) / 1000;
}

/// <summary>Chooses an asset, or imports a new one on the spot.</summary>
public sealed class AssetPicker : ContentControl
{
    public AssetPicker(Func<string> get, Action<string> set, ProjectDocument doc, Mutate edit, Func<TopLevel?> top, Action changed)
    {
        var selection = get();
        var keys = doc.Assets.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var options = new List<(string, string)>();
        if (!keys.Contains(selection)) options.Add((selection, selection.Length == 0 ? "Choose…" : $"{selection} (missing)"));
        options.AddRange(keys.Select(k => (k, k)));
        var picker = UI.Picker(options, selection, v => { edit(() => set(v)); changed(); }, 260);
        var import = UI.Btn("Import…", async () =>
        {
            var added = await Editors.ImportAssets(top(), doc, edit, false);
            if (added.Count > 0) { edit(() => set(added[0])); changed(); }
        });
        Control? thumb = doc.Assets.TryGetValue(selection, out var data) && UI.LoadBitmap(data) is { } bmp
            ? new Image { Source = bmp, Width = 120, Height = 70, Stretch = Stretch.Uniform }
            : null;
        Content = UI.H(8, UI.Txt("Image"), picker, import, thumb);
    }
}

/// <summary>Exhibits for a question or case study: text (monospaced) or an image/PDF.</summary>
public sealed class ExhibitsEditor : ContentControl
{
    readonly Func<List<ExhibitDocument>?> get;
    readonly Action<List<ExhibitDocument>?> set;
    readonly ProjectDocument doc;
    readonly Mutate edit;
    readonly Func<TopLevel?> top;

    public ExhibitsEditor(Func<List<ExhibitDocument>?> get, Action<List<ExhibitDocument>?> set, ProjectDocument doc, Mutate edit, Func<TopLevel?> top)
    {
        this.get = get;
        this.set = set;
        this.doc = doc;
        this.edit = edit;
        this.top = top;
        Render();
    }

    void Change(Action a) { edit(a); Render(); }

    void Render()
    {
        var list = get() ?? [];
        var rows = new List<Control?>();
        for (var i = 0; i < list.Count; i++)
        {
            var e = list[i];
            var index = i;
            var title = new TextBox { Text = e.Title ?? "", Watermark = "Exhibit title" };
            title.TextChanged += (_, _) => edit(() => e.Title = string.IsNullOrEmpty(title.Text) ? null : title.Text, $"exhibit:{e.GetHashCode()}");
            var isImage = e.Image is not null;
            var type = UI.Segmented([("text", "Text"), ("image", "Image / PDF")], isImage ? "image" : "text", kind => Change(() =>
            {
                if (kind == "image") { e.Text = null; e.Image = doc.Assets.Keys.OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault() ?? ""; }
                else { e.Image = null; e.Text = ""; }
            }), 80);
            var block = new List<Control?>
            {
                UI.Columns("*,Auto,Auto", title, type.Pad(8, 0), UI.IconBtn("trash", () => Change(() =>
                {
                    list.RemoveAt(index);
                    set(list.Count == 0 ? null : list);
                }), "Delete", color: Palette.Red)),
                isImage
                    ? new AssetPicker(() => e.Image ?? "", v => e.Image = v, doc, edit, top, Render)
                    : UI.TextArea("Text (shown monospaced: CLI output, configs, logs)", e.Text ?? "", t => edit(() => e.Text = t, $"exhibit-text:{e.GetHashCode()}"), 90, true),
            };
            rows.Add(UI.V(8, block.ToArray()).Pad(0, 4));
        }
        var menu = new MenuFlyout();
        var text = new MenuItem { Header = "Text" };
        text.Click += (_, _) => Change(() => { var l = get() ?? []; l.Add(new ExhibitDocument { Title = $"Exhibit {l.Count + 1}", Text = "" }); set(l); });
        var image = new MenuItem { Header = "Image or PDF" };
        image.Click += (_, _) => Change(() => { var l = get() ?? []; l.Add(new ExhibitDocument { Title = $"Exhibit {l.Count + 1}", Image = doc.Assets.Keys.OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault() ?? "" }); set(l); });
        menu.Items.Add(text);
        menu.Items.Add(image);
        rows.Add(new Button { Content = "Add Exhibit", Flyout = menu }.Left());
        Content = UI.FormSection("Exhibits", rows);
    }
}
