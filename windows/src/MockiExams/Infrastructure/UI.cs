using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Path = Avalonia.Controls.Shapes.Path;

namespace MockiExams;

public enum ButtonKind { Normal, Prominent, Plain, Link, Destructive }

/// <summary>Small builders so views read top to bottom, like the macOS views they mirror.</summary>
public static class UI
{
    public static TextBlock Txt(string text, double size = 13, FontWeight? weight = null, IBrush? color = null, bool wrap = true) => new()
    {
        Text = text, FontSize = size, FontWeight = weight ?? FontWeight.Normal, Foreground = color ?? Palette.Text,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock Secondary(string text, double size = 13) => Txt(text, size, color: Palette.Secondary);
    public static TextBlock Caption(string text) => Txt(text, 11.5, color: Palette.Secondary);
    public static TextBlock Headline(string text, double size = 14) => Txt(text, size, FontWeight.SemiBold);
    public static TextBlock Title(string text, double size = 22) => Txt(text, size, FontWeight.Bold);

    public static TextBlock Upper(string text) => new()
    {
        Text = text.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Palette.Secondary,
        LetterSpacing = 0.4,
    };

    public static StackPanel V(double spacing, params Control?[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Vertical, Spacing = spacing };
        foreach (var c in children) if (c is not null) p.Children.Add(c);
        return p;
    }

    public static StackPanel H(double spacing, params Control?[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var c in children) if (c is not null) { c.VerticalAlignment = VerticalAlignment.Center; p.Children.Add(c); }
        return p;
    }

    public static StackPanel VList(double spacing, IEnumerable<Control?> children) => V(spacing, children.ToArray());
    public static StackPanel HList(double spacing, IEnumerable<Control?> children) => H(spacing, children.ToArray());

    /// <summary>A row with leading items, a flexible gap, then trailing items.</summary>
    public static DockPanel Bar(IEnumerable<Control?> leading, IEnumerable<Control?> trailing, double spacing = 10)
    {
        var d = new DockPanel { LastChildFill = true };
        var right = HList(spacing, trailing);
        DockPanel.SetDock(right, Dock.Right);
        d.Children.Add(right);
        var left = HList(spacing, leading);
        left.Margin = new Thickness(0, 0, spacing, 0);
        d.Children.Add(left);
        return d;
    }

    /// <summary>Columns sized by GridLength strings like "Auto,*,120".</summary>
    public static Grid Columns(string definitions, params Control?[] children)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(definitions) };
        for (var i = 0; i < children.Length; i++)
        {
            if (children[i] is not { } c) continue;
            Grid.SetColumn(c, i);
            g.Children.Add(c);
        }
        return g;
    }

    public static Grid Rows(string definitions, params Control?[] children)
    {
        var g = new Grid { RowDefinitions = new RowDefinitions(definitions) };
        for (var i = 0; i < children.Length; i++)
        {
            if (children[i] is not { } c) continue;
            Grid.SetRow(c, i);
            g.Children.Add(c);
        }
        return g;
    }

    public static Control Divider(bool vertical = false) => new Border
    {
        Background = Palette.ConsoleLine, Height = vertical ? double.NaN : 1, Width = vertical ? 1 : double.NaN,
    };

    public static Button Btn(string label, Action onClick, ButtonKind kind = ButtonKind.Normal, IBrush? tint = null)
    {
        var b = new Button { Content = label, Padding = new Thickness(12, 5), VerticalAlignment = VerticalAlignment.Center };
        Style(b, kind, tint);
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Button Btn(Control content, Action onClick, ButtonKind kind = ButtonKind.Normal, IBrush? tint = null)
    {
        var b = new Button { Content = content, Padding = new Thickness(10, 5), VerticalAlignment = VerticalAlignment.Center };
        Style(b, kind, tint);
        b.Click += (_, _) => onClick();
        return b;
    }

    static void Style(Button b, ButtonKind kind, IBrush? tint)
    {
        switch (kind)
        {
            case ButtonKind.Prominent:
                b.Background = tint ?? Palette.Accent;
                b.Foreground = Brushes.White;
                b.FontWeight = FontWeight.SemiBold;
                b.Classes.Add("prominent");
                break;
            case ButtonKind.Plain:
                b.Classes.Add("flat");
                b.Background = Brushes.Transparent;
                b.BorderThickness = new Thickness(0);
                b.Padding = new Thickness(4, 2);
                break;
            case ButtonKind.Link:
                b.Classes.Add("flat");
                b.Background = Brushes.Transparent;
                b.BorderThickness = new Thickness(0);
                b.Foreground = tint ?? Palette.Accent;
                b.Padding = new Thickness(2, 0);
                b.Cursor = new Cursor(StandardCursorType.Hand);
                break;
            case ButtonKind.Destructive:
                b.Foreground = Palette.Red;
                break;
        }
    }

    /// <summary>A label with an icon in front, for toolbar and header buttons.</summary>
    public static Control Label(string icon, string text, IBrush? color = null, double size = 13) =>
        H(6, Icons.Make(icon, 14, color ?? Palette.Text), Txt(text, size, color: color ?? Palette.Text, wrap: false));

    public static Button IconBtn(string icon, Action onClick, string? tip = null, double size = 14, IBrush? color = null)
    {
        var b = new Button
        {
            Content = Icons.Make(icon, size, color ?? Palette.Text), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Padding = new Thickness(4), VerticalAlignment = VerticalAlignment.Center,
        };
        b.Classes.Add("flat");
        if (tip is not null) ToolTip.SetTip(b, tip);
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Border Card(Control content, IBrush? background = null, IBrush? border = null, double radius = 8, Thickness? padding = null) => new()
    {
        Child = content, Background = background ?? Palette.ConsolePanel, BorderBrush = border ?? Palette.ConsoleLine,
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius), Padding = padding ?? new Thickness(14),
    };

    public static Border Box(Control content, IBrush? background, double radius = 8, Thickness? padding = null) => new()
    {
        Child = content, Background = background, CornerRadius = new CornerRadius(radius), Padding = padding ?? new Thickness(10),
    };

    public static Border Capsule(string text, IBrush? background = null, IBrush? color = null, double size = 11.5) => new()
    {
        Child = Txt(text, size, color: color ?? Palette.Text, wrap: false), Background = background ?? Palette.Quaternary,
        CornerRadius = new CornerRadius(20), Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center,
    };

    public static ScrollViewer Scroll(Control content, bool horizontal = false) => new()
    {
        Content = content,
        HorizontalScrollBarVisibility = horizontal ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    public static T Tip<T>(this T c, string tip) where T : Control { ToolTip.SetTip(c, tip); return c; }
    public static T Pad<T>(this T c, double h, double v) where T : Control { c.Margin = new Thickness(h, v); return c; }
    public static T Pad<T>(this T c, double l, double t, double r, double b) where T : Control { c.Margin = new Thickness(l, t, r, b); return c; }
    public static T Width<T>(this T c, double w) where T : Control { c.Width = w; return c; }
    public static T MaxW<T>(this T c, double w) where T : Control { c.MaxWidth = w; return c; }
    public static T Left<T>(this T c) where T : Control { c.HorizontalAlignment = HorizontalAlignment.Left; return c; }
    public static T Stretch<T>(this T c) where T : Control { c.HorizontalAlignment = HorizontalAlignment.Stretch; return c; }
    public static T Center<T>(this T c) where T : Control { c.HorizontalAlignment = HorizontalAlignment.Center; return c; }
    public static T Disabled<T>(this T c, bool disabled) where T : Control { c.IsEnabled = !disabled; return c; }
    public static T Do<T>(this T c, Action<T> f) where T : Control { f(c); return c; }

    /// <summary>Makes any control clickable.</summary>
    public static T OnTap<T>(this T c, Action action) where T : Control
    {
        c.Cursor = new Cursor(StandardCursorType.Hand);
        c.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) action(); };
        return c;
    }

    public static Bitmap? LoadBitmap(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        try { using var s = File.OpenRead(path); return new Bitmap(s); }
        catch (Exception) { return null; }
    }

    public static Bitmap? LoadBitmap(byte[]? data)
    {
        if (data is null) return null;
        try { using var s = new MemoryStream(data); return new Bitmap(s); }
        catch (Exception) { return null; }
    }

    /// <summary>A segmented picker: a row of toggle buttons where one is selected.</summary>
    public static Control Segmented<T>(IReadOnlyList<(T Value, string Label)> options, T selected, Action<T> onChange, double minWidth = 0)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        for (var i = 0; i < options.Count; i++)
        {
            var (value, label) = options[i];
            var on = EqualityComparer<T>.Default.Equals(value, selected);
            var b = new Button
            {
                Content = Txt(label, 12.5, on ? FontWeight.SemiBold : FontWeight.Normal, on ? Brushes.White : Palette.Text, wrap: false),
                Background = on ? Palette.Accent : Palette.Control, BorderBrush = Palette.ConsoleLine, BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 4), MinWidth = minWidth, HorizontalContentAlignment = HorizontalAlignment.Center,
                CornerRadius = new CornerRadius(i == 0 ? 6 : 0, i == options.Count - 1 ? 6 : 0, i == options.Count - 1 ? 6 : 0, i == 0 ? 6 : 0),
            };
            b.Click += (_, _) => onChange(value);
            row.Children.Add(b);
        }
        return row;
    }

    public static ComboBox Picker<T>(IReadOnlyList<(T Value, string Label)> options, T selected, Action<T> onChange, double width = double.NaN)
    {
        var combo = new ComboBox
        {
            ItemsSource = options.Select(o => o.Label).ToList(), Width = width, MinWidth = 120,
            SelectedIndex = Math.Max(0, options.ToList().FindIndex(o => EqualityComparer<T>.Default.Equals(o.Value, selected))),
            VerticalAlignment = VerticalAlignment.Center,
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && combo.SelectedIndex < options.Count) onChange(options[combo.SelectedIndex].Value);
        };
        return combo;
    }

    public static CheckBox Toggle(string label, bool on, Action<bool> onChange)
    {
        var c = new CheckBox { Content = label, IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
        c.IsCheckedChanged += (_, _) => onChange(c.IsChecked == true);
        return c;
    }

    public static TextBox Field(string text, Action<string> onChange, string? watermark = null, bool mono = false, double width = double.NaN)
    {
        var t = new TextBox { Text = text, Watermark = watermark, Width = width, VerticalAlignment = VerticalAlignment.Center };
        if (mono) t.FontFamily = Fonts.Mono;
        t.TextChanged += (_, _) => onChange(t.Text ?? "");
        return t;
    }

    public static Control TextArea(string title, string text, Action<string> onChange, double minHeight = 80, bool mono = false)
    {
        var t = new TextBox
        {
            Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = minHeight,
            FontFamily = mono ? Fonts.Mono : FontFamily.Default,
        };
        t.TextChanged += (_, _) => onChange(t.Text ?? "");
        return V(4, Caption(title), t);
    }

    /// <summary>A labeled row inside a form section.</summary>
    public static Control LabeledRow(string label, Control value) =>
        Columns("*,Auto", Txt(label), value.Do(v => v.HorizontalAlignment = HorizontalAlignment.Right)).Pad(0, 2);

    public static Control LabeledText(string label, string value) =>
        Columns("Auto,*", Txt(label).Pad(0, 0, 16, 0), Secondary(value).Do(t => { t.TextAlignment = TextAlignment.Right; })).Pad(0, 3);

    /// <summary>A grouped form section with a header, like a macOS grouped Form section.</summary>
    public static Control FormSection(string? header, IEnumerable<Control?> rows, string? footer = null, Control? headerControl = null)
    {
        var body = VList(10, rows);
        return V(6,
            headerControl ?? (header is null ? null : Txt(header, 12.5, FontWeight.SemiBold, Palette.Secondary)),
            Card(body, Palette.ConsolePanel, Palette.ConsoleLine, 10, new Thickness(14, 12)),
            footer is null ? null : Caption(footer).Pad(4, 0));
    }

    /// <summary>A centered "nothing here" message, like ContentUnavailableView.</summary>
    public static Control Unavailable(string title, string icon, string? description = null, params Control[] actions)
    {
        var v = V(10,
            Icons.Make(icon, 34, Palette.Tertiary).Center(),
            Txt(title, 17, FontWeight.SemiBold).Do(t => t.TextAlignment = TextAlignment.Center).Center(),
            description is null ? null : Secondary(description).Do(t => { t.TextAlignment = TextAlignment.Center; t.MaxWidth = 380; }).Center(),
            actions.Length == 0 ? null : HList(8, actions).Center());
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.VerticalAlignment = VerticalAlignment.Center;
        v.Margin = new Thickness(20);
        return v;
    }

    public static string Points(double v) => v.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);

    public static string Duration(double seconds)
    {
        var s = (int)Math.Floor(seconds);
        return s >= 3600 ? $"{s / 3600}:{s % 3600 / 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }
}

