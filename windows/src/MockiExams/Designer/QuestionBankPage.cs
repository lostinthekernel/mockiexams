using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using MockiCore;

namespace MockiExams;

/// <summary>The bank as a table: filter, sort, select one to edit below, or several for bulk actions.</summary>
public sealed class QuestionBankPage : Screen
{
    enum StatusFilter { NeedsAttention, Errors, Valid }

    public enum RowStatus { Errors, Warnings, NoExplanation, Valid }

    public sealed record BankRow(int Index, int Number, string QuestionId, string Prompt, string Kind, QuestionKind? KindValue, string Topic, double Points, RowStatus Status, int Count, string StatusText);

    /// <summary>Which columns show; right-click a column header to change it. ID starts hidden.</summary>
    static readonly HashSet<string> HiddenColumns = ["ID"];

    readonly DesignerWindow window;
    readonly DataGrid grid;
    readonly System.Collections.ObjectModel.ObservableCollection<BankRow> source = [];
    DataGridCollectionView? view;
    readonly ContentControl editorHost = new();
    QuestionKind? kindFilter;
    string? topicFilter;
    StatusFilter? statusFilter;
    bool syncingSelection;

    public QuestionBankPage(DesignerWindow window)
    {
        this.window = window;
        grid = MakeGrid();
        Content = Build();
    }

    ProjectDocument Doc => window.Document;

    protected override Control Build()
    {
        LoadRows();
        ShowEditor();
        var table = UI.Rows("Auto,*", LiveRegion(FilterBar), new Grid { Children = { grid, LiveRegion(EmptyOverlay) } });
        var split = new Grid { RowDefinitions = new RowDefinitions("2*,5,3*") };
        var splitter = new GridSplitter { Height = 5, Background = Palette.ConsoleLine, ResizeDirection = GridResizeDirection.Rows };
        Grid.SetRow(splitter, 1);
        Grid.SetRow(editorHost, 2);
        split.Children.Add(table);
        split.Children.Add(splitter);
        split.Children.Add(editorHost);
        table.MinHeight = 220;
        return ConsolePage.Make([Doc.DisplayName], $"Question bank ({Doc.Questions.Count})", null,
            [ActionsMenu(), UI.Btn("Import…", Import).Tip("Add questions from a CSV, Aiken, GIFT or MockiExams JSON file"),
             UI.Btn("Create Question", window.CreateQuestion, ButtonKind.Prominent, Palette.BrandFill)],
            split);
    }

    /// <summary>Document changes refresh the table and filters, but leave the editor alone so typing keeps focus.</summary>
    public override void Invalidate()
    {
        LoadRows();
        base.Invalidate();
    }

    // MARK: Rows

    List<BankRow> AllRows()
    {
        var rows = new List<BankRow>();
        for (var i = 0; i < Doc.Questions.Count; i++)
        {
            var q = Doc.Questions[i];
            var mine = window.Issues.Where(x => x.Path == $"questions[{i}]" || x.Path.StartsWith($"questions[{i}].")).ToList();
            var errs = mine.Count(x => x.Severity == Severity.Error);
            var status = errs > 0 ? RowStatus.Errors : mine.Count > 0 ? RowStatus.Warnings : string.IsNullOrWhiteSpace(q.Explanation) ? RowStatus.NoExplanation : RowStatus.Valid;
            var count = errs > 0 ? errs : mine.Count;
            var text = status switch
            {
                RowStatus.Errors => $"{count} error{(count == 1 ? "" : "s")}",
                RowStatus.Warnings => $"{count} warning{(count == 1 ? "" : "s")}",
                RowStatus.NoExplanation => "No explanation",
                _ => "Valid",
            };
            var kind = q.KindValue();
            rows.Add(new BankRow(i, i + 1, q.Id ?? "", Markdown.Plain((q.Prompt ?? "").Split('\n')[0]), kind?.Label() ?? q.Kind ?? "?", kind,
                ProjectDocument.NonEmpty(q.Topic) ?? "General", q.Points ?? 1, status, count, text));
        }
        return rows;
    }

