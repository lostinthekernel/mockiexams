using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MockiCore;

namespace MockiExams;

/// <summary>The Library window: installed exams on the left, the selected exam's start screen or a run on the right.</summary>
public sealed class LibraryWindow : AppWindow
{
    readonly LibraryService library = LibraryService.Shared;
    string? selection;
    RunRequest? run;
    Screen? detail;

    public LibraryWindow()
    {
        Title = "MockiExams Library";
        Width = 1180;
        Height = 760;
        MinWidth = 900;
        MinHeight = 600;
        library.Changed += OnLibraryChanged;
        Closed += (_, _) => library.Changed -= OnLibraryChanged;
        selection = library.Exams.FirstOrDefault()?.Id;
        Render();
    }

    Exam? SelectedExam => library.Exams.FirstOrDefault(e => e.Id == selection);

    void OnLibraryChanged(string? select)
    {
        if (select is not null) selection = select;
        if (selection is null || library.Exams.All(e => e.Id != selection)) selection = library.Exams.FirstOrDefault()?.Id;
        if (run is null) Render();
    }

    protected override Control BuildPage()
    {
        if (run is not null) return DetailArea();
        var sidebar = new Border { Background = Palette.Sidebar, Child = Sidebar() };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("260,Auto,*") };
        var splitter = new GridSplitter { Width = 1, Background = Palette.ConsoleLine, ResizeDirection = GridResizeDirection.Columns };
        var detailArea = DetailArea();
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(detailArea, 2);
        grid.Children.Add(sidebar);
        grid.Children.Add(splitter);
        grid.Children.Add(detailArea);
        return UI.Rows("Auto,Auto,Auto,*", MenuBar(), Toolbar(), UI.Divider(), grid);
    }

    Control MenuBar()
    {
        var menu = new Menu { Background = Palette.Window };
        menu.Items.Add(MenuOf("_File",
            Item("_New Exam Project", "Ctrl+N", App.NewProject),
            Item("_Open Project…", "Ctrl+O", () => App.OpenProject(this)),
            null,
            Item("_Import Exam File…", "Ctrl+I", () => Import(false)),
            Item("Import Exam _Folder…", "Ctrl+Shift+I", () => Import(true)),
            null,
            Item("_Settings…", "Ctrl+OemComma", () => SettingsSheet.Show(this)),
            null,
            Item("E_xit", "Alt+F4", () => App.Quit())));
        menu.Items.Add(MenuOf("_Window", Item("_Library", "Ctrl+Shift+L", () => App.ShowLibrary())));
        menu.Items.Add(MenuOf("_Help", Item("_About MockiExams", null, () => AboutSheet.Show(this))));
        return menu;
    }

    Control Toolbar() => UI.Bar(
        [UI.Btn(UI.Label("import", "Import Exam"), () => Import(false)).Tip("Import an exam file (Ctrl+I)"),
         UI.Btn(UI.Label("folder", "Import Folder"), () => Import(true)).Tip("Import an exam folder or .mockiexam project (Ctrl+Shift+I)")],
        [UI.Btn(UI.Label("pencil.clipboard", "New Exam Project"), App.NewProject).Tip("Open the Designer with a new exam project")]).Pad(12, 8);

    Control Sidebar()
    {
        var list = new StackPanel { Spacing = 2, Margin = new Thickness(8) };
        foreach (var exam in library.Exams)
        {
            var on = exam.Id == selection;
            var row = new Border
            {
                Padding = new Thickness(10, 6), CornerRadius = new CornerRadius(6), Background = on ? Palette.Accent : Brushes.Transparent,
                Child = UI.V(2,
                    UI.Txt(exam.Title, 13, color: on ? Brushes.White : Palette.Text, wrap: false),
                    UI.Txt($"{exam.ExamQuestions.Count} questions", 11.5, color: on ? Brushes.White : Palette.Secondary, wrap: false)),
            };
            row.OnTap(() => { selection = exam.Id; Render(); });
            var menu = new ContextMenu();
            var open = new MenuItem { Header = "Open in Designer" };
            open.Click += (_, _) => OpenInDesigner(exam);
            var remove = new MenuItem { Header = "Remove from Library" };
            remove.Click += async (_, _) =>
            {
                var choice = await Dialogs.Ask(this, $"Remove \"{exam.Title}\" from the Library?", "Your history for this exam is kept.",
                    new DialogButton("Cancel", IsCancel: true), new DialogButton("Remove", ButtonKind.Destructive, IsDefault: true));
                if (choice == "Remove") library.Remove(exam);
            };
            menu.Items.Add(open);
            menu.Items.Add(new Separator());
            menu.Items.Add(remove);
            row.ContextMenu = menu;
            list.Children.Add(row);
        }
        if (library.Broken.Count > 0)
        {
            list.Children.Add(UI.Caption("Could not load").Pad(10, 12, 0, 2));
            foreach (var b in library.Broken) list.Children.Add(UI.Txt(b, 11.5, color: Palette.Red).Pad(10, 0));
        }
        if (library.Exams.Count == 0)
        {
            return UI.Unavailable("No exams yet", "doc.text", "Import an exam file or folder, or create one in the Designer.",
                UI.Btn("Import…", () => Import(false)), UI.Btn("New Exam Project", App.NewProject));
        }
        return UI.Scroll(list);
    }

    Control DetailArea()
    {
        if (run is { } r)
        {
            if (detail is RunView current && current.Request.Id == r.Id) return current;
            detail = new RunView(r,
                result => HistoryStore.Shared.Add(result),
                ids =>
                {
                    run = r with { Options = r.Options with { QuestionIds = ids.ToHashSet(), QuestionLimit = null, Seed = (ulong)Random.Shared.NextInt64() } };
                    Render();
                },
                () => { run = null; Render(); });
            return detail;
        }
        if (SelectedExam is { } exam)
        {
            detail = new ExamDetailView(exam, (options, fullScreen, style) =>
            {
                run = new RunRequest(exam, options, fullScreen, style);
                Render();
            });
            return detail;
        }
        detail = null;
        return new Border { Background = Palette.ConsolePage, Child = UI.Unavailable("Select an exam", "checklist") };
    }

    protected override bool HandleKey(KeyEventArgs e) => detail?.HandleKey(e) ?? false;

    async void Import(bool folder)
    {
        var storage = StorageProvider;
        var paths = new List<string>();
        if (folder)
        {
            var picked = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Import exam folders", AllowMultiple = true });
            paths.AddRange(picked.Select(f => f.TryGetLocalPath()).OfType<string>());
        }
        else
        {
            var picked = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import exams", AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Exam JSON") { Patterns = ["*.json"] }, FilePickerFileTypes.All],
            });
            paths.AddRange(picked.Select(f => f.TryGetLocalPath()).OfType<string>());
        }
        if (paths.Count == 0) return;
        var failures = library.Import(paths);
        if (failures.Count > 0) await Dialogs.Alert(this, string.Join("\n\n", failures), "Some files were not imported");
    }

    /// <summary>Copies the installed exam into a new project folder and opens it in the Designer.</summary>
    void OpenInDesigner(Exam exam)
    {
        if (exam.AssetBase is not { } source) return;
        try
        {
            var dest = Path.Combine(Path.GetTempPath(), $"{LibraryStore.FolderName(exam.Id)}-{Guid.NewGuid().ToString()[..6]}.mockiexam");
            CopyDirectory(source, dest);
            App.OpenProjectAt(dest, untitledCopy: true);
        }
        catch (IOException e)
        {
            _ = Dialogs.Alert(this, e.Message, "Couldn't open the exam");
        }
    }

    static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(from)) CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    public static MenuItem MenuOf(string header, params MenuItem?[] items)
    {
        var m = new MenuItem { Header = header };
        foreach (var i in items) m.Items.Add(i is null ? new Separator() : i);
        return m;
    }

    public static MenuItem Item(string header, string? gesture, Action action, bool enabled = true)
    {
        var m = new MenuItem { Header = header, IsEnabled = enabled };
        if (gesture is not null)
        {
            var g = KeyGesture.Parse(gesture);
            m.InputGesture = g;
            if (gesture != "Alt+F4") m.HotKey = g;
        }
        m.Click += (_, _) => action();
        return m;
    }
}
