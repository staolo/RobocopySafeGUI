using RobocopySafe.Core;

namespace RobocopySafe.Gui;

internal sealed record StartupOptions
{
    public string? Source { get; init; }

    public string? Destination { get; init; }

    public CopyOperation? Operation { get; init; }

    public bool InstallContextMenu { get; init; }

    public bool UninstallContextMenu { get; init; }

    public bool StageClipboard { get; init; }

    public string? PasteTarget { get; init; }

    public bool ClipboardPaste { get; init; }

    public bool ShowHelp { get; init; }

    public bool ShowVersion { get; init; }

    public static StartupOptions Parse(IReadOnlyList<string> arguments)
    {
        string? source = null;
        string? destination = null;
        CopyOperation? operation = null;
        var installContextMenu = false;
        var uninstallContextMenu = false;
        var stageClipboard = false;
        string? pasteTarget = null;
        var showHelp = false;
        var showVersion = false;

        for (var index = 0; index < arguments.Count; index++)
        {
            switch (arguments[index].ToLowerInvariant())
            {
                case "--source":
                    source = ReadValue(arguments, ref index, "--source");
                    break;
                case "--destination":
                    destination = ReadValue(arguments, ref index, "--destination");
                    break;
                case "--stage-source":
                    source = ReadValue(arguments, ref index, "--stage-source");
                    stageClipboard = true;
                    break;
                case "--paste-to":
                    pasteTarget = ReadValue(arguments, ref index, "--paste-to");
                    break;
                case "--mode":
                    operation = ReadValue(arguments, ref index, "--mode").ToLowerInvariant() switch
                    {
                        "copy" => CopyOperation.Copy,
                        "move" => CopyOperation.Move,
                        _ => throw new ArgumentException(AppText.Get(TextId.ModeOnlyCopyMove)),
                    };
                    break;
                case "--install-context-menu":
                    installContextMenu = true;
                    break;
                case "--uninstall-context-menu":
                    uninstallContextMenu = true;
                    break;
                case "--help":
                case "-h":
                case "/?":
                    showHelp = true;
                    break;
                case "--version":
                    showVersion = true;
                    break;
                default:
                    throw new ArgumentException(AppText.Format(TextId.UnknownOption, arguments[index]));
            }
        }

        var specialActionCount =
            (installContextMenu ? 1 : 0) +
            (uninstallContextMenu ? 1 : 0) +
            (stageClipboard ? 1 : 0) +
            (pasteTarget is null ? 0 : 1) +
            (showHelp ? 1 : 0) +
            (showVersion ? 1 : 0);
        if (specialActionCount > 1)
        {
            throw new ArgumentException(AppText.Get(TextId.SpecialActionsConflict));
        }

        if (stageClipboard && operation is null)
        {
            throw new ArgumentException(AppText.Get(TextId.StageRequiresMode));
        }

        if (pasteTarget is not null && (source is not null || destination is not null || operation is not null))
        {
            throw new ArgumentException(AppText.Get(TextId.PasteCannotCombine));
        }

        return new StartupOptions
        {
            Source = source,
            Destination = destination,
            Operation = operation,
            InstallContextMenu = installContextMenu,
            UninstallContextMenu = uninstallContextMenu,
            StageClipboard = stageClipboard,
            PasteTarget = pasteTarget,
            ShowHelp = showHelp,
            ShowVersion = showVersion,
        };
    }

    private static string ReadValue(IReadOnlyList<string> arguments, ref int index, string option)
    {
        if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
        {
            throw new ArgumentException(AppText.Format(TextId.OptionMissingValue, option));
        }

        return arguments[index];
    }
}
