using FT232H.ControlStudio.UI;
using FT232H.Core.Gpio;

namespace FT232H.ControlStudio;

/// <summary>
/// GPIO control panel: every usable pin with its assigned function, direction and
/// value control, software polling with configurable interval, pulse generation
/// and event log. SPI-reserved pins are protected from accidental writes.
/// </summary>
public sealed class GpioPlaygroundControl : UserControl
{
    private readonly StudioContext _context;

    private readonly DataGridView _pinGrid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
    };
    private readonly Button _refreshButton = new() { Text = "Read all pins", Width = 120 };
    private readonly Button _setHighButton = new() { Text = "Set HIGH", Width = 90 };
    private readonly Button _setLowButton = new() { Text = "Set LOW", Width = 90 };
    private readonly Button _inputButton = new() { Text = "Set as input", Width = 100 };
    private readonly Button _outputButton = new() { Text = "Set as output", Width = 100 };
    private readonly CheckBox _pollCheck = new() { Text = "Poll inputs", AutoSize = true };
    private readonly NumericUpDown _pollInterval = new() { Minimum = 25, Maximum = 5000, Value = 250, Width = 80 };
    private readonly NumericUpDown _pulseWidth = new() { Minimum = 1, Maximum = 60000, Value = 100, Width = 80 };
    private readonly Button _pulseButton = new() { Text = "Pulse", Width = 80 };
    private readonly TextBox _eventLog = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Bottom,
        Height = 120,
        Font = new Font(FontFamily.GenericMonospace, 9f),
    };

    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 250 };

    public GpioPlaygroundControl(StudioContext context)
    {
        _context = context;

        _pinGrid.ColumnCount = 5;
        _pinGrid.Columns[0].Name = "Pin";
        _pinGrid.Columns[1].Name = "Port";
        _pinGrid.Columns[2].Name = "Function";
        _pinGrid.Columns[3].Name = "Direction";
        _pinGrid.Columns[4].Name = "Value";

        _refreshButton.Click += async (_, _) => await RefreshPinsAsync();
        _setHighButton.Click += async (_, _) => await WriteSelectedAsync(true);
        _setLowButton.Click += async (_, _) => await WriteSelectedAsync(false);
        _inputButton.Click += async (_, _) => await SetDirectionAsync(GpioDirection.Input);
        _outputButton.Click += async (_, _) => await SetDirectionAsync(GpioDirection.Output);
        _pulseButton.Click += async (_, _) => await PulseSelectedAsync();
        _pollCheck.CheckedChanged += (_, _) =>
        {
            _pollTimer.Interval = (int)_pollInterval.Value;
            if (_pollCheck.Checked) _pollTimer.Start();
            else _pollTimer.Stop();
        };
        _pollInterval.ValueChanged += (_, _) => _pollTimer.Interval = (int)_pollInterval.Value;
        _pollTimer.Tick += async (_, _) => await RefreshPinsAsync(quiet: true);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        buttons.Controls.AddRange(new Control[]
        {
            _refreshButton, _setHighButton, _setLowButton,
            _inputButton, _outputButton,
            new Label { Text = "Pulse width (ms):", AutoSize = true, Padding = new Padding(8, 6, 0, 0) },
            _pulseWidth, _pulseButton,
            _pollCheck,
            new Label { Text = "Interval (ms):", AutoSize = true, Padding = new Padding(8, 6, 0, 0) },
            _pollInterval,
        });

        Controls.Add(_pinGrid);
        Controls.Add(buttons);
        Controls.Add(_eventLog);
    }

    private GpioPinId? SelectedPin()
    {
        if (_pinGrid.CurrentRow is null) return null;
        string name = _pinGrid.CurrentRow.Cells[0].Value?.ToString() ?? string.Empty;
        return Enum.TryParse<GpioPinId>(name, out var pin) ? pin : null;
    }

    private Core.Hardware.IFtdiDevice? RequireDevice(bool quiet = false)
    {
        var device = _context.Device;
        if (device is null || !device.IsConnected)
        {
            if (!quiet)
                MessageBox.Show(this, "Connect a device first.", "GPIO",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        return device;
    }

    private async Task RefreshPinsAsync(bool quiet = false)
    {
        var device = RequireDevice(quiet);
        if (device is null) return;

        try
        {
            var states = await device.Gpio.ReadAllAsync();
            _pinGrid.Rows.Clear();
            foreach (var s in states)
                _pinGrid.Rows.Add(s.Pin, s.Pin <= GpioPinId.AdBus7 ? "ADBUS" : "ACBUS",
                    s.Function, s.Direction, s.Value ? "HIGH" : "LOW");
        }
        catch (Core.Hardware.FtdiException ex)
        {
            AppendEvent($"ERROR: {ex.Message}");
            _context.Counters.AddError();
        }
    }

    private async Task WriteSelectedAsync(bool value)
    {
        var device = RequireDevice();
        if (device is null) return;
        var pin = SelectedPin();
        if (pin is null) return;

        var result = await device.Gpio.WritePinAsync(pin.Value, value);
        if (result.Success)
        {
            _context.Counters.AddGpioOperation();
            AppendEvent($"{pin} -> {(value ? "HIGH" : "LOW")}");
        }
        else
        {
            _context.Counters.AddError();
            AppendEvent($"REJECTED {pin}: {result.Error}");
            MessageBox.Show(this, result.Error, "GPIO write rejected",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        await RefreshPinsAsync(quiet: true);
    }

    private async Task SetDirectionAsync(GpioDirection direction)
    {
        var device = RequireDevice();
        if (device is null) return;
        var pin = SelectedPin();
        if (pin is null) return;

        var result = await device.Gpio.SetDirectionAsync(pin.Value, direction);
        if (result.Success)
        {
            _context.Counters.AddGpioOperation();
            AppendEvent($"{pin} direction -> {direction}");
        }
        else
        {
            _context.Counters.AddError();
            AppendEvent($"REJECTED {pin}: {result.Error}");
            MessageBox.Show(this, result.Error, "GPIO direction change rejected",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        await RefreshPinsAsync(quiet: true);
    }

    private async Task PulseSelectedAsync()
    {
        var device = RequireDevice();
        if (device is null) return;
        var pin = SelectedPin();
        if (pin is null) return;

        int widthMs = (int)_pulseWidth.Value;
        AppendEvent($"{pin} pulse {widthMs} ms requested (software-timed, not µs-accurate)");
        var result = await device.Gpio.PulseAsync(pin.Value, TimeSpan.FromMilliseconds(widthMs));
        if (result.Success)
        {
            _context.Counters.AddGpioOperation();
            AppendEvent($"{pin} pulse complete");
        }
        else
        {
            _context.Counters.AddError();
            AppendEvent($"PULSE FAILED {pin}: {result.Error}");
        }
        await RefreshPinsAsync(quiet: true);
    }

    private void AppendEvent(string message)
    {
        _eventLog.AppendText($"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        if (_eventLog.TextLength > 30_000)
            _eventLog.Text = _eventLog.Text[^15_000..];
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pollTimer.Stop();
            _pollTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}