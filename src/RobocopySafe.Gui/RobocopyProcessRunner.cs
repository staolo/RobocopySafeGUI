using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using RobocopySafe.Core;

namespace RobocopySafe.Gui;

internal sealed record RobocopyProgress(
    int? CurrentFilePercent,
    long CompletedItems,
    long OutputLines,
    string? CurrentItem);

internal sealed partial class RobocopyProcessRunner : IDisposable
{
    private static readonly Encoding RobocopyOutputEncoding = CreateRobocopyOutputEncoding();
    private readonly object processLock = new();
    private readonly object progressLock = new();
    private Process? process;
    private long completedItems;
    private long outputLines;
    private string? currentItem;

    public event Action<string>? OutputReceived;

    public event Action<RobocopyProgress>? ProgressChanged;

    public async Task<int> RunAsync(RobocopyPlan plan, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "robocopy.exe"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = RobocopyOutputEncoding,
            StandardErrorEncoding = RobocopyOutputEncoding,
        };

        foreach (var argument in plan.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var localProcess = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        lock (progressLock)
        {
            completedItems = 0;
            outputLines = 0;
            currentItem = null;
        }

        lock (processLock)
        {
            process = localProcess;
        }

        try
        {
            if (!localProcess.Start())
            {
                throw new InvalidOperationException(AppText.Get(TextId.CannotStartRobocopy));
            }

            var standardOutputTask = PumpOutputAsync(localProcess.StandardOutput);
            var standardErrorTask = PumpOutputAsync(localProcess.StandardError);

            using var registration = cancellationToken.Register(Cancel);
            await localProcess.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            return localProcess.ExitCode;
        }
        finally
        {
            lock (processLock)
            {
                process = null;
            }

            localProcess.Dispose();
        }
    }

    public void Cancel()
    {
        lock (processLock)
        {
            if (process is null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // The process exited between the state check and Kill.
            }
        }
    }

    public void Dispose() => Cancel();

    private async Task PumpOutputAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        var line = new StringBuilder(512);
        var previousWasCarriageReturn = false;

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            for (var index = 0; index < read; index++)
            {
                var character = buffer[index];
                if (character is '\r' or '\n')
                {
                    if (character == '\n' && previousWasCarriageReturn)
                    {
                        previousWasCarriageReturn = false;
                        continue;
                    }

                    HandleOutput(line.ToString());
                    line.Clear();
                    previousWasCarriageReturn = character == '\r';
                    continue;
                }

                previousWasCarriageReturn = false;
                line.Append(character);
            }
        }

        HandleOutput(line.ToString());
    }

    private void HandleOutput(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var match = PercentRegex().Match(line);
        int? percent = null;
        if (match.Success)
        {
            var percentageText = match.Groups[1].Value.Replace(',', '.');
            if (double.TryParse(
                    percentageText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsedPercent))
            {
                percent = Math.Clamp((int)Math.Round(parsedPercent), 0, 100);
            }
        }

        RobocopyProgress progress;
        lock (progressLock)
        {
            outputLines++;
            if (percent == 100)
            {
                completedItems++;
            }

            if (percent is null)
            {
                currentItem = line.Trim();
            }

            progress = new RobocopyProgress(percent, completedItems, outputLines, currentItem);
        }

        ProgressChanged?.Invoke(progress);

        // Robocopy rewrites percentage updates with carriage returns. They feed the live
        // status only; appending them to a RichTextBox would overwhelm the UI message loop.
        if (percent is null)
        {
            OutputReceived?.Invoke(line);
        }
    }

    private static Encoding CreateRobocopyOutputEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(checked((int)GetOEMCP()));
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    [GeneratedRegex(@"^\s*(\d+(?:[.,]\d+)?)%\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex PercentRegex();
}
