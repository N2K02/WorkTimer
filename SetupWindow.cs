using System.Diagnostics;
using Microsoft.Win32;

namespace WorkTimer;

internal sealed class SetupWindow : Form
{
    private static readonly Icon? AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

    private readonly Button _btnOpenWidBar;
    private readonly Label _lblDevModeStatus;
    private readonly Button _btnDevMode;
    private readonly Label _lblWidBarStatus;
    private readonly Button _btnWidBar;
    private readonly Label _lblWidgetStatus;
    private readonly Button _btnWidget;
    private readonly Label _lblTrackerStatus;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    public SetupWindow()
    {
        Text = "WorkTimer - Setup & Status";
        Icon = AppIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;
        ClientSize = new Size(540, 520);
        BackColor = Color.FromArgb(32, 32, 32);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        // Header Panel
        var header = new Panel
        {
            Location = new Point(20, 20),
            Size = new Size(500, 56)
        };

        if (AppIcon is not null)
        {
            var iconBox = new PictureBox
            {
                Image = AppIcon.ToBitmap(),
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(0, 4),
                Size = new Size(48, 48)
            };
            header.Controls.Add(iconBox);
        }

        var titleLabel = new Label
        {
            Text = "WorkTimer",
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(58, 2),
            AutoSize = true
        };
        var subtitleLabel = new Label
        {
            Text = "Taskbar work hours widget for Windows 11",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(170, 170, 170),
            Location = new Point(60, 32),
            AutoSize = true
        };
        header.Controls.AddRange([titleLabel, subtitleLabel]);
        Controls.Add(header);

        // Primary Action: Open WidBar Button
        _btnOpenWidBar = new Button
        {
            Location = new Point(20, 88),
            Size = new Size(500, 48),
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnOpenWidBar.FlatAppearance.BorderSize = 0;
        _btnOpenWidBar.Click += (_, _) => LaunchWidBar();
        Controls.Add(_btnOpenWidBar);

        // Checklist Group / Cards
        int y = 152;
        int cardH = 64;

        // Card 1: Windows Developer Mode
        var (c1, s1, b1) = CreateCard("1. Developer Mode",
            "Allows Windows 11 to register local taskbar widget packages.",
            "Open Settings", (_, _) => Process.Start(new ProcessStartInfo("ms-settings:developers") { UseShellExecute = true }),
            new Point(20, y));
        _lblDevModeStatus = s1;
        _btnDevMode = b1;
        Controls.Add(c1);

        y += cardH + 10;
        // Card 2: WidBar Host App
        var (c2, s2, b2) = CreateCard("2. WidBar App",
            "Taskbar widget bar that hosts WorkTimer next to your system clock.",
            "Install (Store)", (_, _) => Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?productid=9PKLDNM83TP9") { UseShellExecute = true }),
            new Point(20, y));
        _lblWidBarStatus = s2;
        _btnWidBar = b2;
        Controls.Add(c2);

        y += cardH + 10;
        // Card 3: WorkTimer Widget Registration
        var (c3, s3, b3) = CreateCard("3. WorkTimer Widget",
            "WinUI 3 AppExtension registered in Windows for WidBar discovery.",
            "Register", (_, _) => { RegisterWidget(); RefreshStatus(); },
            new Point(20, y));
        _lblWidgetStatus = s3;
        _btnWidget = b3;
        Controls.Add(c3);

        y += cardH + 10;
        // Card 4: Background Tracker Engine
        var (c4, s4, _) = CreateCard("4. Background Engine",
            "Tracks 9-hour work time, handles idle sleep/lock, and syncs live.",
            "", null,
            new Point(20, y));
        _lblTrackerStatus = s4;
        Controls.Add(c4);

        // Footer Guidance
        var footer = new Label
        {
            Text = "Next step: Open WidBar > Layout, then drag WorkTimer to your taskbar.\nClosing this window keeps WorkTimer running in the background.",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(160, 160, 160),
            Location = new Point(20, 452),
            Size = new Size(380, 48)
        };
        Controls.Add(footer);

        var btnClose = new Button
        {
            Text = "Done",
            Location = new Point(420, 456),
            Size = new Size(100, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White
        };
        btnClose.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
        btnClose.Click += (_, _) => Hide();
        Controls.Add(btnClose);

        // Timer to periodically verify criteria live
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _refreshTimer.Tick += (_, _) => RefreshStatus();
        _refreshTimer.Start();

        Disposed += (_, _) => _refreshTimer.Dispose();

        RefreshStatus();
    }

    private static (Panel Card, Label StatusLabel, Button ActionButton) CreateCard(
        string title, string description, string buttonText, EventHandler? onClick, Point location)
    {
        var panel = new Panel
        {
            Location = location,
            Size = new Size(500, 64),
            BackColor = Color.FromArgb(42, 42, 42)
        };

        var titleLbl = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(14, 10),
            AutoSize = true
        };

        var statusLbl = new Label
        {
            Location = new Point(180, 10),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };

        var descLbl = new Label
        {
            Text = description,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(160, 160, 160),
            Location = new Point(14, 34),
            Size = new Size(350, 22)
        };

        panel.Controls.AddRange([titleLbl, statusLbl, descLbl]);

        var btn = new Button
        {
            Text = buttonText,
            Location = new Point(370, 15),
            Size = new Size(116, 32),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Visible = !string.IsNullOrEmpty(buttonText)
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        if (onClick is not null) btn.Click += onClick;
        panel.Controls.Add(btn);

        return (panel, statusLbl, btn);
    }

    private void RefreshStatus()
    {
        var devMode = IsDeveloperModeEnabled();
        var widBar = IsWidBarInstalled();
        var widget = IsWidgetRegistered();

        // 1. Developer Mode
        if (devMode)
        {
            _lblDevModeStatus.Text = "✓ Enabled";
            _lblDevModeStatus.ForeColor = Color.FromArgb(52, 211, 153); // Emerald
            _btnDevMode.Visible = false;
        }
        else
        {
            _lblDevModeStatus.Text = "⚠️ Disabled";
            _lblDevModeStatus.ForeColor = Color.FromArgb(251, 146, 60); // Orange
            _btnDevMode.Visible = true;
        }

        // 2. WidBar Host
        if (widBar)
        {
            _lblWidBarStatus.Text = "✓ Installed";
            _lblWidBarStatus.ForeColor = Color.FromArgb(52, 211, 153);
            _btnWidBar.Visible = false;
        }
        else
        {
            _lblWidBarStatus.Text = "⚠️ Not installed";
            _lblWidBarStatus.ForeColor = Color.FromArgb(251, 146, 60);
            _btnWidBar.Visible = true;
        }

        // 3. Widget Extension
        if (widget)
        {
            _lblWidgetStatus.Text = "✓ Registered";
            _lblWidgetStatus.ForeColor = Color.FromArgb(52, 211, 153);
            _btnWidget.Text = "Re-register";
            _btnWidget.Visible = false;
        }
        else
        {
            _lblWidgetStatus.Text = "⚠️ Missing";
            _lblWidgetStatus.ForeColor = Color.FromArgb(251, 146, 60);
            _btnWidget.Text = "Register";
            _btnWidget.Visible = true;
        }

        // 4. Tracker
        _lblTrackerStatus.Text = "✓ Active";
        _lblTrackerStatus.ForeColor = Color.FromArgb(52, 211, 153);

        // Top Button State
        var allReady = devMode && widBar && widget;
        if (allReady)
        {
            _btnOpenWidBar.Enabled = true;
            _btnOpenWidBar.Text = "✓ Open WidBar Taskbar Host";
            _btnOpenWidBar.BackColor = Color.FromArgb(16, 185, 129); // Modern Emerald Green
            _btnOpenWidBar.ForeColor = Color.White;
        }
        else
        {
            _btnOpenWidBar.Enabled = false;
            _btnOpenWidBar.Text = "Open WidBar (complete requirements below)";
            _btnOpenWidBar.BackColor = Color.FromArgb(50, 50, 50);
            _btnOpenWidBar.ForeColor = Color.FromArgb(140, 140, 140);
        }
    }

    private static bool IsDeveloperModeEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock");
            return key?.GetValue("AllowDevelopmentWithoutDevLicense") is int val && val == 1;
        }
        catch { return false; }
    }

