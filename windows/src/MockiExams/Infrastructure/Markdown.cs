using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace MockiExams;

/// <summary>Inline Markdown (bold, italic, code, links) with line breaks kept, plus fenced ``` code blocks,
/// which exam prompts use for configs and output.</summary>
public static class Markdown
{
    public static Control Rich(string source, double size = 13, IBrush? color = null, FontFamily? font = null)
    {
        var blocks = new List<(bool Code, string Text)>();
        var buffer = new List<string>();
        var inCode = false;
        void Flush()
        {
            var joined = string.Join("\n", buffer);
            if (inCode) blocks.Add((true, joined));
            else if (joined.Trim().Length > 0) blocks.Add((false, joined.Trim('\n', '\r')));
            buffer.Clear();
        }
        foreach (var line in source.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```")) { Flush(); inCode = !inCode; }
            else buffer.Add(line);
        }
        Flush();

        var panel = new StackPanel { Spacing = 10 };
        foreach (var (code, text) in blocks)
            panel.Children.Add(code ? CodeBox(text) : Inline(text, size, color, font));
        return panel;
    }

    public static SelectableTextBlock Inline(string text, double size = 13, IBrush? color = null, FontFamily? font = null, bool selectable = true)
    {
        var tb = new SelectableTextBlock
        {
            FontSize = size, Foreground = color ?? Palette.Text, TextWrapping = TextWrapping.Wrap,
            IsHitTestVisible = selectable,
        };
        if (font is not null) tb.FontFamily = font;
        foreach (var inline in Parse(text, size)) tb.Inlines!.Add(inline);
        return tb;
    }

    /// <summary>A non-selectable rendering, for rows that are themselves clickable.</summary>
    public static TextBlock Label(string text, double size = 13, IBrush? color = null, FontFamily? font = null)
    {
        var tb = new TextBlock { FontSize = size, Foreground = color ?? Palette.Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        if (font is not null) tb.FontFamily = font;
        foreach (var inline in Parse(text, size)) tb.Inlines!.Add(inline);
        return tb;
    }

    /// <summary>Plain text with Markdown markers removed, for single-line table cells.</summary>
    public static string Plain(string text)
    {
        var sb = new StringBuilder();
        foreach (var inline in Parse(text, 13))
            if (inline is Run r) sb.Append(r.Text);
            else if (inline is LineBreak) sb.Append(' ');
        return sb.ToString();
    }

    static List<Inline> Parse(string text, double size)
    {
        var result = new List<Inline>();
        var plain = new StringBuilder();
        void FlushPlain()
        {
            if (plain.Length == 0) return;
            var parts = plain.ToString().Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0) result.Add(new LineBreak());
                if (parts[i].Length > 0) result.Add(new Run(parts[i]));
            }
            plain.Clear();
        }

        var s = text;
        var i = 0;
        while (i < s.Length)
        {
            if (s[i] == '\\' && i + 1 < s.Length && "*_`[]\\".Contains(s[i + 1])) { plain.Append(s[i + 1]); i += 2; continue; }
            if (s[i] == '`' && s.IndexOf('`', i + 1) is var endCode and > 0)
            {
                FlushPlain();
                result.Add(new Run(s[(i + 1)..endCode]) { FontFamily = Fonts.Mono, FontSize = size * 0.95, Background = Palette.Quaternary });
                i = endCode + 1;
                continue;
            }
            if (s.AsSpan(i).StartsWith("**") && s.IndexOf("**", i + 2, StringComparison.Ordinal) is var endBold and > 0)
            {
                FlushPlain();
                foreach (var inner in Parse(s[(i + 2)..endBold], size)) { inner.FontWeight = FontWeight.Bold; result.Add(inner); }
                i = endBold + 2;
                continue;
            }
            if ((s[i] == '*' || s[i] == '_') && i + 1 < s.Length && !char.IsWhiteSpace(s[i + 1])
                && s.IndexOf(s[i], i + 1) is var endIt and > 0 && !char.IsWhiteSpace(s[endIt - 1])
                && (s[i] == '*' || i == 0 || !char.IsLetterOrDigit(s[i - 1])))
            {
                FlushPlain();
                foreach (var inner in Parse(s[(i + 1)..endIt], size)) { inner.FontStyle = FontStyle.Italic; result.Add(inner); }
                i = endIt + 1;
                continue;
            }
            if (s[i] == '[' && s.IndexOf("](", i, StringComparison.Ordinal) is var mid and > 0 && s.IndexOf(')', mid) is var close and > 0
                && !s[(i + 1)..mid].Contains('\n'))
            {
                FlushPlain();
                var label = s[(i + 1)..mid];
                var url = s[(mid + 2)..close];
                result.Add(Link(label, url, size));
                i = close + 1;
                continue;
            }
            plain.Append(s[i]);
            i++;
        }
        FlushPlain();
        return result;
    }

    static Inline Link(string label, string url, double size)
    {
        var tb = new TextBlock
        {
            Text = label, FontSize = size, Foreground = Palette.Accent, TextDecorations = TextDecorations.Underline,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(tb, url);
        tb.PointerReleased += (_, _) => Open(url);
        return new InlineUIContainer(tb) { BaselineAlignment = BaselineAlignment.TextBottom };
    }

    public static void Open(string url)
    {
        if (!url.StartsWith("http://") && !url.StartsWith("https://") && !url.StartsWith("mailto:")) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception) { }
    }

    public static Control CodeBox(string text, double size = 12.5) => new Border
    {
        Background = Palette.Control, BorderBrush = Palette.ConsoleLine, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new SelectableTextBlock { Text = text, FontFamily = Fonts.Mono, FontSize = size, Margin = new Thickness(12), Foreground = Palette.Text },
        },
    };
}
