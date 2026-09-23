using System.Text.Json;

namespace SysmlStudio.App.Services;

/// <summary>
/// Where the application keeps what belongs to the user rather than to any
/// model: the user's application data, or SYSML_STUDIO_HOME when that is set,
/// which the tests use so they never touch a real list.
/// </summary>
public static class AppHome
{
    public static string PathOf(string file) => System.IO.Path.Combine(
        Environment.GetEnvironmentVariable("SYSML_STUDIO_HOME") is { Length: > 0 } home
            ? home
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SysmlStudio"),
        file);
}

/// <summary>The user's settings: the theme, and every shortcut that differs from its default.</summary>
public sealed class StudioSettings
{
    // A file people may read and edit, so "+" is written as it is, not escaped.
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>"light" or "dark"; null follows the default.</summary>
    public string? Theme { get; set; }

    /// <summary>Shortcut id → gesture, only where the user changed it. An empty gesture turns the shortcut off.</summary>
    public Dictionary<string, string> Shortcuts { get; set; } = [];

    private static string StorePath => AppHome.PathOf("settings.json");

    public static StudioSettings Load()
    {
        try
        {
            return File.Exists(StorePath)
                ? JsonSerializer.Deserialize<StudioSettings>(File.ReadAllText(StorePath)) ?? new StudioSettings()
                : new StudioSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new StudioSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(this, Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Settings that cannot be written still apply until the window closes.
        }
    }
}