/// <summary>A region that rebuilds its content from a builder when its screen invalidates.</summary>
public sealed class Live : ContentControl
{
    readonly Func<Control?> build;
    public Live(Func<Control?> build) { this.build = build; Content = build(); }
    public void Update() => Content = build();
}

/// <summary>A view whose live regions refresh together after state changes.</summary>
public abstract class Screen : UserControl
{
    readonly List<WeakReference<Live>> regions = [];

    protected Live LiveRegion(Func<Control?> build)
    {
        var live = new Live(build);
        regions.Add(new WeakReference<Live>(live));
        return live;
    }

    /// <summary>Forgets live regions before a screen swaps its whole content.</summary>
    protected void ResetRegions() => regions.Clear();

    /// <summary>Rebuilds every live region (and drops ones that are gone).</summary>
    public virtual void Invalidate()
    {
        regions.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var w in regions.ToList()) if (w.TryGetTarget(out var l)) l.Update();
    }

    /// <summary>Rebuilds the whole screen.</summary>
    public void Rebuild()
    {
        regions.Clear();
        Content = Build();
    }

    protected abstract Control Build();

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Content ??= Build();
    }

    /// <summary>Called by the window for keys not handled by a focused control. Return true if handled.</summary>
    public virtual bool HandleKey(KeyEventArgs e) => false;
}

