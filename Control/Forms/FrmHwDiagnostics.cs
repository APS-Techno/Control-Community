using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using APS.Controls.Theme;
using APS.SystemControl.Core.Diagnostics;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.Forms;

/// <summary>
/// On-demand hardware diagnostics window. Opening pings once; the "Ping Hardware"
/// button re-pings. Probes each PLC and each serial/TCP device with its own link.
/// PLC-hosted controls (PlcValve, PLCMFC, etc.) are excluded — no own link.
/// Never reads hardware on the UI thread outside of btnPing.Click.
/// </summary>
public partial class FrmHwDiagnostics : Form
{
    private readonly Core.Core _core;

    public FrmHwDiagnostics(Core.Core core)
    {
        ArgumentNullException.ThrowIfNull(core);
        _core = core;

        InitializeComponent();
        DarkModeHelper.FixControls(this);

        btnPing.Click += async (_, _) => await PingAsync();
        // Probe AFTER the window is visible (Shown, not Load) and OFF the UI thread,
        // so the window opens instantly instead of blocking on live device I/O.
        Shown += async (_, _) => await PingAsync();
    }

    private bool _pinging;

    private async Task PingAsync()
    {
        if (_pinging) return;                 // ignore re-entry while a probe is running
        _pinging = true;
        btnPing.Enabled = false;
        lblBanner.Text = "Checking hardware...";
        lblBanner.Visible = true;

        try
        {
            // RunHardwareSelfTest does live, blocking device I/O (PLC connect, serial
            // reads with timeouts). Run it on a background thread so the window stays
            // responsive; await resumes on the UI thread to populate the grid.
            var report = await Task.Run(() => _core.RunHardwareSelfTest());
            if (IsDisposed || Disposing) return;   // window closed mid-probe

            if (!report.Ran)
            {
                lblBanner.Visible = false;
                ThemedMessageBox.Show(this, report.BlockedReason ?? "Blocked.",
                    "Hardware Diagnostics", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            grid.Rows.Clear();
            // Only show devices that were actually probed; hide monitor-only devices
            // (no active liveness probe — they'd just be noise in a diagnostics list).
            foreach (var r in report.Results
                         .Where(x => x.Result.Level != HealthLevel.MonitoredOnly)
                         .OrderBy(x => x.DeviceType).ThenBy(x => x.DeviceId))
            {
                int i = grid.Rows.Add(
                    r.DeviceId,
                    r.DeviceType,
                    r.Result.Level.ToString(),
                    r.Result.LatencyMs > 0 ? r.Result.LatencyMs.ToString("0") : "",
                    r.Result.Identity,
                    r.Result.Error ?? r.Result.Detail);
                grid.Rows[i].Cells["Result"].Style.ForeColor = ColorFor(r.Result.Level);
            }
            lblBanner.Visible = false;
        }
        finally
        {
            if (!IsDisposed && !Disposing) btnPing.Enabled = true;
            _pinging = false;
        }
    }

    private static System.Drawing.Color ColorFor(HealthLevel level) => level switch
    {
        HealthLevel.Alive         => System.Drawing.Color.LightGreen,
        HealthLevel.WrongProtocol => System.Drawing.Color.Orange,
        HealthLevel.Unresponsive  => System.Drawing.Color.IndianRed,
        _                         => System.Drawing.Color.Gray,
    };
}
