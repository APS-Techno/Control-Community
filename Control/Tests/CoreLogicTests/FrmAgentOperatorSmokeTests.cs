using APS.Agent.Core.Guards;
using APS.Agent.Core.Runtime;
using APS.Agent.Ui;
using APS.Controls.Theme;

namespace CoreLogicTests;

public sealed class FrmAgentOperatorSmokeTests
{
    [Fact]
    public void FrmAgentOperator_initializes_dark_theme_toggles_modes_and_updates_watch_and_indicators()
    {
        bool? prevForceDark = VacuumTheme.ForceDark;
        VacuumTheme.ForceDark = true;
        try
        {
            using var form = new FrmAgentOperator();

            Assert.Equal(AgentMode.Copilot, form.Controller.Mode);
            form.SetMode(AgentMode.Autonomous);
            Assert.Equal(AgentMode.Autonomous, form.Controller.Mode);
            form.SetMode(AgentMode.Copilot);
            Assert.Equal(AgentMode.Copilot, form.Controller.Mode);

            var badge = MachineIdentityBadge.FromDescribeMachineJson("""
                {
                  "profile": "ADS",
                  "appVersion": "1.7.2",
                  "isEmulator": true,
                  "executionTarget": "emulator",
                  "sessionTier": "Recipe"
                }
                """);
            var indicators = new ConnectionIndicatorsSnapshot(
                new IndicatorStatus(IndicatorState.Ready, "STRATA LOADED", "qwen3.8"),
                new IndicatorStatus(IndicatorState.Off, "CLOUD STANDBY", "loopback"),
                new IndicatorStatus(IndicatorState.Ready, "ELN3 MCP", "eln-mcp"));

            form.ApplyIndicators(badge, indicators);
            Assert.Equal("EMULATOR | ADS | TIER: RECIPE", form.MachineBadgeText);
            Assert.Equal("STRATA LOADED", form.StrataIndicatorText);
            Assert.Equal("CLOUD STANDBY", form.CloudIndicatorText);
            Assert.Equal("ELN3 MCP", form.ElnIndicatorText);

            var notice = WatchProgressNotice.Parse(
                10,
                50,
                "Running step=24 iter=5/50 deposits=10 psu=PSU_C:382.5V/50.0mA/19.1W pressure=Main:0.003Torr");
            form.ApplyWatchProgress(notice);

            Assert.Equal("Period 5/50", form.WatchPeriodText);
            Assert.Equal("Deposits: 10", form.WatchDepositsText);
            Assert.Contains("psu=PSU_C:382.5V/50.0mA/19.1W", form.WatchTelemetryText);
        }
        finally
        {
            VacuumTheme.ForceDark = prevForceDark;
        }
    }
}