    List<BankRow> FilteredRows()
    {
        var text = window.Search.Trim();
        return AllRows().Where(r =>
        {
            if (kindFilter is { } k && r.KindValue != k) return false;
            if (topicFilter is { } t && r.Topic != t) return false;
            switch (statusFilter)
            {
                case StatusFilter.Errors when r.Status != RowStatus.Errors: return false;
                case StatusFilter.NeedsAttention when r.Status == RowStatus.Valid: return false;
                case StatusFilter.Valid when r.Status != RowStatus.Valid: return false;
            }
            return text.Length == 0 || new[] { r.QuestionId, r.Prompt, r.Topic, r.Kind }.Any(s => s.Contains(text, StringComparison.CurrentCultureIgnoreCase));
        }).ToList();
    }

    /// <summary>Updates the table in place, so selection and scroll position survive edits.</summary>
    void LoadRows()
    {
        syncingSelection = true;
        var fresh = FilteredRows();
        if (fresh.Count == source.Count && fresh.Select(r => r.Index).SequenceEqual(source.Select(r => r.Index)))
        {
            for (var i = 0; i < fresh.Count; i++) if (!fresh[i].Equals(source[i])) source[i] = fresh[i];
        }
        else
        {
            source.Clear();
            foreach (var r in fresh) source.Add(r);
        }
        view ??= new DataGridCollectionView(source);
        if (grid.ItemsSource != view) grid.ItemsSource = view;
        var shown = grid.SelectedItems.OfType<BankRow>().Select(r => r.Index).ToHashSet();
        if (!shown.SetEquals(window.BankSelection.Where(i => source.Any(r => r.Index == i))))
        {
            grid.SelectedItems.Clear();
            foreach (var row in source.Where(r => window.BankSelection.Contains(r.Index))) grid.SelectedItems.Add(row);
        }
        // The grid can report selection changes after the reload; they are ours, not the user's.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => syncingSelection = false, Avalonia.Threading.DispatcherPriority.Background);
    }