/// <summary>Line icons drawn as paths on a 24×24 grid, so they look the same everywhere (including Wine).</summary>
public static class Icons
{
    static readonly Dictionary<string, (string Data, bool Fill)> Paths = new()
    {
        ["chevron.left"] = ("M15,5 L8,12 L15,19", false),
        ["chevron.right"] = ("M9,5 L16,12 L9,19", false),
        ["chevron.up"] = ("M5,15 L12,8 L19,15", false),
        ["chevron.down"] = ("M5,9 L12,16 L19,9", false),
        ["xmark"] = ("M6,6 L18,18 M18,6 L6,18", false),
        ["plus"] = ("M12,5 L12,19 M5,12 L19,12", false),
        ["minus.circle"] = ("M12,3 A9,9 0 1 1 11.99,3 Z M7.5,12 L16.5,12", false),
        ["checkmark"] = ("M5,12.5 L10,17.5 L19,7", false),
        ["flag"] = ("M6,21 L6,4 M6,4 L17,4 L14.5,8 L17,12 L6,12", false),
        ["flag.fill"] = ("M6,4 L17,4 L14.5,8 L17,12 L7,12 L7,21 L5,21 L5,4 Z", true),
        ["trash"] = ("M4.5,7 L19.5,7 M10,7 L10,4.5 L14,4.5 L14,7 M6.5,7 L7.5,20 L16.5,20 L17.5,7 M10,11 L10,16.5 M14,11 L14,16.5", false),
        ["magnifyingglass"] = ("M10.5,4 A6.5,6.5 0 1 1 10.49,4 Z M15.3,15.3 L20,20", false),
        ["zoom.in"] = ("M10.5,4 A6.5,6.5 0 1 1 10.49,4 Z M15.3,15.3 L20,20 M10.5,7.5 L10.5,13.5 M7.5,10.5 L13.5,10.5", false),
        ["zoom.out"] = ("M10.5,4 A6.5,6.5 0 1 1 10.49,4 Z M15.3,15.3 L20,20 M7.5,10.5 L13.5,10.5", false),
        ["clock"] = ("M12,3.5 A8.5,8.5 0 1 1 11.99,3.5 Z M12,7.5 L12,12 L15.5,14", false),
        ["timer"] = ("M12,5.5 A7.5,7.5 0 1 1 11.99,5.5 Z M12,9.5 L12,13 M10,2.5 L14,2.5 M18,5.5 L19.5,7", false),
        ["briefcase"] = ("M3.5,8 L20.5,8 L20.5,19 L3.5,19 Z M9,8 L9,5 L15,5 L15,8 M3.5,13 L20.5,13", false),
        ["play"] = ("M3.5,5 L20.5,5 L20.5,19 L3.5,19 Z M10,9 L15,12 L10,15 Z", false),
        ["tray.down"] = ("M12,3.5 L12,13.5 M8,9.5 L12,13.5 L16,9.5 M4,13 L4,19.5 L20,19.5 L20,13", false),
        ["import"] = ("M12,3.5 L12,13.5 M8,9.5 L12,13.5 L16,9.5 M4,13 L4,19.5 L20,19.5 L20,13", false),
        ["gear"] = ("M12,9 A3,3 0 1 1 11.99,9 Z M12,5.5 A6.5,6.5 0 1 1 11.99,5.5 Z M12,2.5 L12,5.5 M12,18.5 L12,21.5 M2.5,12 L5.5,12 M18.5,12 L21.5,12 M5.3,5.3 L7.4,7.4 M16.6,16.6 L18.7,18.7 M5.3,18.7 L7.4,16.6 M16.6,7.4 L18.7,5.3", false),
        ["list"] = ("M9,6.5 L20,6.5 M9,12 L20,12 M9,17.5 L20,17.5 M4,6.5 L5.5,6.5 M4,12 L5.5,12 M4,17.5 L5.5,17.5", false),
        ["list.number"] = ("M10,6.5 L20,6.5 M10,12 L20,12 M10,17.5 L20,17.5 M5,4.5 L5,8.5 M4,14 L6,14 L4,17 L6,17", false),
        ["grid"] = ("M4,4 L10.5,4 L10.5,10.5 L4,10.5 Z M13.5,4 L20,4 L20,10.5 L13.5,10.5 Z M4,13.5 L10.5,13.5 L10.5,20 L4,20 Z M13.5,13.5 L20,13.5 L20,20 L13.5,20 Z", false),
        ["photo"] = ("M3.5,5 L20.5,5 L20.5,19 L3.5,19 Z M3.5,16 L9,11 L13,15 L16,12 L20.5,16 M15.5,8 A1.2,1.2 0 1 1 15.49,8 Z", false),
        ["doc"] = ("M6,3 L14,3 L19,8 L19,21 L6,21 Z M14,3 L14,8 L19,8", false),
        ["doc.text"] = ("M6,3 L14,3 L19,8 L19,21 L6,21 Z M14,3 L14,8 L19,8 M9,12 L16,12 M9,15.5 L16,15.5", false),
        ["pencil"] = ("M4,20 L5,15.5 L16,4.5 L19.5,8 L8.5,19 Z M14,6.5 L17.5,10", false),
        ["lock"] = ("M6,11 L18,11 L18,20 L6,20 Z M8.5,11 L8.5,8 A3.5,3.5 0 0 1 15.5,8 L15.5,11", false),
        ["warning"] = ("M12,4 L21,20 L3,20 Z M12,10 L12,14.5 M12,16.8 L12,17.2", false),
        ["scope"] = ("M12,4.5 A7.5,7.5 0 1 1 11.99,4.5 Z M12,1.5 L12,7.5 M12,16.5 L12,22.5 M1.5,12 L7.5,12 M16.5,12 L22.5,12", false),
        ["line3"] = ("M5,8 L19,8 M5,12 L19,12 M5,16 L19,16", false),
        ["filter"] = ("M4,7 L20,7 M7,12 L17,12 M10,17 L14,17", false),
        ["arrow.right"] = ("M4,12 L20,12 M14,6 L20,12 L14,18", false),
        ["arrow.up"] = ("M12,20 L12,4 M6,10 L12,4 L18,10", false),
        ["arrow.down"] = ("M12,4 L12,20 M6,14 L12,20 L18,14", false),
        ["arrow.leftright"] = ("M4,9 L20,9 M16,5 L20,9 L16,13 M20,15 L4,15 M8,11 L4,15 L8,19", false),
        ["checklist"] = ("M9,6.5 L20,6.5 M9,12 L20,12 M9,17.5 L20,17.5 M3.5,6.5 L5,8 L7.5,5 M3.5,12 L5,13.5 L7.5,10.5 M3.5,17.5 L5,19 L7.5,16", false),
        ["circle.inset"] = ("M12,3.5 A8.5,8.5 0 1 1 11.99,3.5 Z M12,8.5 A3.5,3.5 0 1 1 11.99,8.5 Z", false),
        ["truefalse"] = ("M8,4.5 A6,6 0 1 1 7.99,4.5 Z M5,10.5 L7,12.5 L11,8.5 M14,14 L20,20 M20,14 L14,20", false),
        ["menu.select"] = ("M3.5,5 L20.5,5 L20.5,10 L3.5,10 Z M15,6.5 L16.5,8.5 L18,6.5 M3.5,13 L20.5,13 L20.5,20 L3.5,20 Z", false),
        ["cursor.text"] = ("M9,4 L15,4 M12,4 L12,20 M9,20 L15,20 M3,9 L6,9 M3,15 L6,15", false),
        ["seal.check"] = ("M12,2.5 L14.5,4.5 L17.5,4 L18.5,7 L21,9 L20,12 L21,15 L18.5,17 L17.5,20 L14.5,19.5 L12,21.5 L9.5,19.5 L6.5,20 L5.5,17 L3,15 L4,12 L3,9 L5.5,7 L6.5,4 L9.5,4.5 Z M8,12 L11,15 L16.5,9", false),
        ["seal.x"] = ("M12,2.5 L14.5,4.5 L17.5,4 L18.5,7 L21,9 L20,12 L21,15 L18.5,17 L17.5,20 L14.5,19.5 L12,21.5 L9.5,19.5 L6.5,20 L5.5,17 L3,15 L4,12 L3,9 L5.5,7 L6.5,4 L9.5,4.5 Z M9,9 L15,15 M15,9 L9,15", false),
        ["notes"] = ("M4,4 L15,4 M4,4 L4,20 L20,20 L20,10 M10.5,13.5 L11,11 L18.5,3.5 L20.5,5.5 L13,13 Z", false),
        ["sidebar"] = ("M3.5,5 L20.5,5 L20.5,19 L3.5,19 Z M9,5 L9,19", false),
        ["octagon.x"] = ("M8.3,3 L15.7,3 L21,8.3 L21,15.7 L15.7,21 L8.3,21 L3,15.7 L3,8.3 Z M9,9 L15,15 M15,9 L9,15", false),
        ["info"] = ("M12,3.5 A8.5,8.5 0 1 1 11.99,3.5 Z M12,11 L12,16.5 M12,7.8 L12,8.2", false),
        ["external"] = ("M13,4 L20,4 L20,11 M20,4 L11,13 M17,14 L17,20 L4,20 L4,7 L10,7", false),
        ["save"] = ("M4,4 L17,4 L20,7 L20,20 L4,20 Z M8,4 L8,9 L15,9 L15,4 M7,20 L7,14 L17,14 L17,20", false),
        ["undo"] = ("M9,6 L4,11 L9,16 M4,11 L15,11 A5,5 0 0 1 15,21 L11,21", false),
        ["redo"] = ("M15,6 L20,11 L15,16 M20,11 L9,11 A5,5 0 0 0 9,21 L13,21", false),
        ["folder"] = ("M3.5,6 L9.5,6 L11.5,8.5 L20.5,8.5 L20.5,19 L3.5,19 Z", false),
        ["pencil.clipboard"] = ("M7,4 L5,4 L5,21 L19,21 L19,4 L17,4 M8.5,3 L15.5,3 L15.5,6 L8.5,6 Z M9,16 L9.5,13.5 L14.5,8.5 L16.5,10.5 L11.5,15.5 Z", false),
    };

