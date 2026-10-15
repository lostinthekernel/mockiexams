using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MockiCore;

namespace MockiExams;

/// <summary>The answering area for any question kind. Reads and writes the session, then calls changed
/// so the rest of the screen (grid, counts) can refresh.</summary>
public sealed class QuestionBodyView : ContentControl
{
    readonly Session session;
    readonly Question q;
    readonly bool classic;
    readonly Action changed;
    string? selected;

    public QuestionBodyView(Session session, Question question, bool classic, Action changed)
    {
        this.session = session;
        q = question;
        this.classic = classic;
        this.changed = changed;
        Render();
    }

    bool Locked => session.IsLocked(q);
    Response? Current => session.Response(q);

    void Render() => Content = q.Body switch
    {
        SingleBody b => ChoiceList(b.Choices, false, new HashSet<string> { b.Answer }),
        MultipleBody b => ChoiceList(b.Choices, true, b.Answer),
        OrderingBody b => Ordering(b),
        MatchingBody b => Matching(b),
        DropdownsBody b => Dropdowns(b.Rows),
        FillInBody b => FillIn(b.Blanks),
        HotspotBody b => Hotspot(b),
        _ => null,
    };

    /// <summary>Redraws after something outside changed the question's state (e.g. it was checked).</summary>
    public void Refresh() => Render();

    void Commit()
    {
        changed();
        Render();
    }

    /// <summary>Number keys pick choices. Returns true if the key was used.</summary>
    public bool Press(int number)
    {
        if (Locked) return false;
        var choices = q.Body switch { SingleBody b => b.Choices, MultipleBody b => b.Choices, _ => null };
        if (choices is null || number < 1 || number > Math.Min(9, choices.Count)) return false;
        session.Choose(choices[number - 1].Id);
        Commit();
        return true;
    }

    // MARK: Single / multiple

    Control ChoiceList(IReadOnlyList<Choice> choices, bool multi, IReadOnlySet<string> correct)
    {
        var picked = Current switch
        {
            SingleResponse s => new HashSet<string> { s.Choice },
            MultipleResponse m => m.Choices.ToHashSet(),
            _ => [],
        };
        var list = new StackPanel { Spacing = classic ? 2 : 8 };
        for (var i = 0; i < choices.Count; i++)
        {
            var c = choices[i];
            bool? verdict = Locked ? (correct.Contains(c.Id) ? true : picked.Contains(c.Id) ? false : null) : null;
            var row = classic ? ClassicRow(i + 1, c.Text, multi, picked.Contains(c.Id)) : ModernRow(i + 1, c.Text, multi, picked.Contains(c.Id), verdict);
            if (!Locked) row.OnTap(() => { session.Choose(c.Id); Commit(); });
            list.Children.Add(row);
        }
        return list;
    }

    static Control ModernRow(int number, string text, bool multi, bool selected, bool? verdict) => new Border
    {
        Padding = new Thickness(12), CornerRadius = new CornerRadius(8),
        Background = verdict switch { true => Palette.GreenSoft, false => Palette.RedSoft, _ => Palette.Control },
        BorderBrush = selected ? Palette.Accent : Palette.Clear, BorderThickness = new Thickness(1.5),
        Child = UI.Columns("Auto,Auto,*,Auto",
            Icons.Mark(multi, selected).Pad(0, 0, 12, 0),
            UI.Secondary(number.ToString()).Pad(0, 0, 12, 0),
            Markdown.Label(text),
            verdict is { } v ? Icons.Make(v ? "checkmark.circle.fill" : "xmark.circle.fill", 16, v ? Palette.Green : Palette.Red).Pad(8, 0, 0, 0) : null),
    };

    /// <summary>Plain lettered rows, as testing-center software shows them.</summary>
    static Control ClassicRow(int number, string text, bool multi, bool selected)
    {
        var letter = (char)(64 + Math.Min(number, 26));
        return new Border
        {
            Padding = new Thickness(0, 4), Background = Palette.Clear,
            Child = UI.Columns("Auto,Auto,*",
                Icons.Mark(multi, selected, 15, Palette.Text).Pad(0, 0, 10, 0),
                UI.Txt($"{letter}. ", 14).Do(t => t.FontFamily = Fonts.Classic),
                Markdown.Label(text, 14, font: Fonts.Classic)),
        };
    }

