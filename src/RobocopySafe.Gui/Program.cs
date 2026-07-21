using RobocopySafe.Core;

namespace RobocopySafe.Gui;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        try
        {
            var preferences = SettingsStore.Load();
            AppText.SetLanguage(preferences.Language);
            var options = StartupOptions.Parse(args);
            var executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException(AppText.Get(TextId.ProcessPathUnavailable));
            var executableName = Path.GetFileName(executablePath);

            if (options.ShowHelp)
            {
                MessageBox.Show(
                    AppText.Format(TextId.HelpText, executableName),
                    AppText.Get(TextId.WindowTitle),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return 0;
            }

            if (options.ShowVersion)
            {
                var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown";
                MessageBox.Show(
                    AppText.Format(TextId.VersionText, AppText.Get(TextId.WindowTitle), version),
                    AppText.Get(TextId.WindowTitle),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return 0;
            }

            if (options.InstallContextMenu)
            {
                ExplorerContextMenu.Install(executablePath);
                return 0;
            }

            if (options.UninstallContextMenu)
            {
                ExplorerContextMenu.Uninstall();
                return 0;
            }

            if (options.StageClipboard)
            {
                SafeCopyClipboard.Stage(
                    options.Source ?? throw new ArgumentException(AppText.Format(TextId.OptionMissingValue, "--stage-source")),
                    options.Operation ?? throw new ArgumentException(AppText.Format(TextId.OptionMissingValue, "--mode")));
                return 0;
            }

            if (options.PasteTarget is not null)
            {
                var request = SafeCopyClipboard.ReadPasteRequest(options.PasteTarget);
                options = options with
                {
                    Source = request.Source,
                    Destination = request.Destination,
                    Operation = request.Operation,
                    ClipboardPaste = true,
                };
            }

            using var mainForm = new MainForm(options, preferences);
            Application.Run(mainForm);
            return 0;
        }
        catch (Exception ex) when (
            ex is ArgumentException or
                IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException or
                System.Runtime.InteropServices.ExternalException or
                InvalidOperationException)
        {
            MessageBox.Show(
                ex.Message,
                AppText.Get(TextId.WindowTitle),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }
}