    public static Control Make(string name, double size = 14, IBrush? brush = null)
    {
        brush ??= Palette.Text;
        switch (name)
        {
            case "checkmark.circle.fill": return Badge(size, brush, "M7.5,12.5 L10.5,15.5 L16.5,9");
            case "xmark.circle.fill": return Badge(size, brush, "M8.5,8.5 L15.5,15.5 M15.5,8.5 L8.5,15.5");
            case "circle.half":
                return new Grid
                {
                    Width = size, Height = size,
                    Children =
                    {
                        new Ellipse { Stroke = brush, StrokeThickness = Math.Max(1.4, size / 12) },
                        new Path { Data = Geometry.Parse("M12,2 A10,10 0 0 0 12,22 Z"), Fill = brush, Stretch = Stretch.Uniform, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Width = size / 2, Height = size },
                    },
                };
        }
        if (!Paths.TryGetValue(name, out var p)) p = Paths["doc"];
        var path = new Path { Data = Geometry.Parse(p.Data) };
        if (p.Fill) path.Fill = brush;
        else
        {
            path.Stroke = brush;
            path.StrokeThickness = 1.8;
            path.StrokeLineCap = PenLineCap.Round;
            path.StrokeJoin = PenLineJoin.Round;
        }
        return new Viewbox
        {
            Width = size, Height = size, Stretch = Stretch.Uniform,
            Child = new Canvas { Width = 24, Height = 24, Children = { path } },
        };
    }