    DataGrid MakeGrid()
    {
        var g = new DataGrid
        {
            SelectionMode = DataGridSelectionMode.Extended, CanUserSortColumns = true, CanUserResizeColumns = true, CanUserReorderColumns = false,
            IsReadOnly = true, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, HeadersVisibility = DataGridHeadersVisibility.Column,
            Margin = new Thickness(22, 0, 22, 8), BorderThickness = new Thickness(1), BorderBrush = Palette.ConsoleLine, Background = Palette.ConsolePanel,
        };
        g.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new Binding(nameof(BankRow.Number)), Width = new DataGridLength(46) });
        g.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new Binding(nameof(BankRow.QuestionId)), Width = new DataGridLength(130), FontFamily = Fonts.Mono });
        g.Columns.Add(new DataGridTextColumn { Header = "Question", Binding = new Binding(nameof(BankRow.Prompt)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 200 });
        g.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Kind", SortMemberPath = nameof(BankRow.Kind), Width = new DataGridLength(150),
            CellTemplate = new FuncDataTemplate<BankRow>((row, _) => row is null ? new Panel() :
                UI.H(6, Icons.Make(row.KindValue is { } k ? Icons.Kind(k) : "doc", 13, Palette.Secondary), UI.Txt(row.Kind, 12.5, wrap: false)).Pad(8, 0)),
        });
        g.Columns.Add(new DataGridTextColumn { Header = "Topic", Binding = new Binding(nameof(BankRow.Topic)), Width = new DataGridLength(130) });
        g.Columns.Add(new DataGridTextColumn { Header = "Points", Binding = new Binding(nameof(BankRow.Points)), Width = new DataGridLength(80) });
        g.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Status", SortMemberPath = nameof(BankRow.Status), Width = new DataGridLength(130),
            CellTemplate = new FuncDataTemplate<BankRow>((row, _) => row is null ? new Panel() : StatusBadge(row)),
        });
        foreach (var c in g.Columns) c.IsVisible = !HiddenColumns.Contains((string)c.Header!);

        // Right-click any column header to show or hide columns. Question always shows.
        var flyout = new MenuFlyout();
        foreach (var c in g.Columns.Where(c => (string)c.Header! != "Question"))
        {
            var item = new MenuItem { Header = c.Header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = c.IsVisible };
            item.Click += (_, _) =>
            {
                c.IsVisible = !c.IsVisible;
                item.IsChecked = c.IsVisible;
                if (c.IsVisible) HiddenColumns.Remove((string)c.Header!); else HiddenColumns.Add((string)c.Header!);
            };
            flyout.Items.Add(item);
        }
        g.Styles.Add(new Style(x => x.OfType<DataGridColumnHeader>())
        {
            Setters = { new Setter(ContextFlyoutProperty, flyout) },
        });

        g.SelectionChanged += (_, _) =>
        {
            if (syncingSelection) return;
            window.BankSelection.Clear();
            foreach (var r in g.SelectedItems.OfType<BankRow>()) window.BankSelection.Add(r.Index);
            ShowEditor();
        };
        g.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && window.BankSelection.Count > 0) { Delete(window.BankSelection.ToList()); e.Handled = true; }
        };
        g.ContextRequested += (_, e) =>
        {
            var ids = window.BankSelection.ToList();
            if (ids.Count == 0) return;
            var menu = new ContextMenu();
            if (ids.Count == 1)
            {
                menu.Items.Add(LibraryWindow.Item("Preview", null, () => window.Preview(SessionMode.Practice, ids[0])));
                menu.Items.Add(LibraryWindow.Item("Duplicate", null, () => Duplicate(ids[0])));
            }
            menu.Items.Add(LibraryWindow.Item(ids.Count == 1 ? "Delete Question" : $"Delete {ids.Count} Questions", null, () => Delete(ids)));
            menu.Open(g);
            e.Handled = true;
        };
        return g;
    }

    static Control StatusBadge(BankRow row)
    {
        var color = row.Status switch { RowStatus.Errors => Palette.Err, RowStatus.Warnings => Palette.Warn, RowStatus.NoExplanation => Palette.Secondary, _ => Palette.Ok };
        return UI.H(5, new Avalonia.Controls.Shapes.Ellipse { Width = 7, Height = 7, Fill = color }, UI.Txt(row.StatusText, 12.5, FontWeight.Medium, color, wrap: false)).Pad(8, 0);
    }

    // MARK: Filters

    Control FilterBar()
    {
        var shown = FilteredRows().Count;
        var total = Doc.Questions.Count;
        return UI.Bar(
            [Icons.Make("filter", 15, Palette.Secondary),
             FilterMenu("Kind", kindFilter?.Label(), QuestionKinds.All.Select(k => (k.Label(), (Action)(() => kindFilter = k))), () => kindFilter = null),
             FilterMenu("Topic", topicFilter, Doc.Topics.Select(t => (t, (Action)(() => topicFilter = t))), () => topicFilter = null),
             FilterMenu("Status", statusFilter switch { StatusFilter.NeedsAttention => "Needs attention", StatusFilter.Errors => "Errors", StatusFilter.Valid => "Valid", _ => null },
                [("Needs attention", () => statusFilter = StatusFilter.NeedsAttention), ("Errors", () => statusFilter = StatusFilter.Errors), ("Valid", () => statusFilter = StatusFilter.Valid)],
                () => statusFilter = null),
             window.Search.Length == 0 ? null : Chip($"Text: {window.Search}", () => { window.Search = ""; window.Render(); })],
            [UI.Secondary(shown == total ? $"{shown} questions" : $"{shown} of {total}", 12.5)], 8).Pad(22, 0, 22, 8);
    }

    Control FilterMenu(string name, string? value, IEnumerable<(string Label, Action Set)> items, Action clear)
    {
        if (value is not null) return Chip($"{name} = {value}", () => { clear(); Invalidate(); });
        var menu = new MenuFlyout();
        foreach (var (label, set) in items)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => { set(); Invalidate(); };
            menu.Items.Add(item);
        }
        return new Button { Content = UI.H(4, UI.Txt(name, 12.5, wrap: false), Icons.Make("chevron.down", 9, Palette.Secondary)), Flyout = menu, Padding = new Thickness(10, 3) };
    }

    static Control Chip(string text, Action clear) => new Border
    {
        Padding = new Thickness(9, 3), CornerRadius = new CornerRadius(20), Background = Palette.BrandSoft, BorderBrush = Palette.Brand, BorderThickness = new Thickness(1),
        Child = UI.H(4, UI.Txt(text, 12.5, color: Palette.Brand, wrap: false), Icons.Make("xmark", 9, Palette.Brand)),
    }.Tip("Remove filter").OnTap(clear);

    Control? EmptyOverlay()
    {
        if (FilteredRows().Count > 0) return null;
        var none = Doc.Questions.Count == 0;
        return new Border
        {
            Background = Palette.ConsolePage, Margin = new Thickness(22, 30, 22, 8),
            Child = UI.Unavailable(none ? "No questions yet" : "No matches", "list",
                none ? "Create a question or import a file to start the bank." : "Clear a filter to see more questions.",
                none ? [UI.Btn("Create Question", window.CreateQuestion)] : []),
        };
    }

    // MARK: Editor panel

    void ShowEditor()
    {
        var sel = window.BankSelection.Where(i => i < Doc.Questions.Count).ToList();
        if (sel.Count == 1)
        {
            var i = sel[0];
            var q = Doc.Questions[i];
            var head = new Border
            {
                Background = Palette.ConsolePanel, Padding = new Thickness(16, 8),
                Child = UI.Bar(
                    [UI.Txt(q.Id ?? "Question", 14, FontWeight.SemiBold).Do(t => t.FontFamily = Fonts.Mono), UI.Secondary(q.KindValue()?.Label() ?? "")],
                    [UI.IconBtn("arrow.up", () => Move(i, -1), "Move up in the bank").Disabled(i == 0),
                     UI.IconBtn("arrow.down", () => Move(i, 1), "Move down in the bank").Disabled(i == Doc.Questions.Count - 1),
                     UI.Btn("Preview", () => window.Preview(SessionMode.Practice, i)),
                     UI.Btn("Duplicate", () => Duplicate(i)),
                     UI.IconBtn("xmark", () => { window.BankSelection.Clear(); LoadRows(); ShowEditor(); }, "Close the editor")]),
            };
            editorHost.Content = UI.Rows("Auto,Auto,*", head, UI.Divider(), UI.Scroll(new QuestionEditor(window, q)));
        }
        else if (sel.Count > 1)
        {
            editorHost.Content = new Border
            {
                Background = Palette.ConsolePanel,
                Child = UI.V(10, UI.Headline($"{sel.Count} questions selected").Center(),
                    UI.Secondary("Use Actions to set a topic, set points, add to a section, or delete them together.").Center())
                    .Do(v => v.VerticalAlignment = VerticalAlignment.Center),
            };
        }
        else
        {
            editorHost.Content = new Border { Background = Palette.ConsolePanel, Child = UI.Secondary("Select a question to edit it here.").Center() };
        }
    }

    void Move(int i, int d)
    {
        if (i + d < 0 || i + d >= Doc.Questions.Count) return;
        Doc.Edit(() => Editors.Swap(Doc.Questions, i, d));
        window.BankSelection.Clear();
        window.BankSelection.Add(i + d);
        LoadRows();
        ShowEditor();
    }

    // MARK: Actions

    Control ActionsMenu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(LibraryWindow.Item("Set Topic…", null, async () =>
        {
            var n = window.BankSelection.Count;
            var topic = await Dialogs.Prompt(this, "Set topic", $"Applies to {n} selected question{(n == 1 ? "" : "s")}.", "", "Topic");
            if (topic is not null) Apply(q => q.Topic = ProjectDocument.NonEmpty(topic));
        }));
        menu.Items.Add(LibraryWindow.Item("Set Points…", null, async () =>
        {
            var n = window.BankSelection.Count;
            var text = await Dialogs.Prompt(this, "Set points", $"Applies to {n} selected question{(n == 1 ? "" : "s")}.", "", "Points");
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var p) && p > 0) Apply(q => q.Points = p == 1 ? null : p);
        }));
        var partial = new MenuItem { Header = "Partial Credit" };
        partial.Items.Add(LibraryWindow.Item("On", null, () => Apply(q => q.PartialCredit = true)));
        partial.Items.Add(LibraryWindow.Item("Off", null, () => Apply(q => q.PartialCredit = false)));
        partial.Items.Add(LibraryWindow.Item("Exam Default", null, () => Apply(q => q.PartialCredit = null)));
        menu.Items.Add(partial);
        var section = new MenuItem { Header = "Add to Section" };
        var sections = Doc.Exam.Sections ?? [];
        for (var si = 0; si < sections.Count; si++)
        {
            var index = si;
            section.Items.Add(LibraryWindow.Item(ProjectDocument.NonEmpty(sections[si].Title) ?? sections[si].Id ?? $"Section {si + 1}", null, () => AddToSection(index)));
        }
        if (sections.Count == 0) section.Items.Add(new MenuItem { Header = "No sections yet", IsEnabled = false });
        menu.Items.Add(section);
        menu.Items.Add(new Separator());
        menu.Items.Add(LibraryWindow.Item("Delete", null, () => Delete(window.BankSelection.ToList())));
        var button = new Button { Content = UI.H(4, UI.Txt("Actions", wrap: false), Icons.Make("chevron.down", 9, Palette.Secondary)), Flyout = menu };
        menu.Opening += (_, _) => { foreach (var i in menu.Items.OfType<MenuItem>()) i.IsEnabled = window.BankSelection.Count > 0; };
        return button;
    }

    void Apply(Action<QuestionDocument> change)
    {
        var picked = window.BankSelection.Where(i => i < Doc.Questions.Count).Select(i => Doc.Questions[i]).ToList();
        Doc.Edit(() => { foreach (var q in picked) change(q); });
        ShowEditor();
    }

    void AddToSection(int si)
    {
        var ids = window.BankSelection.Order().Select(i => Doc.Questions.ElementAtOrDefault(i)?.Id).OfType<string>().ToList();
        Doc.Edit(() =>
        {
            var sections = Doc.Exam.Sections;
            if (sections is null || si >= sections.Count) return;
            foreach (var s in sections) s.Questions?.RemoveAll(ids.Contains);
            sections[si].Questions = [.. sections[si].Questions ?? [], .. ids];
        });
    }

    async void Delete(List<int> indices)
    {
        var ids = indices.Select(i => Doc.Questions.ElementAtOrDefault(i)?.Id).OfType<string>().ToList();
        if (ids.Count == 0) return;
        if (ids.Count > 1)
        {
            var choice = await Dialogs.Ask(this, $"Delete {ids.Count} questions?", "You can undo this with Ctrl+Z.",
                new DialogButton("Cancel", IsCancel: true), new DialogButton("Delete", ButtonKind.Destructive, IsDefault: true));
            if (choice != "Delete") return;
        }
        Doc.Edit(() => { foreach (var id in ids) Doc.DeleteQuestion(id); });
        window.BankSelection.Clear();
        LoadRows();
        ShowEditor();
    }

    void Duplicate(int i)
    {
        if (Doc.Questions.ElementAtOrDefault(i)?.Id is not { } id) return;
        string? copy = null;
        Doc.Edit(() => copy = Doc.DuplicateQuestion(id));
        var j = Doc.Questions.FindIndex(q => q.Id == copy);
        if (j < 0) return;
        window.BankSelection.Clear();
        window.BankSelection.Add(j);
        LoadRows();
        ShowEditor();
    }

    async void Import()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import questions", AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Question files") { Patterns = ["*.csv", "*.tsv", "*.txt", "*.gift", "*.aiken", "*.json"] }, FilePickerFileTypes.All],
        });
        var added = 0;
        var skipped = new List<string>();
        foreach (var f in files)
        {
            if (f.TryGetLocalPath() is not { } path) continue;
            var name = Path.GetFileName(path);
            string text;
            try { text = File.ReadAllText(path, new UTF8Encoding(false, true)); }
            catch (Exception) { skipped.Add($"{name}: can't read"); continue; }
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var doc = JsonSerializer.Deserialize<ExamDocument>(text, ExamFormat.Options);
                    var questions = doc?.Questions ?? [];
                    Doc.Edit(() => added += Doc.Append(questions));
                    continue;
                }
                catch (JsonException) { }
            }
            if (SourceFormats.Detect(name, text) is not { } format)
            {
                skipped.Add($"{name}: not CSV, Aiken, GIFT or MockiExams JSON");
                continue;
            }
            var c = Converter.Convert(text, format, "import", "Import");
            Doc.Edit(() => added += Doc.Append(c.Document.Questions ?? []));
            skipped.AddRange(c.Skipped.Select(s => $"{name} {s.Path}: {s.Message}"));
        }
        if (files.Count == 0) return;
        LoadRows();
        await Dialogs.Alert(this, $"Imported {added} question{(added == 1 ? "" : "s")}.{(skipped.Count == 0 ? "" : "\n\nSkipped:\n" + string.Join("\n", skipped.Take(10)))}");
    }
}
