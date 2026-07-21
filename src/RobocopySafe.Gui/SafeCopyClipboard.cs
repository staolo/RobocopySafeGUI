using System.Collections.Specialized;
using System.Runtime.InteropServices;
using RobocopySafe.Core;

namespace RobocopySafe.Gui;

internal sealed record ClipboardPasteRequest(
    string Source,
    string Destination,
    CopyOperation Operation);

internal static class SafeCopyClipboard
{
    private const string PreferredDropEffectFormat = "Preferred DropEffect";
    private const int DropEffectCopy = 1;
    private const int DropEffectMove = 2;

    public static void Stage(string source, CopyOperation operation)
    {
        var normalizedSource = NormalizeExistingDirectory(source, AppText.Get(TextId.SourceDirectoryName));
        var data = CreateDataObject(normalizedSource, operation);
        Clipboard.SetDataObject(data, copy: true, retryTimes: 10, retryDelay: 100);
    }

    public static ClipboardPasteRequest ReadPasteRequest(string target)
    {
        var data = Clipboard.GetDataObject()
            ?? throw new InvalidOperationException(AppText.Get(TextId.ClipboardNoPasteDirectory));
        return ReadPasteRequest(data, target);
    }

    internal static DataObject CreateDataObject(string source, CopyOperation operation)
    {
        var data = new DataObject();
        var files = new StringCollection { source };
        data.SetFileDropList(files);
        var effect = operation == CopyOperation.Move ? DropEffectMove : DropEffectCopy;
        data.SetData(PreferredDropEffectFormat, new MemoryStream(BitConverter.GetBytes(effect)));
        return data;
    }

    internal static ClipboardPasteRequest ReadPasteRequest(IDataObject data, string target)
    {
        var source = ReadSingleDirectory(data);
        var operation = ReadOperation(data);
        var normalizedTarget = NormalizeExistingDirectory(target, AppText.Get(TextId.PasteTargetDirectoryName));
        var sourceName = GetDirectoryName(source);
        var destination = Path.Combine(normalizedTarget, sourceName);

        if (PathEquals(source, destination))
        {
            if (operation == CopyOperation.Move)
            {
                throw new InvalidOperationException(AppText.Get(TextId.MoveIntoSelf));
            }

            destination = FindAvailableCopyPath(normalizedTarget, sourceName);
        }

        return new ClipboardPasteRequest(source, destination, operation);
    }

    public static bool TryClearIfSourceMatches(string source)
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null || !PathEquals(ReadSinglePath(data), source))
            {
                return false;
            }

            Clipboard.Clear();
            return true;
        }
        catch (Exception ex) when (
            ex is ExternalException or
                InvalidOperationException or
                ArgumentException or
                IOException or
                UnauthorizedAccessException)
        {
            return false;
        }
    }


    private static string ReadSingleDirectory(IDataObject data)
    {
        var path = ReadSinglePath(data);
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(AppText.Format(TextId.ClipboardSourceMissing, path));
        }

        return path;
    }

    private static string ReadSinglePath(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop, autoConvert: false) ||
            data.GetData(DataFormats.FileDrop, autoConvert: false) is not string[] paths ||
            paths.Length == 0)
        {
            throw new InvalidOperationException(
                AppText.Get(TextId.ClipboardNoDirectory));
        }

        if (paths.Length != 1)
        {
            throw new InvalidOperationException(AppText.Format(TextId.ClipboardMultipleItems, paths.Length));
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths[0].Trim()));
    }

    private static CopyOperation ReadOperation(IDataObject data)
    {
        var raw = data.GetData(PreferredDropEffectFormat, autoConvert: false);
        var effect = raw switch
        {
            MemoryStream memory when memory.Length >= sizeof(int) =>
                BitConverter.ToInt32(memory.ToArray(), 0),
            byte[] bytes when bytes.Length >= sizeof(int) =>
                BitConverter.ToInt32(bytes, 0),
            _ => DropEffectCopy,
        };
        return (effect & DropEffectMove) != 0 ? CopyOperation.Move : CopyOperation.Copy;
    }

    private static string FindAvailableCopyPath(string target, string sourceName)
    {
        var candidate = Path.Combine(target, AppText.Format(TextId.CopySuffix, sourceName));
        for (var index = 2; Directory.Exists(candidate) || File.Exists(candidate); index++)
        {
            candidate = Path.Combine(target, AppText.Format(TextId.CopySuffixIndexed, sourceName, index));
        }

        return candidate;
    }

    private static string GetDirectoryName(string source)
    {
        var normalized = Path.TrimEndingDirectorySeparator(source);
        var name = Path.GetFileName(normalized);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var rootName = (Path.GetPathRoot(source) ?? "Drive")
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .TrimEnd(':');
        return string.IsNullOrWhiteSpace(rootName) ? "Drive" : $"{rootName}-Drive";
    }

    private static string NormalizeExistingDirectory(string value, string displayName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(AppText.Format(TextId.SelectNamedDirectory, displayName));
        }

        var fullPath = Path.GetFullPath(value.Trim());
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(AppText.Format(TextId.NamedDirectoryMissing, displayName, fullPath));
        }

        return Path.TrimEndingDirectorySeparator(fullPath);
    }


    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
}
