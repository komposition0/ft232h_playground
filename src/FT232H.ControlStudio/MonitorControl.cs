using FT232H.Core.Buffers;
using ScottPlot;

namespace FT232H.ControlStudio;

/// <summary>
/// Signal monitor: ScottPlot time series of host-observed SPI transaction
/// durations, throughput and error counts fed from bounded ring buffers.
/// All values are host-observed samples, NOT hardware signal timing.
/// </summary>
public sealed class MonitorControl : UserControl
{
    private readonly StudioContext _context;
    private readonly ScottPlot.WinForms.FormsPlot _plot = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Label _statsLabel = new()
    {
        Dock = DockStyle.Bottom,
        AutoSize = false,
        Height = 24,
        Text = "Samples: 0",
    };

    private readonly RingBuffer<double> _durations = new(500);
    private readonly RingBuffer<double> _throughput = new(500);
    private readonly RingBuffer<double> _errors = new(500);
    private long _sampleIndex;
    private double _lastBytesTotal;
    private ScottPlot.Plottables.Scatter? _durationPlot;
    private ScottPlot.Plottables.Scatter? _throughputPlot;
    private ScottPlot.Plottables.Scatter? _errorPlot;

    public MonitorControl(StudioContext context)
    {
        _context = context;

        Controls.Add(_plot);
        Controls.Add(_statsLabel);

        var plt = _plot.Plot;
        plt.Title("Communication Monitor (host-observed)");
        plt.XLabel("Sample");
        plt.ShowLegend(Edge.Right);

        _durationPlot = plt.Add.Scatter(new double[] { }, new double[] { });
        _durationPlot.LegendText = "SPI duration (ms)";
        _durationPlot.Color = Colors.Cyan;

        _throughputPlot = plt.Add.Scatter(new double[] { }, new double[] { });
        _throughputPlot.LegendText = "Throughput (KB/s)";
        _throughputPlot.Color = Colors.Orange;

        _errorPlot = plt.Add.Scatter(new double[] { }, new double[] { });
        _errorPlot.LegendText = "Errors (cumulative)";
        _errorPlot.Color = Colors.Red;

        _plot.Refresh();

        var sampleTimer = new System.Windows.Forms.Timer { Interval = 500 };
        sampleTimer.Tick += (_, _) => SampleOnce();
        sampleTimer.Start();
    }

    public void OnConnectionChanged(bool connected)
    {
        // Sampling continues regardless; counters read zero when disconnected.
    }

    private void SampleOnce()
    {
        var counters = _context.Counters;
        double bytesNow = counters.BytesWritten + counters.BytesRead;
        double kbps = (bytesNow - _lastBytesTotal) / 1024.0 / 0.5; // per 500 ms tick
        _lastBytesTotal = bytesNow;

        var lastTx = _context.Logger.Snapshot().LastOrDefault(t => t.Kind == Core.Diagnostics.TransactionKind.Spi);
        double durationMs = lastTx?.HostObservedDuration.TotalMilliseconds ?? 0;

        _durations.Add(durationMs);
        _throughput.Add(kbps);
        _errors.Add(counters.Errors);
        _sampleIndex++;

        RedrawPlot();
        _statsLabel.Text =
            $"Samples: {_sampleIndex}  |  Last SPI duration: {durationMs:0.000} ms  |  " +
            $"Throughput: {kbps:0.0} KB/s  |  Total errors: {counters.Errors}";
    }

    private void RedrawPlot()
    {
        var plt = _plot.Plot;
        plt.Clear();

        var xs = Enumerable.Range(0, _durations.Count).Select(i => (double)i).ToArray();

        _durationPlot = plt.Add.Scatter(xs, _durations.ToArray());
        _durationPlot.LegendText = "SPI duration (ms)";
        _durationPlot.Color = Colors.Cyan;

        _throughputPlot = plt.Add.Scatter(xs, _throughput.ToArray());
        _throughputPlot.LegendText = "Throughput (KB/s)";
        _throughputPlot.Color = Colors.Orange;

        _errorPlot = plt.Add.Scatter(xs, _errors.ToArray());
        _errorPlot.LegendText = "Errors (cumulative)";
        _errorPlot.Color = Colors.Red;

        _plot.Refresh();
    }
}