    static Control Badge(double size, IBrush fill, string mark) => new Viewbox
    {
        Width = size, Height = size,
        Child = new Canvas
        {
            Width = 24, Height = 24,
            Children =
            {
                new Ellipse { Width = 22, Height = 22, Fill = fill, [Canvas.LeftProperty] = 1.0, [Canvas.TopProperty] = 1.0 },
                new Path { Data = Geometry.Parse(mark), Stroke = Brushes.White, StrokeThickness = 2.4, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round },
            },
        },
    };

    /// <summary>The icon for each question kind, as in the macOS Designer.</summary>
    public static string Kind(MockiCore.QuestionKind kind) => kind switch
    {
        MockiCore.QuestionKind.Single => "circle.inset",
        MockiCore.QuestionKind.Multiple => "checklist",
        MockiCore.QuestionKind.TrueFalse => "truefalse",
        MockiCore.QuestionKind.YesNo => "list",
        MockiCore.QuestionKind.Ordering => "list.number",
        MockiCore.QuestionKind.Matching => "arrow.leftright",
        MockiCore.QuestionKind.Dropdowns => "menu.select",
        MockiCore.QuestionKind.FillIn => "cursor.text",
        _ => "scope",
    };

    public static string Blurb(MockiCore.QuestionKind kind) => kind switch
    {
        MockiCore.QuestionKind.Single => "One correct option.",
        MockiCore.QuestionKind.Multiple => "Choose two or more correct options.",
        MockiCore.QuestionKind.TrueFalse => "A statement that is true or false.",
        MockiCore.QuestionKind.YesNo => "Several statements, Yes or No for each.",
        MockiCore.QuestionKind.Ordering => "Drag steps into order, with optional distractors.",
        MockiCore.QuestionKind.Matching => "Drag items onto categories or pairs.",
        MockiCore.QuestionKind.Dropdowns => "Pick from a list in each row, like a hot area.",
        MockiCore.QuestionKind.FillIn => "Type a command, number or word.",
        _ => "Click the right area of an image.",
    };

    /// <summary>A radio or checkbox mark drawn to match the macOS answer rows.</summary>
    public static Control Mark(bool multi, bool selected, double size = 16, IBrush? color = null)
    {
        color ??= selected ? Palette.Accent : Palette.Secondary;
        if (multi)
        {
            return new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(3.5), BorderThickness = new Thickness(1.6),
                BorderBrush = color, Background = selected ? color : Brushes.Transparent,
                Child = selected ? Make("checkmark", size - 4, Brushes.White).Center() : null,
            };
        }
        return new Grid
        {
            Width = size, Height = size,
            Children =
            {
                new Ellipse { Stroke = color, StrokeThickness = 1.6 },
                selected ? new Ellipse { Fill = color, Margin = new Thickness(size * 0.26) } : new Panel(),
            },
        };
    }
}
