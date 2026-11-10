using FT232H.ControlStudio.UI;

namespace FT232H.ControlStudio;

/// <summary>
/// Simulation backend controls: virtual device count, duplicate-serial scenario,
/// simulated latency, SPI loopback and backend diagnostics display.
/// </summary>
public sealed class SimulationControl : UserControl
{
    private readonly StudioContext _context;
    private readonly MainForm _mainForm;

    private readonly NumericUpDown _deviceCount = new() { Minimum = 1, Maximum = 8, Value = 2, Width = 70 };
    private readonly CheckBox _duplicateSerials = new() { Text = "Include duplicate serial numbers", AutoSize = true, Checked = true };
    private readonly NumericUpDown _latency = new() { Minimum = 0, Maximum = 1000, Value = 5, Width = 70 };
    private readonly CheckBox _loopback = new() { Text = "SPI loopback (echo TX on RX)", AutoSize = true, Checked = true };
    private readonly Button _applyButton = new() { Text = "Apply to simulation", Width = 150 };
    private readonly Button _refreshDevicesButton = new() { Text = "Refresh device list", Width = 140 };
    private readonly DataGridView _deviceGrid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
    };
    private readonly TextBox _diagnostics = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Bottom,
        Height = 130,
        Font = new Font(FontFamily.GenericMonospace, 9f),
    };

    public SimulationControl(StudioContext context, MainForm mainForm)
    {
        _context = context;
        _mainForm = mainForm;

        _deviceGrid.ColumnCount = 5;
        _deviceGrid.Columns[0].Name = "Index";
        _deviceGrid.Columns[1].Name = "Serial";
        _deviceGrid.Columns[2].Name = "Description";
        _deviceGrid.Columns[3].Name = "Location";
        _deviceGrid.Columns[4].Name = "Duplicate SN";

        _applyButton.Click += (_, _) => ApplyOptions();
        _refreshDevicesButton.Click += async (_, _) =>
        {
            await _mainForm.LoadDevicesAsync();
            await RefreshDiagnosticsAsync();
        };

        var optionsPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        optionsPanel.Controls.AddRange(new Control[]
        {
            new Label { Text = "Virtual devices:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) },
            _deviceCount,
            _duplicateSerials,
            new Label { Text = "Latency (ms):", AutoSize = true, Padding = new Padding(8, 6, 0, 0) },
            _latency,
            _loopback,
            _applyButton,
            _refreshDevicesButton,
        });

        Controls.Add(_deviceGrid);
        Controls.Add(_diagnostics);
        Controls.Add(optionsPanel);

        _ = RefreshDiagnosticsAsync();
    }

    private void ApplyOptions()
    {
        if (_context.Backend is FT232H.Simulation.SimulatedFtdiDeviceManager sim)
        {
            sim.Options.VirtualDeviceCount = (int)_deviceCount.Value;
            sim.Options.IncludeDuplicateSerials = _duplicateSerials.Checked;
            sim.Options.LatencyMilliseconds = (int)_latency.Value;
            sim.Options.SpiLoopback = _loopback.Checked;
            StudioLogSink.Instance.Append(Core.Diagnostics.DiagnosticSeverity.Info, "Sim",
                "Simulation options updated.");
            _ = RefreshDiagnosticsAsync();
        }
        else
        {
            MessageBox.Show(this, "Switch the backend to Simulation first.", "Simulation",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async Task RefreshDiagnosticsAsync()
    {
        try
        {
            var lines = await _context.Backend.GetDiagnosticsAsync();
            _diagnostics.Clear();
            foreach (var line in lines)
                _diagnostics.AppendText(
                    $"{(line.IsOk ? "[ OK ]" : "[FAIL]")} {line.Name}: {line.Value}" +
                    $"{(line.Detail is null ? string.Empty : " — " + line.Detail)}{Environment.NewLine}");

            var devices = await _context.Backend.EnumerateAsync();
            _deviceGrid.Rows.Clear();
            foreach (var d in devices)
                _deviceGrid.Rows.Add(d.Index, d.SerialNumber, d.Description, d.LocationId,
                    d.HasDuplicateSerial ? "YES" : "no");
        }
        catch (Core.Hardware.FtdiException ex)
        {
            _diagnostics.AppendText($"Diagnostics failed: {ex.Message}{Environment.NewLine}");
        }
    }
}