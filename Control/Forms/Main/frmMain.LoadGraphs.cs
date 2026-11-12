using APS.Controls.Theme;
using APS.SystemControl.XML;
using ScottPlot.WinForms;
using ScottPlot.Plottables;
using ScottPlot.Panels;
using ScottPlot;

namespace APS.SystemControl;

public class SensorGraph
{
    public FormsPlot? FormsPlot { get; private set; }
    private readonly DataStreamer? streamer;

    public SensorGraph(XML_Graph config, int window)
    {
        string axis = $"{config.YAxis ?? string.Empty} ({config.Units ?? string.Empty})";
        string title = config.Name ?? string.Empty;

        FormsPlot = new FormsPlot() { Dock = DockStyle.Fill };
        streamer = FormsPlot.Plot.Add.DataStreamer(window);
        FormsPlot.Plot.YLabel(axis);
        FormsPlot.Plot.Title(title.Trim());
        FormsPlot.Plot.Axes.ContinuouslyAutoscale = true;
        FormsPlot.Plot.Axes.SetLimitsY(config.Min, config.Max);
        streamer.Color = ScottPlot.Color.FromHex(config.Color);
        streamer.LineWidth = 2;
        SetTicks();
        ApplyDarkTheme();
        FormsPlot?.Refresh();
    }

    // Set when new data has been streamed in but not yet rendered. A FormsPlot
    // re-render is a full bitmap redraw, so Add only appends the data point and marks
    // dirty; a single UI render timer calls RenderIfDirty, decoupling the on-screen
    // refresh rate from the data rate.
    private bool _dirty;

    public void Add(float value)
    {
        if (streamer != null)
        {
            streamer.Add(float.IsFinite(value) ? value : double.NaN);
            streamer.ViewScrollLeft();
            _dirty = true;
        }
    }

    /// <summary>Renders the plot if new data arrived since the last render.
    /// Must be called on the UI thread (the render timer's Tick).</summary>
    public void RenderIfDirty()
    {
        if (!_dirty) return;
        _dirty = false;
        FormsPlot?.Refresh();
    }

    private void SetTicks()
    {
        ScottPlot.TickGenerators.LogMinorTickGenerator minorTickGen = new();

        ScottPlot.TickGenerators.NumericAutomatic tickGen = new();
        tickGen.MinorTickGenerator = minorTickGen;

        static string LogTickLabelFormatter(double y) => $"{y:0.00e0}";

        tickGen.IntegerTicksOnly = false;

        tickGen.LabelFormatter = LogTickLabelFormatter;

        FormsPlot!.Plot.Axes.Left.TickGenerator = tickGen;

        // Reserve room for the rotated Y-axis title + tick labels: ScottPlot's default
        // SizeWhenNoData (15px) is too narrow for the title on an empty plot, and
        // MinimumSize avoids a layout jump on first data. SizeWhenNoData lives on the
        // concrete AxisBase, not the IYAxis interface.
        if (FormsPlot.Plot.Axes.Left is ScottPlot.AxisPanels.AxisBase leftAxis)
        {
            leftAxis.SizeWhenNoData = 55;
            leftAxis.MinimumSize = 55;
        }
    }

    private void ApplyDarkTheme()
    {
        if (!VacuumTheme.IsDark || FormsPlot == null) return;

        var plot = FormsPlot.Plot;

        // Match the DarkModeHelper industrial dark blue palette.
        plot.FigureBackground.Color = ScottPlot.Color.FromHex("#0D1B2A");
        plot.DataBackground.Color = ScottPlot.Color.FromHex("#162032");

        plot.Axes.Color(ScottPlot.Color.FromHex("#C8D6E5"));

        plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#283C5A");
        plot.Grid.MinorLineColor = ScottPlot.Color.FromHex("#1A2840");
    }
}

public partial class FrmMain
{
    private readonly Dictionary<string, SensorGraph> graphs = new();
    private System.Windows.Forms.Timer? _graphRenderTimer;

    private void LoadGraphs()
    {
        var xmlGraphs = Serializer<XML_Graphs>.Load(config.GraphFile);

        if (xmlGraphs?.Graphs == null) return;

        TableLayoutPanel parent = this.graphLayout;
        if (parent == null) return;

        // The designer hardcodes RowStyles for a 3-row panel (3×33.33% + a 20px spacer);
        // setting only RowCount squashes any 4th+ graph into the 20px row, so clear and
        // redistribute for any number of graphs.
        parent.RowCount = xmlGraphs.Graphs.Count;
        parent.RowStyles.Clear();
        for (int i = 0; i < xmlGraphs.Graphs.Count; i++)
            parent.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / xmlGraphs.Graphs.Count));

        for (int column = 0; column < xmlGraphs.Graphs.Count; column++)
        {
            XML_Graph xml_graph = xmlGraphs.Graphs[column];
            SensorGraph graph = new SensorGraph(xml_graph, 50);
            graphs.Add(xml_graph.Sensor, graph);
            parent.Controls.Add(graph.FormsPlot, 0, column);
            parent.Controls.SetChildIndex(graph.FormsPlot, 0);
        }

        StartGraphRenderTimer();
    }

    /// <summary>
    /// Drives the on-screen graph refresh at ~10 fps, independent of the 200 ms
    /// data poll. SensorGraph.Add only appends data + marks dirty; this single
    /// UI-thread tick renders any graph that received new samples, coalescing a
    /// burst of per-sensor updates into one redraw per graph per tick.
    /// </summary>
    private void StartGraphRenderTimer()
    {
        if (_graphRenderTimer != null || graphs.Count == 0) return;

        _graphRenderTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _graphRenderTimer.Tick += (_, _) =>
        {
            foreach (var graph in graphs.Values)
                graph.RenderIfDirty();
        };
        _graphRenderTimer.Start();

        this.FormClosing += (_, e) =>
        {
            // A prior handler may have cancelled the exit (operator answered No to
            // the confirmation). WinForms still runs the remaining handlers, so
            // tearing the timer down here would permanently freeze graph rendering
            // for the rest of the session — there is no restart path.
            if (e.Cancel) return;
            _graphRenderTimer?.Stop();
            _graphRenderTimer?.Dispose();
            _graphRenderTimer = null;
        };
    }
}
