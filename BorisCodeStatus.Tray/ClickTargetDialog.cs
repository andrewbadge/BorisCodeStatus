namespace BorisCodeStatus.Tray;

/// <summary>
/// Picks what a click on the waiting card does: just dismiss it (the default), or also bring an
/// app to the front. A dialog rather than a submenu because the list of open apps can be long,
/// and a list box scrolls and can be searched by typing where a menu cannot.
/// </summary>
internal sealed class ClickTargetDialog : Form
{
    private const string DismissOnly = "Just dismiss the notification";

    private readonly ListBox _list = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
    };

    private readonly List<string?> _processNames = [];

    /// <summary>The chosen process name, or null for "just dismiss". Valid after OK.</summary>
    public string? ProcessName => _list.SelectedIndex >= 0 ? _processNames[_list.SelectedIndex] : null;

    public ClickTargetDialog(string? current)
    {
        Text = "When notification is clicked";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(360, 380);
        Padding = new Padding(12);

        var prompt = new Label
        {
            Text = "Clicking the waiting notification closes it. It can also bring one of these apps to the front:",
            Dock = DockStyle.Top,
            Height = 40,
        };

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 40,
            Padding = new Padding(0, 8, 0, 0),
        };
        buttons.Controls.AddRange([cancel, ok]);

        Controls.Add(_list);
        Controls.Add(buttons);
        Controls.Add(prompt);
        AcceptButton = ok;
        CancelButton = cancel;

        _list.DoubleClick += (_, _) =>
        {
            if (_list.SelectedIndex >= 0)
            {
                DialogResult = DialogResult.OK;
            }
        };

        Fill(current);
    }

    /// <summary>
    /// "Just dismiss", then every app with a window open now. A saved choice that is not running
    /// is still listed, selected, so the dialog never hides what is set.
    /// </summary>
    private void Fill(string? current)
    {
        Add(null, DismissOnly);

        var apps = WindowActivator.RunningApps();
        if (current is not null && !apps.Any(a => a.ProcessName.Equals(current, StringComparison.OrdinalIgnoreCase)))
        {
            Add(current, $"{current} (not running)");
        }

        foreach (var (processName, label) in apps)
        {
            Add(processName, label);
        }

        _list.SelectedIndex = Math.Max(0, _processNames.FindIndex(
            name => string.Equals(name, current, StringComparison.OrdinalIgnoreCase)));
    }

    private void Add(string? processName, string label)
    {
        _processNames.Add(processName);
        _list.Items.Add(label);
    }
}