    // MARK: Dropdowns / yes-no

    Control Dropdowns(IReadOnlyList<DropdownRow> rows)
    {
        var picked = Current is DropdownsResponse d ? d.Picked.ToDictionary() : [];
        var yesNo = q.Kind == QuestionKind.YesNo;
        var list = new StackPanel { Spacing = 8 };
        if (yesNo)
            list.Children.Add(UI.Columns("*,140,Auto", UI.Txt("Statement", 11.5, FontWeight.Bold, Palette.Secondary),
                UI.Txt("Yes / No", 11.5, FontWeight.Bold, Palette.Secondary).Do(t => t.TextAlignment = TextAlignment.Center),
                Locked ? new Panel { Width = 28 } : null).Pad(12, 0));
        foreach (var row in rows)
        {
            void Set(string value)
            {
                var m = new Dictionary<string, string>(picked);
                if (value.Length == 0) m.Remove(row.Id); else m[row.Id] = value;
                session.Respond(new DropdownsResponse(m), q);
                Commit();
            }
            var current = picked.GetValueOrDefault(row.Id) ?? "";
            Control picker;
            if (yesNo)
            {
                picker = UI.Segmented(row.Options.Select(o => (o, o)).ToList(), current, Set, 66).Disabled(Locked).Center();
            }
            else
            {
                var options = new List<(string, string)> { ("", "Select…") };
                options.AddRange(row.Options.Select(o => (o, o)));
                picker = UI.Picker(options, current, Set, 240).Disabled(Locked);
            }
            list.Children.Add(RowBox(UI.Columns($"*,{(yesNo ? "140" : "Auto")},Auto",
                Markdown.Label(row.Label).Pad(0, 0, 12, 0),
                picker,
                Locked ? Verdict(picked.GetValueOrDefault(row.Id) == row.Answer).Pad(8, 0, 0, 0) : null)));
        }
        return list;
    }

    // MARK: Fill in

    Control FillIn(IReadOnlyList<Blank> blanks)
    {
        var list = new StackPanel { Spacing = 8 };
        foreach (var blank in blanks)
        {
            var typed = Current is FillInResponse f ? f.Typed.GetValueOrDefault(blank.Id) ?? "" : "";
            var box = new TextBox
            {
                Text = typed, Watermark = blank.Label ?? "Answer", FontFamily = Fonts.Mono, IsEnabled = !Locked,
                VerticalAlignment = VerticalAlignment.Center,
            };
            box.TextChanged += (_, _) =>
            {
                var m = Current is FillInResponse r ? r.Typed.ToDictionary() : [];
                m[blank.Id] = box.Text ?? "";
                session.Respond(new FillInResponse(m), q);
                changed();
            };
            list.Children.Add(RowBox(UI.Columns("Auto,*,Auto",
                blank.Label is null ? null : UI.Txt(blank.Label).Width(180),
                box,
                Locked ? Verdict(blank.Accepts(typed)).Pad(8, 0, 0, 0) : null)));
        }
        return list;
    }

    // MARK: Matching

