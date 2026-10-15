using System.Text;

namespace MockiCore;

/// <summary>The exam library on disk: one folder per exam, holding exam.json and the images it uses.
/// Shared by the app and the mockiexam install command.</summary>
public sealed class LibraryStore
{
    public string Directory { get; }

    public LibraryStore(string? directory = null)
    {
        Directory = directory ?? DefaultDirectory;
        System.IO.Directory.CreateDirectory(Directory);
    }

    /// <summary>%APPDATA%\MockiExams\Library on Windows.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MockiExams", "Library");

    public sealed record Contents(List<Exam> Exams, List<string> Broken);

    public Contents Load()
    {
        MigrateFlatFiles();
        var contents = new Contents([], []);
        var entries = System.IO.Directory.Exists(Directory) ? System.IO.Directory.GetDirectories(Directory) : [];
        foreach (var entry in entries.OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var file = Path.Combine(entry, "exam.json");
            if (!File.Exists(file)) continue;
            try
            {
                contents.Exams.Add(ExamFormat.Decode(File.ReadAllBytes(file), entry));
            }
            catch (Exception e) when (e is ExamLoadException or IOException or UnauthorizedAccessException)
            {
                contents.Broken.Add($"{Path.GetFileName(entry)}: {e.Message}");
            }
        }
        contents.Exams.Sort((a, b) => NaturalCompare(a.Title, b.Title));
        return contents;
    }

    /// <summary>Validates and copies an exam in. source is an exam JSON file or a folder holding
    /// exam.json; referenced images are resolved relative to it. Replaces an exam with the same id.</summary>
    public Exam Install(string source)
    {
        var isDir = System.IO.Directory.Exists(source);
        if (!isDir && !File.Exists(source))
            throw new ExamLoadException([new Issue(Severity.Error, "$", $"No such file: {source}")]);
        var file = isDir ? ExamFile(source) : source;
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(file))!;
        var exam = ExamFormat.Decode(File.ReadAllBytes(file), baseDir);

        var staging = Path.Combine(Directory, $".staging-{Guid.NewGuid()}");
        try
        {
            System.IO.Directory.CreateDirectory(staging);
            File.Copy(file, Path.Combine(staging, "exam.json"));
            foreach (var path in exam.AssetPaths)
            {
                var dest = Path.Combine(staging, path);
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(Path.Combine(baseDir, path), dest);
            }
            var target = Path.Combine(Directory, FolderName(exam.Id));
            if (System.IO.Directory.Exists(target)) System.IO.Directory.Delete(target, true);
            System.IO.Directory.Move(staging, target);
            exam.AssetBase = target;
            return exam;
        }
        finally
        {
            if (System.IO.Directory.Exists(staging)) System.IO.Directory.Delete(staging, true);
        }
    }

    /// <summary>v1 stored exams as flat &lt;id&gt;.exam.json files; move each into its own folder.</summary>
    internal void MigrateFlatFiles()
    {
        foreach (var file in System.IO.Directory.GetFiles(Directory, "*.json"))
        {
            Exam exam;
            try { exam = ExamFormat.Decode(File.ReadAllBytes(file)); }
            catch (Exception) { continue; }
            var target = Path.Combine(Directory, FolderName(exam.Id));
            try
            {
                if (File.Exists(Path.Combine(target, "exam.json"))) File.Delete(file);
                else
                {
                    System.IO.Directory.CreateDirectory(target);
                    File.Move(file, Path.Combine(target, "exam.json"));
                }
            }
            catch (IOException) { }
        }
    }

    public void Remove(string examId)
    {
        var target = Path.Combine(Directory, FolderName(examId));
        if (System.IO.Directory.Exists(target)) System.IO.Directory.Delete(target, true);
    }

    /// <summary>exam.json in a folder, or its only .json file.</summary>
    internal static string ExamFile(string folder)
    {
        var named = Path.Combine(folder, "exam.json");
        if (File.Exists(named)) return named;
        var jsons = System.IO.Directory.GetFiles(folder, "*.json");
        if (jsons.Length != 1)
            throw new ExamLoadException([new Issue(Severity.Error, "$",
                $"Folder {Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, '/'))} needs an exam.json (found {jsons.Length} .json files).")]);
        return jsons[0];
    }

    public static string FolderName(string id)
    {
        var sb = new StringBuilder();
        foreach (var rune in id.EnumerateRunes())
            sb.Append(Text2.IsAlphanumeric(rune) || rune.Value == '-' ? rune.ToString() : "_");
        return sb.ToString();
    }

    /// <summary>Finder-style ordering: case-insensitive, with numbers compared by value.</summary>
    public static int NaturalCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a[si..i].TrimStart('0');
                var nb = b[sj..j].TrimStart('0');
                var c = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : string.CompareOrdinal(na, nb);
                if (c != 0) return c;
            }
            else
            {
                var c = string.Compare(a[i].ToString(), b[j].ToString(), StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                i++; j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}
