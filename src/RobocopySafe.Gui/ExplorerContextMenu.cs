using System.Runtime.InteropServices;
using Microsoft.Win32;
using RobocopySafe.Core;

namespace RobocopySafe.Gui;

internal static class ExplorerContextMenu
{
    private const string ClassesRoot = @"Software\Classes";
    private const string MenuKeyName = "RobocopySafeGUI";
    private const int MenuSchemaVersion = 3;
    private const uint ShellChangeAssociationChanged = 0x08000000;
    private const uint ShellNotifyIdList = 0x0000;

    private static readonly MenuLocation[] Locations =
    [
        new(@"Directory\shell", "%1", false),
        new(@"Drive\shell", "%1", false),
        new(@"Directory\Background\shell", "%V", true),
    ];

    public static bool IsInstalledFor(string executablePath)
    {
        try
        {
            var expectedPath = Path.GetFullPath(executablePath);
            return Locations.All(location =>
            {
                var rootPath = $@"{ClassesRoot}\{location.ParentKey}\{MenuKeyName}";
                var commandPath = $@"{rootPath}\shell\01-copy\command";
                using var root = Registry.CurrentUser.OpenSubKey(rootPath, writable: false);
                using var commandKey = Registry.CurrentUser.OpenSubKey(commandPath, writable: false);
                var command = commandKey?.GetValue(null) as string;
                return root?.GetValue("SchemaVersion") is int version &&
                       version == MenuSchemaVersion &&
                       command is not null &&
                       command.Contains(expectedPath, StringComparison.OrdinalIgnoreCase) &&
                       command.Contains(" --stage-source ", StringComparison.OrdinalIgnoreCase);
            });
        }
        catch (Exception ex) when (
            ex is IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException)
        {
            return false;
        }
    }

    public static void Install(string executablePath)
    {
        var fullExecutablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullExecutablePath))
        {
            throw new FileNotFoundException(AppText.Get(TextId.ContextTargetMissing), fullExecutablePath);
        }

        foreach (var location in Locations)
        {
            RegisterLocation(location, fullExecutablePath);
        }

        NotifyShell();
    }

    public static void Uninstall()
    {
        foreach (var location in Locations)
        {
            var path = $@"{ClassesRoot}\{location.ParentKey}\{MenuKeyName}";
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }

        NotifyShell();
    }

    private static void RegisterLocation(MenuLocation location, string executablePath)
    {
        var path = $@"{ClassesRoot}\{location.ParentKey}\{MenuKeyName}";
        Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);

        using var root = Registry.CurrentUser.CreateSubKey(path, writable: true)
            ?? throw new IOException(AppText.Format(TextId.RegistryCreateFailed, path));

        root.SetValue("MUIVerb", AppText.Get(TextId.ContextMenuTitle), RegistryValueKind.String);
        root.SetValue("Icon", $"{executablePath},0", RegistryValueKind.String);
        root.SetValue("SubCommands", string.Empty, RegistryValueKind.String);
        root.SetValue("Position", "Top", RegistryValueKind.String);
        root.SetValue("ExecutablePath", executablePath, RegistryValueKind.String);
        root.SetValue("SchemaVersion", MenuSchemaVersion, RegistryValueKind.DWord);

        // Selected-directory and background-directory verbs use separate localized labels.
        RegisterCommand(
            root,
            "01-copy",
            location.IsBackground ? AppText.Get(TextId.ContextCopyCurrentDirectory) : AppText.Get(TextId.ContextCopyThisDirectory),
            executablePath,
            "--stage-source",
            location.PathToken,
            "--mode copy");
        RegisterCommand(
            root,
            "02-cut",
            location.IsBackground ? AppText.Get(TextId.ContextCutCurrentDirectory) : AppText.Get(TextId.ContextCutThisDirectory),
            executablePath,
            "--stage-source",
            location.PathToken,
            "--mode move");
        RegisterCommand(
            root,
            "03-paste",
            location.IsBackground ? AppText.Get(TextId.ContextPasteCurrentDirectory) : AppText.Get(TextId.ContextPasteThisDirectory),
            executablePath,
            "--paste-to",
            location.PathToken,
            suffix: null,
            separatorBefore: true);
        RegisterCommand(
            root,
            "04-open",
            AppText.Get(TextId.ContextOpen),
            executablePath,
            "--source",
            location.PathToken,
            suffix: null,
            separatorBefore: true);
    }

    private static void RegisterCommand(
        RegistryKey root,
        string keyName,
        string title,
        string executablePath,
        string pathOption,
        string pathToken,
        string? suffix,
        bool separatorBefore = false)
    {
        using var verb = root.CreateSubKey($@"shell\{keyName}", writable: true)
            ?? throw new IOException(AppText.Format(TextId.ContextCommandCreateFailed, keyName));
        verb.SetValue("MUIVerb", title, RegistryValueKind.String);
        verb.SetValue("Icon", $"{executablePath},0", RegistryValueKind.String);
        if (separatorBefore)
        {
            verb.SetValue("CommandFlags", 0x20, RegistryValueKind.DWord);
        }

        using var command = verb.CreateSubKey("command", writable: true)
            ?? throw new IOException(AppText.Format(TextId.ContextCommandLineCreateFailed, keyName));
        var commandLine = $"\"{executablePath}\" {pathOption} \"{pathToken}\"";
        if (suffix is not null)
        {
            commandLine += " " + suffix;
        }

        command.SetValue(null, commandLine, RegistryValueKind.String);
    }

    private static void NotifyShell() =>
        SHChangeNotify(ShellChangeAssociationChanged, ShellNotifyIdList, IntPtr.Zero, IntPtr.Zero);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    private sealed record MenuLocation(string ParentKey, string PathToken, bool IsBackground);
}
