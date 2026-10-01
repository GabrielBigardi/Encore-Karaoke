using System.IO;
using System.Reflection;
using System.Text.Json;
using Encore.Core;

namespace Encore.Services;

public sealed class AppStorage
{
    public string DataFolder { get; }
    public string DemoFolder => Path.Combine(DataFolder, "Songs");
    public UserSettings Settings { get; private set; } = new();
    public string? Warning { get; private set; }
    private string SettingsPath => Path.Combine(DataFolder, "settings.json");
    public AppStorage(string? overrideFolder = null)
    {
        DataFolder = overrideFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Encore Karaoke");
        Directory.CreateDirectory(DataFolder);
        if (File.Exists(SettingsPath))
        {
            try { Settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath)) ?? new(); }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            { Warning = "Saved settings could not be read. Default settings are in use."; }
        }
        Settings.SongFolders ??= [];
        Settings.Favorites ??= [];
        Settings.History ??= [];
        Settings.Volume = Clamp(Settings.Volume, 0, 1, .7);
        Settings.NoiseGate = Clamp(Settings.NoiseGate, .001, .08, .015);
        Settings.LatencyMs = Clamp(Settings.LatencyMs, -300, 300, 0);
        Settings.Transpose = Math.Clamp(Settings.Transpose, -12, 12);
    }
    private static double Clamp(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    public void InstallDemos()
    {
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var resource in assembly.GetManifestResourceNames().Where(r => r.StartsWith("Encore.DemoSongs.", StringComparison.Ordinal)))
        {
            var relative = resource["Encore.DemoSongs.".Length..];
            // Demo directory names intentionally contain no dots.
            var separator = relative.IndexOf('.');
            if (separator < 0) continue;
            var folder = Path.Combine(DemoFolder, relative[..separator]);
            Directory.CreateDirectory(folder);
            var destination = Path.Combine(folder, relative[(separator + 1)..]);
            if (File.Exists(destination)) continue;
            using var input = assembly.GetManifestResourceStream(resource)!;
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
            input.CopyTo(output);
        }
    }
    public void Save()
    {
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, SettingsPath, true);
    }
    public void AddResult(PerformanceResult result)
    {
        Settings.History.Insert(0, result);
        if (Settings.History.Count > 500) Settings.History.RemoveRange(500, Settings.History.Count - 500);
        Save();
    }
}

public sealed record LibraryScan(IReadOnlyList<Song> Songs, IReadOnlyList<string> Errors);
public static class SongLibrary
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3", ".ogg", ".flac", ".m4a", ".aac", ".wma" };
    public static LibraryScan Scan(IEnumerable<string> folders)
    {
        var songs = new List<Song>();
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(folder)) { errors.Add($"Folder unavailable: {folder}"); continue; }
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
                foreach (var file in Directory.EnumerateFiles(folder, "*.txt", options))
                {
                    if (!seen.Add(Path.GetFullPath(file))) continue;
                    try
                    {
                        // Don't report unrelated readme or license text as broken song charts.
                        if (new FileInfo(file).Length > 8_000_000) { errors.Add($"Chart too large: {file}"); continue; }
                        var prefix = File.ReadLines(file).Take(40).ToList();
                        if (!prefix.Any(l => l.TrimStart('\uFEFF', ' ', '\t').StartsWith("#TITLE:", StringComparison.OrdinalIgnoreCase))) continue;
                        var song = ChartParser.ParseFile(file);
                        if (!File.Exists(song.AudioPath)) throw new FileNotFoundException($"Audio file not found: {Path.GetFileName(song.AudioPath)}");
                        if (!Extensions.Contains(Path.GetExtension(song.AudioPath))) throw new FormatException("Supported audio: WAV, MP3, OGG, FLAC, M4A, AAC and WMA.");
                        songs.Add(song);
                    }
                    catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException or ArgumentException)
                    { errors.Add($"{Path.GetFileName(Path.GetDirectoryName(file))}: {e.Message}"); }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { errors.Add($"{folder}: {e.Message}"); }
        }
        return new(songs.OrderBy(s => s.Artist == "Encore Originals" ? 0 : 1).ThenBy(s => s.Title).ToList(), errors);
    }
}
