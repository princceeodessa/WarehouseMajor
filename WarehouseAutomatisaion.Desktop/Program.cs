using System.Text;
using WarehouseAutomatisaion.Desktop.Controls;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        System.Windows.Forms.Application.ThreadException += (_, args) => LogUnhandledException(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                LogUnhandledException(exception);
            }
        };

        try
        {
            var launchScreen = new LaunchScreenForm();
            var formStarted = false;

            launchScreen.Shown += (_, _) =>
            {
                if (formStarted)
                {
                    return;
                }

                formStarted = true;
                launchScreen.BeginInvoke(() =>
                {
                    try
                    {
                        var salesWorkspaceStore = SalesWorkspaceStore.CreateDefault();
                        var salesWorkspace = SalesWorkspace.Create(Environment.UserName);
                        var mainForm = new Form1(salesWorkspaceStore, salesWorkspace);
                        mainForm.FormClosed += (_, _) =>
                        {
                            if (!launchScreen.IsDisposed)
                            {
                                launchScreen.Close();
                            }
                        };

                        mainForm.Show();
                        mainForm.BringToFront();
                        mainForm.Activate();
                        launchScreen.Hide();
                    }
                    catch (Exception exception)
                    {
                        LogUnhandledException(exception);
                        MessageBox.Show(
                            launchScreen,
                            $"Не удалось открыть основное окно.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                            "Мажор Flow",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);

                        if (!launchScreen.IsDisposed)
                        {
                            launchScreen.Close();
                        }
                    }
                });
            };

            System.Windows.Forms.Application.Run(launchScreen);
        }
        catch (Exception exception)
        {
            LogUnhandledException(exception);
            MessageBox.Show(
                $"Не удалось запустить desktop-приложение.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Мажор Flow",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void LogUnhandledException(Exception exception)
    {
        try
        {
            var root = WorkspacePathResolver.ResolveWorkspaceRoot();
            var directory = Path.Combine(root, "app_data");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "desktop-startup-error.log");
            File.AppendAllText(
                path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
        }
    }
}

internal sealed class LaunchScreenForm : Form
{
    public LaunchScreenForm()
    {
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = DesktopTheme.AppBackground;
        ClientSize = new Size(560, 220);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Мажор Flow";
        TopMost = true;

        var titleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 72,
            Padding = new Padding(28, 24, 28, 0),
            Text = "Запуск desktop-приложения",
            Font = DesktopTheme.TitleFont(18f),
            ForeColor = DesktopTheme.TextPrimary
        };

        var subtitleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 56,
            Padding = new Padding(28, 0, 28, 0),
            Text = "Открываю рабочее окно. Если база грузится долго, этот экран останется видимым.",
            Font = DesktopTheme.BodyFont(10.5f),
            ForeColor = DesktopTheme.TextSecondary
        };

        var progressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 10,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 24,
            Margin = new Padding(0)
        };

        var progressHost = new Panel
        {
            Dock = DockStyle.Top,
            Height = 32,
            Padding = new Padding(28, 8, 28, 0)
        };
        progressHost.Controls.Add(progressBar);

        Controls.Add(progressHost);
        Controls.Add(subtitleLabel);
        Controls.Add(titleLabel);
    }
}
