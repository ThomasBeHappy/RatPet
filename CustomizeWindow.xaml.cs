using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using WF = System.Windows.Forms;
using WpfCheckBox = System.Windows.Controls.CheckBox;

namespace RatPet;

public partial class CustomizeWindow : Window
{
    private readonly MainWindow _main;
    private static string SettingsFilePath => Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        "RatPet", "settings.json");

    private string? _idle;
    private string? _move;
    private string? _sleep;

    public CustomizeWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        BtnGray.Click += (_, _) => ApplySkin("idle.png", "move.png", "sleep.png");
        BtnWhite.Click += (_, _) => ApplySkin("white-idle.png", "white-rat.png", "white-sleep.png");
        // Initialize sliders from current values
        SldScale.Value = _main.GetScale();
        SldSpeed.Value = _main.GetBaseSpeed();
        SldSneak.Value = _main.GetSneakChance();
        SldMischief.Value = _main.GetMischiefChance();
        // Populate monitors UI
        BuildMonitorList();
        // Load persisted monitor selection
        LoadAndApplySavedMonitors();
        // Wire live updates
        SldScale.ValueChanged += (_, e) => _main.SetScale(e.NewValue);
        SldSpeed.ValueChanged += (_, e) => _main.SetBaseSpeed(e.NewValue);
        SldSneak.ValueChanged += (_, e) => _main.SetSneakChance(e.NewValue);
        SldMischief.ValueChanged += (_, e) => _main.SetMischiefChance(e.NewValue);
    }

    private void ApplySkin(string idle, string move, string sleep)
    {
        _idle = idle;
        _move = move;
        _sleep = sleep;
        _main.ApplySkin(idle, move, sleep);
        if (ChkRemember.IsChecked == true)
        {
            var dir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(new { Idle = idle, Move = move, Sleep = sleep });
            File.WriteAllText(SettingsFilePath, json);
        }
    }

    public static void LoadSavedSkin(MainWindow main)
    {
        try
        {
            var path = SettingsFilePath;
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            var obj = JsonSerializer.Deserialize<SkinSettings>(json);
            if (obj == null) return;
            main.ApplySkin(obj.idle, obj.move, obj.sleep);
        }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (ChkRemember.IsChecked == true) SaveSettingsPreservingUnknown();
        base.OnClosed(e);
    }

    private record SkinSettings(string idle, string move, string sleep);

    private void BuildMonitorList()
    {
        if (MonitorPanel == null) return;
        MonitorPanel.Children.Clear();
        var screens = WF.Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            var cb = new WpfCheckBox
            {
                Content = s.Primary ? $"Monitor {i + 1} (Primary)" : $"Monitor {i + 1}",
                Margin = new Thickness(0, 2, 0, 2),
                Tag = s.DeviceName
            };
            cb.Checked += MonitorSelectionChanged;
            cb.Unchecked += MonitorSelectionChanged;
            MonitorPanel.Children.Add(cb);
        }
    }

    private void MonitorSelectionChanged(object? sender, RoutedEventArgs e)
    {
        var selected = MonitorPanel.Children
            .OfType<WpfCheckBox>()
            .Where(c => c.IsChecked == true)
            .Select(c => c.Tag as string)
            .Where(s => !string.IsNullOrEmpty(s))
            .Cast<string>()
            .ToArray();
        _main.SetAllowedMonitors(selected);
        if (ChkRemember.IsChecked == true) SaveSettingsPreservingUnknown();
    }

    private void LoadAndApplySavedMonitors()
    {
        try
        {
            var path = SettingsFilePath;
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            var node = JsonNode.Parse(json) as JsonObject;
            if (node == null) return;
            string[] devices = node["AllowedMonitors"] is JsonArray arr
                ? arr.Select(x => x?.GetValue<string>() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToArray()
                : Array.Empty<string>();
            var checkboxes = MonitorPanel.Children.OfType<WpfCheckBox>().ToList();
            foreach (var cb in checkboxes)
            {
                string dev = cb.Tag as string ?? string.Empty;
                cb.IsChecked = devices.Contains(dev);
            }
            _main.SetAllowedMonitors(devices);
        }
        catch { }
    }

    private void SaveSettingsPreservingUnknown()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            JsonObject root;
            if (File.Exists(SettingsFilePath))
            {
                try { root = (JsonNode.Parse(File.ReadAllText(SettingsFilePath)) as JsonObject) ?? new JsonObject(); }
                catch { root = new JsonObject(); }
            }
            else root = new JsonObject();

            if (!string.IsNullOrEmpty(_idle)) root["Idle"] = _idle;
            if (!string.IsNullOrEmpty(_move)) root["Move"] = _move;
            if (!string.IsNullOrEmpty(_sleep)) root["Sleep"] = _sleep;

            var selected = MonitorPanel.Children
                .OfType<WpfCheckBox>()
                .Where(c => c.IsChecked == true)
                .Select(c => c.Tag as string)
                .Where(s => !string.IsNullOrEmpty(s))
                .Cast<string>()
                .ToArray();
            root["AllowedMonitors"] = new JsonArray(selected.Select(s => JsonValue.Create(s)!).ToArray());

            File.WriteAllText(SettingsFilePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}


