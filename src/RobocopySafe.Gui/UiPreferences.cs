using System.Text.Json;
using RobocopySafe.Core;

namespace RobocopySafe.Gui;

internal sealed record UiPreferences
{
    public UiLanguage Language { get; init; } = UiLanguage.System;

    public string Source { get; init; } = string.Empty;

    public string Destination { get; init; } = string.Empty;

    public bool ExcludeHiddenSystemFiles { get; init; } = true;

    public bool ExcludeHiddenSystemDirectories { get; init; } = true;

    public LinkHandling LinkHandling { get; init; } = LinkHandling.Skip;

    public string ExcludedFilePatterns { get; init; } = string.Empty;

    public string ExcludedDirectoryPatterns { get; init; } = "$RECYCLE.BIN;System Volume Information";

    public int Threads { get; init; } = Math.Clamp(Environment.ProcessorCount, 4, 16);

    public int Retries { get; init; } = 2;

    public int RetryWaitSeconds { get; init; } = 2;

    public CopyOperation Operation { get; init; } = CopyOperation.Copy;
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RobocopySafeGUI");

    private static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static UiPreferences Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(SettingsPath), SerializerOptions) ?? new UiPreferences()
                : new UiPreferences();
        }
        catch (JsonException)
        {
            return new UiPreferences();
        }
        catch (IOException)
        {
            return new UiPreferences();
        }
        catch (UnauthorizedAccessException)
        {
            return new UiPreferences();
        }
        catch (System.Security.SecurityException)
        {
            return new UiPreferences();
        }
    }

    public static void Save(UiPreferences preferences)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(preferences, SerializerOptions));
    }
}