    /// <summary>Drag each item onto a target box (a target can hold several items), or back to the pool.
    /// Click an item, then a target, as the keyboard-free alternative.</summary>
    Control Matching(MatchingBody b)
    {
        var placed = Current is MatchingResponse m ? m.Placed.ToDictionary() : [];
        void Place(string itemId, string? target)
        {
            if (Locked || b.Items.All(i => i.Id != itemId)) return;
            var map = new Dictionary<string, string>(placed);
            if (target is null) map.Remove(itemId); else map[itemId] = target;
            session.Respond(new MatchingResponse(map), q);
            selected = null;
            Commit();
        }
        Control Tile(Choice item, bool? verdict)
        {
            var tile = DragTile(item.Text, null, selected == item.Id, verdict);
            if (!Locked)
            {
                InAppDrag.Attach(tile, item.Id, item.Text, () => { selected = selected == item.Id ? null : item.Id; Render(); });
                var menu = new ContextMenu();
                foreach (var t in b.Targets)
                {
                    var mi = new MenuItem { Header = $"Place on {Markdown.Plain(t.Text)}" };
                    mi.Click += (_, _) => Place(item.Id, t.Id);
                    menu.Items.Add(mi);
                }
                menu.Items.Add(new Separator());
                var back = new MenuItem { Header = "Return to Items" };
                back.Click += (_, _) => Place(item.Id, null);
                menu.Items.Add(back);
                tile.ContextMenu = menu;
            }
            return tile;
        }

        var pool = new StackPanel { Spacing = 6 };
        foreach (var item in b.Items.Where(i => !placed.ContainsKey(i.Id)))
            pool.Children.Add(Tile(item, Locked ? !b.Answer.ContainsKey(item.Id) : null));
        if (b.Items.All(i => placed.ContainsKey(i.Id)))
            pool.Children.Add(UI.Txt("All items placed.", 12.5, color: Palette.Tertiary));
        var left = UI.V(6, UI.Txt("Items", 11.5, FontWeight.Bold, Palette.Secondary),
            DropZone(null, pool, 120, (id, _) => Place(id, null), () => { if (selected is { } s) Place(s, null); }));

        var right = new StackPanel { Spacing = 8 };
        right.Children.Add(UI.Txt("Targets", 11.5, FontWeight.Bold, Palette.Secondary));
        foreach (var target in b.Targets)
        {
            var here = new StackPanel { Spacing = 6 };
            var items = b.Items.Where(i => placed.GetValueOrDefault(i.Id) == target.Id).ToList();
            foreach (var item in items) here.Children.Add(Tile(item, Locked ? b.Answer.GetValueOrDefault(item.Id) == target.Id : null));
            if (items.Count == 0)
                here.Children.Add(UI.Txt(selected is null ? "Drag items here" : "Click to place the selected item", 12.5, color: Palette.Tertiary));
            right.Children.Add(DropZone(target.Text, here, 54, (id, _) => Place(id, target.Id), () => { if (selected is { } s) Place(s, target.Id); }));
        }
        return UI.Columns("*,16,*", left, null, right);
    }

    // MARK: Ordering

    /// <summary>Drag options into the answer area and drag rows to reorder. Clicking an option appends it;
    /// the arrow and remove buttons are the keyboard alternative.</summary>
    Control Ordering(OrderingBody b)
    {
        var order = Current is OrderingResponse o ? o.Order.ToList() : [];
        string Text(string id) => b.Choices.FirstOrDefault(c => c.Id == id)?.Text ?? id;
        void Put(string id, int? index)
        {
            if (Locked || b.Choices.All(c => c.Id != id)) return;
            var list = order.ToList();
            var old = list.IndexOf(id);
            list.Remove(id);
            if (index is { } i)
            {
                if (old >= 0 && old < i) i -= 1;
                list.Insert(Math.Clamp(i, 0, list.Count), id);
            }
            session.Respond(new OrderingResponse(list), q);
            Commit();
        }

        var pool = new StackPanel { Spacing = 6 };
        foreach (var c in b.Choices.Where(c => !order.Contains(c.Id)))
        {
            var tile = DragTile(c.Text, null, false, Locked ? !b.Answer.Contains(c.Id) : null);
            if (!Locked) InAppDrag.Attach(tile, c.Id, c.Text, () => Put(c.Id, order.Count));
            pool.Children.Add(tile);
        }
        if (b.Choices.All(c => order.Contains(c.Id))) pool.Children.Add(UI.Txt("All options placed.", 12.5, color: Palette.Tertiary));

        var answer = new StackPanel { Spacing = 6 };
        var rows = new List<(string Id, Control Row)>();
        for (var i = 0; i < order.Count; i++)
        {
            var id = order[i];
            var index = i;
            var tile = DragTile(Text(id), i + 1, false, Locked ? (i < b.Answer.Count && b.Answer[i] == id) : null);
            if (!Locked) InAppDrag.Attach(tile, id, Text(id), null);
            var row = UI.Columns("*,Auto,Auto,Auto", tile,
                Locked ? null : UI.IconBtn("chevron.up", () => Put(id, index - 1), "Move up").Disabled(index == 0),
                Locked ? null : UI.IconBtn("chevron.down", () => Put(id, index + 2), "Move down").Disabled(index == order.Count - 1),
                Locked ? null : UI.IconBtn("xmark", () => Put(id, null), "Remove"));
            rows.Add((id, row));
            answer.Children.Add(row);
        }
        if (order.Count == 0) answer.Children.Add(UI.Txt("Drag options here, in order (or click them).", 12.5, color: Palette.Tertiary));

        Control? answerZone = null;
        answerZone = DropZone(null, answer, 120, (id, point) =>
        {
            // Dropping above a row's middle inserts before it; below every row appends.
            var at = rows.FindIndex(r => r.Row.TranslatePoint(new Point(0, r.Row.Bounds.Height / 2), answerZone!) is { } mid && point.Y < mid.Y);
            Put(id, at < 0 ? order.Count : at);
        }, null);

        return UI.Columns("*,16,*",
            UI.V(6, UI.Txt("Options", 11.5, FontWeight.Bold, Palette.Secondary), DropZone(null, pool, 120, (id, _) => Put(id, null), null)),
            null,
            UI.V(6, UI.Txt("Answer area", 11.5, FontWeight.Bold, Palette.Secondary), answerZone));
    }

