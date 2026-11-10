using System.Text.Json;
using FT232H.ControlStudio.UI;
using FT232H.Core.Diagnostics;
using FT232H.Core.Devices;
using FT232H.Core.Hardware;
using FT232H.Core.Spi;
using Microsoft.Extensions.Logging;

namespace FT232H.ControlStudio;

/// <summary>
/// Main engineering shell: device toolbar, connection status, tabbed playgrounds
/// (SPI / GPIO / Monitor / Automation / Simulation), live log viewer and
/// configuration save/load. All hardware I/O is async; the UI never blocks.
/// </summary>
public sealed class MainForm : Form
{
    private readonly StudioContext _context;
    private readonly ILogger<MainForm> _logger;

    private readonly ToolStrip _toolbar = new() { GripStyle = ToolStripGripStyle.Hidden };
    private readonly ToolStripComboBox _backendCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly ToolStripComboBox _deviceCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    private readonly ToolStripButton _connectButton = new("Connect");
    private readonly ToolStripButton _disconnectButton = new("Disconnect");

    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _deviceStatus = new("Disconnected") { ForeColor = StudioTheme.Error };
    private readonly ToolStripStatusLabel _spiSummary = new("SPI: -");
    private readonly ToolStripStatusLabel _countersLabel = new("TX: 0 B  RX: 0 B  Ops: 0  Err: 0");
    private readonly ToolStripStatusLabel _clockLabel = new("Clock: -");

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TextBox _logView = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        Font = new Font(FontFamily.GenericMonospace, 9f),
    };

    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 250 };
    private readonly SplitContainer _split = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Horizontal,
        FixedPanel = FixedPanel.Panel2,
    };

    private SpiPlaygroundControl _spiTab = null!;
    private MonitorControl _monitorTab = null!;

    public MainForm(StudioContext context, ILogger<MainForm> logger)
    {
        _context = context;
        _logger = logger;

        Text = "FT232H Control Studio";
        MinimumSize = new Size(1100, 720);
        Size = new Size(1400, 900);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        StudioTheme.ApplyDark(this);

        _uiTimer.Tick += (_, _) => RefreshStatus();
        _uiTimer.Start();

        _context.ConnectionChanged += (_, _) => OnConnectionChanged();
        _context.Logger.DiagnosticLogged += (_, evt) =>
            StudioLogSink.Instance.Append(evt.Severity, evt.Source, evt.Message);

        _ = LoadDevicesAsync();
    }

    private void BuildLayout()
    {
        _backendCombo.Items.AddRange(new object[] { "Simulation", "Hardware (D2XX)" });
        _backendCombo.SelectedIndex = 0;
        _backendCombo.SelectedIndexChanged += (_, _) => OnBackendChanged();
        _backendCombo.ToolTipText = "Select the device backend. Simulation runs without hardware.";

        var refreshButton = new ToolStripButton("Refresh");
        var saveConfigButton = new ToolStripButton("Save config");
        var loadConfigButton = new ToolStripButton("Load config");
        refreshButton.Click += async (_, _) => await LoadDevicesAsync();
        _connectButton.Click += async (_, _) => await ConnectAsync();
        _disconnectButton.Click += async (_, _) => await DisconnectAsync();
        saveConfigButton.Click += (_, _) => SaveConfiguration();
        loadConfigButton.Click += (_, _) => LoadConfiguration();

        _toolbar.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripLabel("Backend:"), _backendCombo,
            new ToolStripSeparator(),
            new ToolStripLabel("Device:"), _deviceCombo,
            refreshButton, _connectButton, _disconnectButton,
            new ToolStripSeparator(),
            saveConfigButton, loadConfigButton,
        });

        _spiTab = new SpiPlaygroundControl(_context);
        var gpioTab = new GpioPlaygroundControl(_context);
        _monitorTab = new MonitorControl(_context);
        var automationTab = new AutomationControl(_context);
        var simulationTab = new SimulationControl(_context, this);
        _tabs.TabPages.Add(CreateTab("SPI Playground", _spiTab));
        _tabs.TabPages.Add(CreateTab("GPIO Playground", gpioTab));
        _tabs.TabPages.Add(CreateTab("Signal Monitor", _monitorTab));
        _tabs.TabPages.Add(CreateTab("Automation", automationTab));
        _tabs.TabPages.Add(CreateTab("Simulation", simulationTab));

        _split.Panel1.Controls.Add(_tabs);
        _split.Panel2.Controls.Add(_logView);

        _statusStrip.Items.AddRange(new ToolStripItem[]
        {
            _deviceStatus, new ToolStripSeparator(),
            _spiSummary, new ToolStripSeparator(),
            _clockLabel, new ToolStripSeparator(),
            _countersLabel,
        });

        Controls.Add(_split);
        Controls.Add(_toolbar);
        Controls.Add(_statusStrip);
    }

    private static TabPage CreateTab(string title, Control content)
    {
        content.Dock = DockStyle.Fill;
        var page = new TabPage(title);
        page.Controls.Add(content);
        return page;
    }

    private void OnBackendChanged()
    {
        _context.Backend = _backendCombo.SelectedIndex == 0
            ? new FT232H.Simulation.SimulatedFtdiDeviceManager()
            : new FT232H.Hardware.FtdiDeviceManager();
        _logger.LogInformation("Backend switched to {Backend}", _backendCombo.SelectedItem);
        _ = LoadDevicesAsync();
    }

    internal async Task LoadDevicesAsync()
    {
        try
        {
            var devices = await _context.Backend.EnumerateAsync();
            _deviceCombo.Items.Clear();
            foreach (var d in devices)
                _deviceCombo.Items.Add(d);
            if (_deviceCombo.Items.Count > 0)
                _deviceCombo.SelectedIndex = 0;

            StudioLogSink.Instance.Append(DiagnosticSeverity.Info, "Device",
                $"Found {devices.Count} device(s).");
        }
        catch (FtdiException ex)
        {
            StudioLogSink.Instance.Append(DiagnosticSeverity.Error, "Device", ex.Message);
        }
    }

    private async Task ConnectAsync()
    {
        if (_deviceCombo.SelectedItem is not FtdiDeviceInfo info)
        {
            MessageBox.Show(this, "Select a device first.", "Connect",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            // Duplicate serial numbers are disambiguated by USB location.
            var selector = info.HasDuplicateSerial
                ? DeviceSelector.BySerial(info.SerialNumber, info.LocationId)
                : DeviceSelector.ByIndex(info.Index);
            await _context.ConnectAsync(selector);
        }
        catch (FtdiException ex)
        {
            StudioLogSink.Instance.Append(DiagnosticSeverity.Error, "Device", ex.Message);
            MessageBox.Show(this, ex.Message, "Connection failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task DisconnectAsync()
    {
        await _context.DisconnectAsync();
    }

    private void OnConnectionChanged()
    {
        if (InvokeRequired)
        {
            BeginInvoke(OnConnectionChanged);
            return;
        }

        bool connected = _context.IsConnected;
        _deviceStatus.Text = connected
            ? $"Connected: {_context.Device?.Info.Description} ({_context.Device?.Info.SerialNumber})"
            : "Disconnected";
        _deviceStatus.ForeColor = connected ? StudioTheme.Ok : StudioTheme.Error;
        _connectButton.Enabled = !connected;
        _disconnectButton.Enabled = connected;
        _monitorTab.OnConnectionChanged(connected);
    }

    private void RefreshStatus()
    {
        var drained = StudioLogSink.Instance.Drain();
        if (drained.Count > 0)
        {
            foreach (var evt in drained)
            {
                string line = $"{evt.TimestampUtc:HH:mm:ss.fff} [{evt.Severity}] {evt.Source}: {evt.Message}";
                _logView.AppendText(line + Environment.NewLine);
            }
            // Keep the view bounded.
            if (_logView.TextLength > 120_000)
                _logView.Text = _logView.Text[^60_000..];
        }

        var counters = _context.Counters;
        _countersLabel.Text =
            $"TX: {counters.BytesWritten:N0} B  RX: {counters.BytesRead:N0} B  " +
            $"Ops: {counters.Transactions:N0}  Err: {counters.Errors:N0}";

        var device = _context.Device;
        if (device is not null)
        {
            var cfg = device.Spi.Configuration;
            _spiSummary.Text = $"SPI: {cfg.Mode}, {cfg.BitOrder}, CS {cfg.ChipSelectPin} ({cfg.ChipSelectMode})";
            _clockLabel.Text = $"Clock: {device.Spi.ActualClockHz / 1_000_000.0:0.###} MHz actual";
        }
    }

    private void SaveConfiguration()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Studio configuration (*.json)|*.json",
            FileName = "ft232h-studio.json",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var config = new StudioConfiguration
            {
                Backend = _backendCombo.SelectedIndex == 0 ? "Simulation" : "Hardware",
                Spi = _spiTab.CurrentConfiguration,
            };
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }));
            StudioLogSink.Instance.Append(DiagnosticSeverity.Info, "Config", $"Saved to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadConfiguration()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Studio configuration (*.json)|*.json",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var config = JsonSerializer.Deserialize<StudioConfiguration>(File.ReadAllText(dialog.FileName),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            if (config is null) return;

            _backendCombo.SelectedIndex = config.Backend == "Simulation" ? 0 : 1;
            if (config.Spi is not null)
                _spiTab.ApplyConfiguration(config.Spi);
            StudioLogSink.Instance.Append(DiagnosticSeverity.Info, "Config", $"Loaded from {dialog.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Load failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _uiTimer.Stop();
        _context.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        base.OnFormClosing(e);
    }
}

/// <summary>Serializable studio configuration (backend + SPI settings).</summary>
public sealed class StudioConfiguration
{
    public string Backend { get; set; } = "Simulation";
    public SpiConfiguration? Spi { get; set; }
}