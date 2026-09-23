using System.Text.Json;

namespace SysmlStudio.App.Services;

/// <summary>A model folder opened before, with how many files it held then.</summary>
public sealed record RecentFolder(string Path, int FileCount)
{
    /// <summary>What the start page calls it: "ferrix" for ferrix/docs/sysml.</summary>
    public string Name => ViewModels.ShellViewModel.DisplayName(Path);

    /// <summary>The path with the home folder written as "~", and forward slashes.</summary>
    public string ShortPath
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var path = home.Length > 0 && Path.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "~" + Path[home.Length..] : Path;
            return path.Replace('\\', '/');
        }
    }
}

/// <summary>
/// The folders opened lately, newest first, kept in the user's application
/// data rather than beside any model. A folder that has since disappeared is
/// dropped when the list is read.
/// </summary>
public static class RecentFolders
{
    private const int Limit = 8;

    /// <summary>
    /// Where the list lives: the user's application data, or SYSML_STUDIO_HOME
    /// when that is set, which the tests use so they never touch a real list.
    /// </summary>
    private static string StorePath => System.IO.Path.Combine(
        Environment.GetEnvironmentVariable("SYSML_STUDIO_HOME") is { Length: > 0 } home
            ? home
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SysmlStudio"),
        "recent.json");

    public static IReadOnlyList<RecentFolder> Load()
    {
        try
        {
            if (!File.Exists(StorePath))
                return [];

            var stored = JsonSerializer.Deserialize<List<RecentFolder>>(File.ReadAllText(StorePath)) ?? [];
            return [.. stored.Where(r => Directory.Exists(r.Path))];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static IReadOnlyList<RecentFolder> Remember(string path, int fileCount)
    {
        var list = Load().Where(r => !string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Insert(0, new RecentFolder(path, fileCount));
        list = [.. list.Take(Limit)];

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(list));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not remembering a folder is no reason to fail opening it.
        }

        return list;
    }
}
