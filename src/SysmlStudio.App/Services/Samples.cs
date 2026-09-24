namespace SysmlStudio.App.Services;

/// <summary>
/// The sample models shipped beside the application. An installer puts
/// them where the user cannot write (Program Files, the app bundle, /opt),
/// so a sample is opened from a copy in the user's documents, made the
/// first time and kept after that, edits and all.
/// </summary>
public static class Samples
{
    /// <summary>The sample the start page offers.</summary>
    public const string Default = "drone";

    /// <summary>Where the shipped samples are.</summary>
    public static string ShippedRoot => Path.Combine(AppContext.BaseDirectory, "samples");

    /// <summary>
    /// Where the copies go: "SysML Studio samples" in the user's documents,
    /// or under SYSML_STUDIO_HOME when that is set.
    /// </summary>
    public static string CopyRoot
        => Environment.GetEnvironmentVariable("SYSML_STUDIO_HOME") is { Length: > 0 } home
            ? Path.Combine(home, "samples")
            : Path.Combine(Documents(), "SysML Studio samples");

    public static bool Has(string name) => Directory.Exists(Path.Combine(ShippedRoot, name));

    /// <summary>The user's copy of a sample, made from the shipped one if there is none yet.</summary>
    public static string Prepare(string name)
    {
        var shipped = Path.Combine(ShippedRoot, name);
        var copy = Path.Combine(CopyRoot, name);
        if (!Directory.Exists(copy))
            CopyTree(shipped, copy);

        return copy;
    }

    private static string Documents()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return documents.Length > 0 ? documents : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static void CopyTree(string from, string to)
    {
        // Copy beside the target and move it into place, so a copy cut
        // short never looks like a finished one.
        var partial = to + ".partial";
        if (Directory.Exists(partial))
            Directory.Delete(partial, recursive: true);

        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(partial, Path.GetRelativePath(from, directory)));

        Directory.CreateDirectory(partial);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(partial, Path.GetRelativePath(from, file)));

        Directory.Move(partial, to);
    }
}