    private static bool IsWidBarInstalled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            if (key is not null)
            {
                foreach (var name in key.GetSubKeyNames())
                {
                    if (name.Contains("WidBar", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static bool IsWidgetRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            if (key is not null)
            {
                foreach (var name in key.GetSubKeyNames())
                {
                    if (name.Contains("WorkTimer.Widget", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("ClockInTray.Widget", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static void LaunchWidBar()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", @"shell:AppsFolder\30113AndreaDelBello.WidBar-WidgetTaskbarsystem_2mj5svk4jk01m!App")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?productid=9PKLDNM83TP9") { UseShellExecute = true });
        }
    }

    private static void RegisterWidget()
    {
        try
        {
            var dir = AppContext.BaseDirectory;
            var manifest = Path.Combine(dir, "Widget", "AppxManifest.xml");
            if (!File.Exists(manifest))
            {
                for (var parent = new DirectoryInfo(dir); parent is not null; parent = parent.Parent)
                {
                    var candidate = Path.Combine(parent.FullName, "Widget", "bin", "x64", "Release", "net10.0-windows10.0.26100.0", "win-x64", "AppxManifest.xml");
                    if (File.Exists(candidate)) { manifest = candidate; break; }
                }
            }

            if (File.Exists(manifest))
            {
                var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"Add-AppxPackage -Register '{manifest}'\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi)?.WaitForExit(5000);
            }
        }
        catch { }
    }
}
