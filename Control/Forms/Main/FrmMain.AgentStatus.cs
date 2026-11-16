using System.Diagnostics;
using APS.Controls.Theme;
using APS.SystemControl.Core.Remote.Agent;

namespace APS.SystemControl;

/// <summary>
/// Status-strip MCP indicator: a coloured dot plus a short label showing what authority
/// a connected agent currently holds over the machine. Gray means nothing can be touched
/// (feature off, or listening with nobody connected); green/orange/red escalate through
/// the Read / Recipe / Manual tiers. The mapping itself lives in <see cref="AgentStatusView"/>.
/// </summary>
public partial class FrmMain
{
    private ToolStripStatusLabel? _mcpDot;
    private ToolStripStatusLabel? _mcpText;

    /// <summary>Same family as <c>StateColor</c>'s chamber-tile palette so the strip reads as
    /// one system. VacuumTheme.GlowGreen (0,150,80) is too dark against the strip, so the
    /// green is the 400-weight sibling of the amber and red used by the tiles.</summary>
    private static Color AgentLevelColor(AgentStatusLevel level) => level switch
    {
        AgentStatusLevel.Read   => Color.FromArgb(102, 187, 106),
        AgentStatusLevel.Recipe => Color.FromArgb(255, 183, 77),
        AgentStatusLevel.Manual => Color.FromArgb(239, 83, 80),
        _                       => VacuumTheme.TextDim,
    };

    /// <summary>Builds the indicator and hooks the server. Must be called from FrmMain_Load
    /// AFTER the right-hand spring is in place (so it heads the right-hand group, left of the
    /// quota/user/version block) and AFTER StartAgentServer, so <c>_agent</c> is available to
    /// subscribe to.</summary>
    private void InitAgentStatusIndicator()
    {
        // StatusStrip defaults ShowItemToolTips to false (unlike ToolStrip), so the
        // tooltip carrying the endpoint and connected keys would never appear.
        statusStrip1.ShowItemToolTips = true;

        _mcpDot = new ToolStripStatusLabel("●")
        {
            Font = new Font("Segoe UI", 10F),
            ForeColor = VacuumTheme.TextDim,
            Margin = new Padding(14, 0, 0, 0),
            IsLink = false,
        };
        _mcpText = new ToolStripStatusLabel
        {
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(200, 214, 229),
            Margin = new Padding(3, 0, 6, 0),
        };

        _mcpDot.Click += OnAgentStatusClicked;
        _mcpText.Click += OnAgentStatusClicked;

        statusStrip1.Items.Add(_mcpDot);
        statusStrip1.Items.Add(_mcpText);

        if (_agent is not null) _agent.SessionsChanged += OnAgentSessionsChanged;
        RefreshAgentStatus();
    }

    /// <summary>Fires on an AgentServer client thread — marshal before touching the strip.</summary>
    private void OnAgentSessionsChanged()
    {
        if (IsDisposed || Disposing) return;
        try
        {
            if (InvokeRequired) BeginInvoke(RefreshAgentStatus);
            else RefreshAgentStatus();
        }
        catch (ObjectDisposedException) { }     // form torn down between the check and the post
        catch (InvalidOperationException) { }   // handle not created (or already destroyed)
    }

    /// <summary>Repaints the indicator from current server state. Safe to call before the
    /// labels exist (StartAgentServer runs first) — it simply does nothing then.</summary>
    private void RefreshAgentStatus()
    {
        if (_mcpDot is null || _mcpText is null) return;

        IReadOnlyList<AgentSession> sessions = _agent?.Sessions ?? Array.Empty<AgentSession>();
        var view = AgentStatusView.Describe(
            listening: _agent?.IsListening == true,
            offReason: _agentOffReason,
            endpoint: _agentEndpoint,
            sessions: sessions);

        _mcpDot.ForeColor = AgentLevelColor(view.Level);
        _mcpText.Text = view.Text;
        _mcpDot.ToolTipText = view.Tooltip;
        _mcpText.ToolTipText = view.Tooltip;
    }

    private void OnAgentStatusClicked(object? sender, EventArgs e)
    {
        string uiExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "APS.Agent.Ui.exe");
        if (!File.Exists(uiExe))
        {
            PublishEvent("Agent", $"APS.Agent.Ui.exe was not found in {AppDomain.CurrentDomain.BaseDirectory}.", APS.SystemControl.Core.Logging.LogEventLevel.Operator);
            return;
        }
        try
        {
            using var proc = Process.Start(new ProcessStartInfo(uiExe)
            {
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            PublishEvent("Agent", $"Failed to launch APS.Agent.Ui.exe: {ex.Message}", APS.SystemControl.Core.Logging.LogEventLevel.Operator);
        }
    }
}