    // MARK: Hotspot

    Control Hotspot(HotspotBody b)
    {
        var picked = Current is HotspotResponse h ? h.Regions : new HashSet<string>();
        var bitmap = UI.LoadBitmap(session.Exam.AssetPath(b.Image));
        var v = new StackPanel { Spacing = 8 };
        if (bitmap is null)
        {
            v.Children.Add(UI.Unavailable("Missing image", "photo", b.Image));
            return v;
        }
        var overlay = new FractionPanel { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Cross) };
        foreach (var r in b.Regions)
        {
            var isPicked = picked.Contains(r.Id);
            var isAnswer = b.Answer.Contains(r.Id);
            Control? marker = null;
            if (Locked && (isPicked || isAnswer))
            {
                var color = isAnswer ? Palette.Green : Palette.Red;
                marker = new Border { BorderBrush = color, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(4), Background = isAnswer ? Palette.GreenSoft : Palette.RedSoft };
            }
            else if (isPicked)
            {
                marker = new Border
                {
                    BorderBrush = Palette.Accent, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(4), Background = Palette.AccentSoft,
                    Child = Icons.Make("scope", 22, Palette.Accent).Center().Do(c => c.VerticalAlignment = VerticalAlignment.Center),
                };
            }
            if (marker is null) continue;
            marker.IsHitTestVisible = false;
            FractionPanel.Place(marker, r.X, r.Y, r.Width, r.Height);
            overlay.Children.Add(marker);
        }
        if (!Locked)
        {
            overlay.PointerPressed += (_, e) =>
            {
                var p = e.GetPosition(overlay);
                var size = overlay.Bounds.Size;
                if (size.Width <= 0 || size.Height <= 0) return;
                var x = p.X / size.Width;
                var y = p.Y / size.Height;
                // The smallest region wins when regions overlap.
                var hit = b.Regions.Where(r => r.Contains(x, y)).OrderBy(r => r.Width * r.Height).FirstOrDefault();
                if (hit is null) return;
                session.ToggleRegion(hit.Id);
                Commit();
            };
        }
        var image = new Image { Source = bitmap, Stretch = Stretch.Uniform, MaxWidth = 900, MaxHeight = 640 };
        v.Children.Add(new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(8), ClipToBounds = true,
            BorderBrush = Palette.Quaternary, BorderThickness = new Thickness(1),
            Child = new Grid { Children = { image, overlay } },
        });
        if (picked.Count > 0)
            v.Children.Add(UI.Txt("Selected: " + string.Join(", ", b.Regions.Where(r => picked.Contains(r.Id)).Select(r => r.Label ?? r.Id))));
        if (!Locked)
        {
            // Keyboard / screen-reader alternative to clicking.
            var choose = new WrapPanel { Orientation = Orientation.Horizontal, Opacity = 0.9 };
            choose.Children.Add(UI.Caption("Or choose:").Pad(0, 0, 8, 0));
            foreach (var r in b.Regions)
            {
                var on = picked.Contains(r.Id);
                var btn = UI.Btn(r.Label ?? r.Id, () => { session.ToggleRegion(r.Id); Commit(); }, on ? ButtonKind.Prominent : ButtonKind.Normal);
                btn.Padding = new Thickness(8, 2);
                btn.FontSize = 12;
                btn.Margin = new Thickness(0, 0, 6, 4);
                choose.Children.Add(btn);
            }
            v.Children.Add(choose);
        }
        return v;
    }

    // MARK: Pieces

    static Control RowBox(Control content) => new Border
    {
        Child = content, Padding = new Thickness(10), Background = Palette.Control, CornerRadius = new CornerRadius(8),
    };

    static Control Verdict(bool ok) => Icons.Make(ok ? "checkmark.circle.fill" : "xmark.circle.fill", 16, ok ? Palette.Green : Palette.Red);

    /// <summary>A draggable answer tile.</summary>
    static Border DragTile(string text, int? number, bool selected, bool? verdict) => new()
    {
        Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(7), Background = Palette.Control,
        BorderBrush = selected ? Palette.Accent : Palette.Quaternary, BorderThickness = new Thickness(selected ? 2 : 1),
        Cursor = new Cursor(StandardCursorType.Hand),
        Child = UI.Columns("Auto,Auto,*,Auto",
            Icons.Make("line3", 13, Palette.Tertiary).Pad(0, 0, 8, 0),
            number is { } n ? UI.Secondary(n.ToString()).Pad(0, 0, 8, 0) : null,
            Markdown.Label(text),
            verdict is { } v ? Verdict(v).Pad(6, 0, 0, 0) : null),
    };

    /// <summary>A drop area that highlights while something is dragged over it.</summary>
    static Control DropZone(string? title, Control content, double minHeight, Action<string, Point> onDrop, Action? onTap)
    {
        var outline = new Rectangle
        {
            RadiusX = 10, RadiusY = 10, Stroke = Palette.Tertiary, StrokeThickness = 1, StrokeDashArray = [6, 4],
            Fill = Palette.IsDark ? new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(10, 0, 0, 0)),
        };
        var inner = UI.V(6, title is null ? null : Markdown.Label(title, 13.5).Do(t => t.FontWeight = FontWeight.SemiBold), content);
        inner.Margin = new Thickness(10);
        var zone = new Grid { MinHeight = minHeight, Background = Brushes.Transparent, Children = { outline, inner } };
        void Highlight(bool on)
        {
            outline.Stroke = on ? Palette.Accent : Palette.Tertiary;
            outline.StrokeThickness = on ? 2 : 1;
            outline.Fill = on ? Palette.AccentSoft : (Palette.IsDark ? new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(10, 0, 0, 0)));
        }
        zone.Tag = new DropTarget(onDrop, Highlight);
        if (onTap is not null) zone.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) onTap(); };
        return zone;
    }

    /// <summary>The instruction line under a prompt, shared by both exam styles and practice mode.</summary>
    public static string Instruction(Question q) => q.Body switch
    {
        SingleBody b => $"Choose one. Keys 1–{Math.Min(b.Choices.Count, 9)} select.",
        MultipleBody b => $"Choose {b.Answer.Count}. Keys 1–{Math.Min(b.Choices.Count, 9)} toggle.",
        OrderingBody b => b.Answer.Count < b.Choices.Count
            ? "Drag the correct options to the answer area and put them in order. Not all options are used."
            : "Drag every option to the answer area in the correct order.",
        MatchingBody => "Drag each item onto a target. Targets may be used once, more than once, or not at all.",
        DropdownsBody => q.Kind == QuestionKind.YesNo ? "Select Yes or No for each statement." : "Select the correct option in each list.",
        FillInBody b => b.Blanks.Count == 1 ? "Type your answer." : "Fill in each field.",
        HotspotBody b => b.Answer.Count == 1 ? "Click the correct area of the image." : $"Click the {b.Answer.Count} correct areas of the image.",
        _ => "",
    };

    /// <summary>Fill-in questions need bare keys for typing, so letter and arrow shortcuts are off there.</summary>
    public static bool IsTyping(Question q) => q.Body is FillInBody;
}

/// <summary>Positions children by fractions (0–1) of its own size, for hotspot regions.</summary>
public sealed class FractionPanel : Panel
{
    public static readonly AttachedProperty<Rect> FractionProperty = AvaloniaProperty.RegisterAttached<FractionPanel, Control, Rect>("Fraction");

    public static void Place(Control c, double x, double y, double w, double h) => c.SetValue(FractionProperty, new Rect(x, y, w, h));

    protected override Size MeasureOverride(Size available)
    {
        foreach (var c in Children) c.Measure(available);
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size final)
    {
        foreach (var c in Children)
        {
            var f = c.GetValue(FractionProperty);
            c.Arrange(new Rect(f.X * final.Width, f.Y * final.Height, f.Width * final.Width, f.Height * final.Height));
        }
        return final;
    }
}
