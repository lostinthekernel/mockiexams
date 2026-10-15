using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace MockiExams;

/// <summary>Colors that follow the app appearance. The same values as the macOS app's palette.</summary>
public static class Palette
{
    public static bool IsDark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    static IBrush Dynamic(uint light, uint dark) => new SolidColorBrush(Color.FromUInt32(0xFF000000 | (IsDark ? dark : light)));
    static IBrush Alpha(uint rgb, double a) => new SolidColorBrush(Color.FromUInt32(((uint)(a * 255) << 24) | rgb));

    // Designer
    public static IBrush Brand => Dynamic(0x0B6E69, 0x4FB8B0);
    /// <summary>Fill for prominent buttons; dark enough for white text in both modes.</summary>
    public static IBrush BrandFill => Dynamic(0x0B6E69, 0x1B7770);
    public static IBrush BrandSoft => Dynamic(0xE3F2F1, 0x163432);
    public static IBrush ConsolePage => Dynamic(0xF2F4F7, 0x15191F);
    public static IBrush ConsolePanel => Dynamic(0xFFFFFF, 0x1D232B);
    public static IBrush ConsoleLine => Dynamic(0xD8DEE5, 0x313A46);
    public static IBrush Ok => Dynamic(0x1F7A3A, 0x5CC27C);
    public static IBrush OkSoft => Dynamic(0xE5F4EA, 0x183223);
    public static IBrush Warn => Dynamic(0x8A5300, 0xE7B04A);
    public static IBrush WarnSoft => Dynamic(0xFDF1DC, 0x3A2C12);
    public static IBrush Err => Dynamic(0xB42318, 0xF07B70);
    public static IBrush ErrSoft => Dynamic(0xFDE8E6, 0x3D1C1A);

    // Exam, modern
    public static IBrush ExamPage => Dynamic(0xF6F7F9, 0x14171B);
    public static IBrush ExamSurface => Dynamic(0xFFFFFF, 0x1C2026);
    public static IBrush ExamLine => Dynamic(0xDDE2E8, 0x323944);
    public static IBrush ExamInk => Dynamic(0x15191E, 0xE8ECF1);
    public static IBrush Answered => Dynamic(0x2F6A9F, 0x5A9AD6);
    public static IBrush Flagged => Dynamic(0xC77700, 0xE7A23A);
    public static IBrush Unanswered => Dynamic(0xDBE1E7, 0x39414C);
    public static IBrush TimerSoft => Dynamic(0xFFF6E5, 0x3A2C12);

    // Exam, classic
    public static IBrush ClassicTitle => Dynamic(0x233A55, 0x1A2A3E);
    public static IBrush ClassicBar => Dynamic(0xDFE4EA, 0x2A3038);
    public static IBrush ClassicPage => Dynamic(0xEEF1F4, 0x181B20);
    public static IBrush ClassicStage => Dynamic(0xFFFFFF, 0x22262C);
    public static IBrush ClassicLine => Dynamic(0xC4CCD5, 0x3B434E);

    // System-like colors
    public static IBrush Text => Dynamic(0x1D1D1F, 0xEDEDED);
    public static IBrush Secondary => Dynamic(0x6B7280, 0x9BA3AF);
    public static IBrush Tertiary => Dynamic(0x9CA3AF, 0x6B7280);
    public static IBrush Control => Dynamic(0xF3F4F6, 0x262A30);
    public static IBrush Window => Dynamic(0xFFFFFF, 0x1E1E1E);
    public static IBrush Sidebar => Dynamic(0xF3F4F6, 0x202227);
    public static IBrush Quaternary => Dynamic(0xE5E7EB, 0x3A3F47);
    public static IBrush Accent => Dynamic(0x0A64D8, 0x3B8EEA);
    public static IBrush AccentSoft => IsDark ? Alpha(0x3B8EEA, 0.18) : Alpha(0x0A64D8, 0.12);
    public static IBrush Green => Dynamic(0x1F9D55, 0x4CC27C);
    public static IBrush Red => Dynamic(0xD92D20, 0xF07B70);
    public static IBrush Orange => Dynamic(0xE07B00, 0xF2A33A);
    public static IBrush Yellow => Dynamic(0xCA8A04, 0xEAB308);
    public static IBrush GreenSoft => IsDark ? Alpha(0x4CC27C, 0.16) : Alpha(0x1F9D55, 0.12);
    public static IBrush RedSoft => IsDark ? Alpha(0xF07B70, 0.16) : Alpha(0xD92D20, 0.10);
    public static IBrush OrangeSoft => IsDark ? Alpha(0xF2A33A, 0.16) : Alpha(0xE07B00, 0.10);
    public static IBrush White => Brushes.White;
    public static IBrush Clear => Brushes.Transparent;
}

public static class Fonts
{
    public static readonly FontFamily Mono = new("Cascadia Mono,Consolas,Courier New,monospace");
    /// <summary>The plain face testing-center software uses.</summary>
    public static readonly FontFamily Classic = new("Arial,Liberation Sans,Inter");
}
