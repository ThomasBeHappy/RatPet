using System;
using System.Windows;
using System.Windows.Forms;
using System.IO;

namespace RatPet;

public partial class App : System.Windows.Application
{
    private NotifyIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _tray = new NotifyIcon
        {
            Text = "RatPet",
            Visible = true,
        };

        // Prefer icon.ico if provided, else fall back to app icon
        try
        {
            var icoPath = Path.Combine(AppContext.BaseDirectory, "icon.ico");
            if (File.Exists(icoPath))
            {
                using var fs = File.OpenRead(icoPath);
                _tray.Icon = new System.Drawing.Icon(fs);
            }
            else
            {
                _tray.Icon = System.Drawing.SystemIcons.Application;
            }
        }
        catch { _tray.Icon = System.Drawing.SystemIcons.Application; }

        var menu = new ContextMenuStrip();
        //menu.Items.Add("Feed a crumb", null, (_, _) => (System.Windows.Application.Current.MainWindow as MainWindow)?.FeedCrumb());
        var muteItem = new ToolStripMenuItem("Mute") { Checked = false, CheckOnClick = true };
        muteItem.CheckedChanged += (_, _) => (System.Windows.Application.Current.MainWindow as MainWindow)?.SetMuted(muteItem.Checked);
        //menu.Items.Add(muteItem);
        var funItem = new ToolStripMenuItem("Fun mode (steal cursor)") { Checked = false, CheckOnClick = true };
        funItem.CheckedChanged += (_, _) => (System.Windows.Application.Current.MainWindow as MainWindow)?.SetFunMode(funItem.Checked);
        menu.Items.Add(funItem);
        var sneakItem = new ToolStripMenuItem("Sneak behind windows") { Checked = true, CheckOnClick = true };
        sneakItem.CheckedChanged += (_, _) => (System.Windows.Application.Current.MainWindow as MainWindow)?.SetSneakEnabled(sneakItem.Checked);
        menu.Items.Add(sneakItem);
        menu.Items.Add("Throw toy", null, (_, _) => (System.Windows.Application.Current.MainWindow as MainWindow)?.ThrowToy());
        menu.Items.Add("Settings...", null, (_, _) =>
        {
            if (System.Windows.Application.Current.MainWindow is MainWindow mw)
            {
                var existing = System.Windows.Application.Current.Windows
                    .OfType<CustomizeWindow>()
                    .FirstOrDefault();
                if (existing != null)
                {
                    existing.Owner = mw;
                    if (existing.WindowState == System.Windows.WindowState.Minimized) existing.WindowState = System.Windows.WindowState.Normal;
                    existing.Activate();
                    existing.Focus();
                }
                else
                {
                    var dlg = new CustomizeWindow(mw) { Owner = mw };
                    dlg.Show();
                }
            }
        });
        var consoleItem = new ToolStripMenuItem("Show debug console") { Checked = false, CheckOnClick = true };
        consoleItem.CheckedChanged += (_, _) => (System.Windows.Application.Current.MainWindow as MainWindow)?.ToggleDebugConsole(consoleItem.Checked);
        menu.Items.Add(consoleItem);
        var mischiefItem = new ToolStripMenuItem("Mischief: minimize windows") { Checked = false, CheckOnClick = true };
        mischiefItem.CheckedChanged += (_, _) =>
        {
            var mw = System.Windows.Application.Current.MainWindow as MainWindow;
            if (mw != null)
            {
                // expose via a safe setter
                mw.GetType().GetMethod("SetMischiefEnabled")?.Invoke(mw, new object[] { mischiefItem.Checked });
            }
        };
        menu.Items.Add(mischiefItem);
        var judgmentItem = new ToolStripMenuItem("Browser judgment mode") { Checked = false, CheckOnClick = true };
        judgmentItem.CheckedChanged += (_, _) =>
        {
            var mw = System.Windows.Application.Current.MainWindow as MainWindow;
            if (mw != null)
            {
                mw.GetType().GetMethod("SetBrowserJudgmentEnabled")?.Invoke(mw, new object[] { judgmentItem.Checked });
            }
        };
        menu.Items.Add(judgmentItem);
        var chaosItem = new ToolStripMenuItem("CHAOS MODE >:3") { Checked = false, CheckOnClick = true };
        chaosItem.CheckedChanged += (_, _) =>
        {
            var mw = System.Windows.Application.Current.MainWindow as MainWindow;
            if (mw != null)
            {
                mw.GetType().GetMethod("SetChaosModeEnabled")?.Invoke(mw, new object[] { chaosItem.Checked });
            }
        };
        menu.Items.Add(chaosItem);
        var uiaItem = new ToolStripMenuItem("Try to read browser URL (UIA)") { Checked = false, CheckOnClick = true };
        uiaItem.CheckedChanged += (_, _) =>
        {
            var mw = System.Windows.Application.Current.MainWindow as MainWindow;
            if (mw != null)
            {
                mw.GetType().GetMethod("SetBrowserUIAEnabled")?.Invoke(mw, new object[] { uiaItem.Checked });
            }
        };
        menu.Items.Add(uiaItem);
        var badWebsiteItem = new ToolStripMenuItem("Bad website detection") { Checked = false, CheckOnClick = true };
        badWebsiteItem.CheckedChanged += (_, _) =>
        {
            var mw = System.Windows.Application.Current.MainWindow as MainWindow;
            if (mw != null)
            {
                mw.GetType().GetMethod("SetBadWebsiteDetection")?.Invoke(mw, new object[] { badWebsiteItem.Checked });
            }
        };
        menu.Items.Add(badWebsiteItem);
        var photoItem = new ToolStripMenuItem("Photojournalist mode") { Checked = false, CheckOnClick = true };
        photoItem.CheckedChanged += (_, _) =>
        {
            var mw = System.Windows.Application.Current.MainWindow as MainWindow;
            if (mw != null)
            {
                mw.GetType().GetMethod("SetPhotojournalistEnabled")?.Invoke(mw, new object[] { photoItem.Checked });
            }
        };
        menu.Items.Add(photoItem);
        menu.Items.Add("Open Photos...", null, (_, _) =>
        {
            if (System.Windows.Application.Current.MainWindow is MainWindow mw)
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = mw.GetPhotosFolder(), UseShellExecute = true }); } catch { }
            }
        });
        menu.Items.Add("Open Rat Diary...", null, (_, _) =>
        {
            if (System.Windows.Application.Current.MainWindow is MainWindow mw)
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = mw.GetDiaryFolder(), UseShellExecute = true }); } catch { }
            }
        });
        menu.Items.Add("Generate Stats Report", null, (_, _) =>
        {
            if (System.Windows.Application.Current.MainWindow is MainWindow mw)
            {
                mw.GenerateRatStatsReport();
            }
        });
        menu.Items.Add("Open Rat Stats...", null, (_, _) =>
        {
            if (System.Windows.Application.Current.MainWindow is MainWindow mw)
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = mw.GetStatsFolder(), UseShellExecute = true }); } catch { }
            }
        });
        menu.Items.Add("Inventory", null, (_, _) => (System.Windows.Application.Current.MainWindow as MainWindow)?.OpenInventory());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => System.Windows.Application.Current.Shutdown());

        _tray.ContextMenuStrip = menu;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnExit(e);
    }

    // Simple system notification via tray balloon (Action Center)
    public void ShowTrayNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info, int timeoutMs = 4000)
    {
        try
        {
            if (_tray == null)
            {
                _tray = new NotifyIcon { Visible = true, Text = "RatPet", Icon = System.Drawing.SystemIcons.Application };
            }
            _tray.BalloonTipTitle = title;
            _tray.BalloonTipText = message;
            _tray.BalloonTipIcon = icon;
            _tray.ShowBalloonTip(timeoutMs);
        }
        catch { }
    }
}

