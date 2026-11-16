using System.Text.Json;
using APS.Agent.Cli.Scoring;

namespace APS.Agent.Tests;

public sealed class CapstoneScorersTests
{
    private static JsonElement IdleStatus(params string[] chambers)
    {
        var obj = new
        {
            isRecipeRunning = false,
            activeAlarms = Array.Empty<object>(),
            chambers = chambers.Select(id => new { id, state = "Idle" }).ToArray(),
        };
        return JsonDocument.Parse(JsonSerializer.Serialize(obj)).RootElement;
    }

    [Fact]
    public void ScoreAc1_passes_valid_5_period_RuC_run_and_computes_gamma_adhesion_and_deadtime()
    {
        string rcpx = """
            <Recipe>
              <Params>
                <Param name="P" unit="mTorr">2.1mT</Param>
                <Param name="tRu">31.26</Param>
                <Param name="tC">133.43</Param>
                <Param name="tAdh">418.17</Param>
              </Params>
              <Script>
                set PSU_C Current 50
                set PSU_C Frequency 50
                set PSU_C DutyCycle 60
                set PSU_B Current 300
                set PSU_B Frequency 50
                set PSU_B DutyCycle 60
                pwait $P 300
                deposit Ru $tAdh Home
                loop 5
                  deposit C $tC Home
                  deposit Ru $tRu Home
                endloop
                pwait 0 1
              </Script>
            </Recipe>
            """;
        var logLines = new List<string>
        {
            "10:00:00.000  [Recipe]  Recipe started: AC1_RuC",
            "10:00:01.000  [Execute]    exec: set PSU_C Current 50",
            "10:00:01.100  [Execute]    result: 0",
            "10:00:02.000  [Execute]    exec: set PSU_C Frequency 50",
            "10:00:02.100  [Execute]    result: 0",
            "10:00:03.000  [Execute]    exec: set PSU_C DutyCycle 60",
            "10:00:03.100  [Execute]    result: 0",
            "10:00:04.000  [Execute]    exec: set PSU_B Current 300",
            "10:00:04.100  [Execute]    result: 0",
            "10:00:05.000  [Execute]    exec: set PSU_B Frequency 50",
            "10:00:05.100  [Execute]    result: 0",
            "10:00:06.000  [Execute]    exec: set PSU_B DutyCycle 60",
            "10:00:06.100  [Execute]    result: 0",
            "10:00:07.000  [Execute]    exec: pwait TC1 0.0021000000000000003 300",
            "10:00:08.000  [Execute]    result: 0",
            "10:00:09.000  [Execute]    exec: deposit Ru 418.17 Home",
            "10:07:07.170  [Execute]    result: 0",
        };
        for (int i = 0; i < 5; i++)
        {
            logLines.Add("10:10:00.000  [Execute]    exec: deposit C 133.43 Home");
            logLines.Add("10:12:13.430  [Execute]    result: 0");
            logLines.Add("10:12:14.000  [Execute]    exec: deposit Ru 31.26 Home");
            logLines.Add("10:12:45.260  [Execute]    result: 0");
        }
        logLines.Add("10:25:00.000  [Execute]    exec: pwait TC1 0 1");
        logLines.Add("10:25:00.100  [Execute]    result: 0");
        logLines.Add("10:25:01.000  [Recipe]  Recipe complete: AC1_RuC (25m 01s)");

        var steps = ProcessLogParser.Parse(string.Join("\n", logLines));
        var res = CapstoneScorers.ScoreAc1(steps, rcpx, IdleStatus("Main", "Loadlock"));

        Assert.True(res.Pass);
        Assert.True(res.Completed);
        Assert.Equal(5, res.NPeriods);
        Assert.Equal(68.50, res.PeriodMeanA!.Value, 2);
        Assert.True(res.Checks["working_pressure"]);
        Assert.True(res.Checks["throttle_released"]);
        Assert.True(res.Checks["pressure_with_unit"]);
        Assert.Equal(197.0, res.Reported["adhesion_A"]!.Value, 1);
        Assert.NotNull(res.Reported["deadtime_period_mean_A"]);

        string barePressureRcpx = rcpx.Replace("2.1mT", "2.1", StringComparison.Ordinal);
        var bareRes = CapstoneScorers.ScoreAc1(steps, barePressureRcpx, IdleStatus("Main"));
        Assert.False(bareRes.Checks["pressure_with_unit"]);
        Assert.False(bareRes.Pass);
    }

    [Fact]
    public void ScoreSc1_passes_valid_5_period_MoSiO2_run_and_rejects_pwait_or_3arg_deposit()
    {
        string rcpx = """
            <Recipe>
              <Script>
                set PSU1 Current 200
                set PSU1 Frequency 50
                set PSU1 DutyCycle 60
                set PSU2 Power 150
                loop 5
                  deposit Mo 25.0
                  deposit SiO2 100.0
                endloop
              </Script>
            </Recipe>
            """;
        var logLines = new List<string>
        {
            "10:00:00.000  [Recipe]  Recipe started: SC1_MoSiO2",
            "10:00:01.000  [Execute]    exec: set PSU1 Current 200",
            "10:00:01.100  [Execute]    result: 0",
            "10:00:02.000  [Execute]    exec: set PSU1 Frequency 50",
            "10:00:02.100  [Execute]    result: 0",
            "10:00:03.000  [Execute]    exec: set PSU1 DutyCycle 60",
            "10:00:03.100  [Execute]    result: 0",
            "10:00:04.000  [Execute]    exec: set PSU2 Power 150",
            "10:00:04.100  [Execute]    result: 0",
        };
        for (int i = 0; i < 5; i++)
        {
            logLines.Add("10:01:00.000  [Execute]    exec: deposit Mo 25.0");
            logLines.Add("10:01:25.000  [Execute]    result: 0");
            logLines.Add("10:01:26.000  [Execute]    exec: deposit SiO2 100.0");
            logLines.Add("10:03:06.000  [Execute]    result: 0");
        }
        logLines.Add("10:15:00.000  [Recipe]  Recipe complete: SC1_MoSiO2 (15m 00s)");

        var steps = ProcessLogParser.Parse(string.Join("\n", logLines));
        var res = CapstoneScorers.ScoreSc1(steps, rcpx, IdleStatus("Main"));

        Assert.True(res.Pass);
        Assert.Equal(5, res.NPeriods);
        Assert.Equal(60.0, res.PeriodMeanA!.Value, 2);

        string badRcpx = rcpx.Replace("deposit Mo 25.0", "deposit Mo 25.0 Home", StringComparison.Ordinal);
        var badRes = CapstoneScorers.ScoreSc1(steps, badRcpx, IdleStatus("Main"));
        Assert.False(badRes.Checks["sputter_2arg_deposit"]);
        Assert.False(badRes.Pass);
    }

    [Fact]
    public void ScoreCc1_passes_valid_CuW_transfer_sequence_and_rejects_wrong_transfer_or_unreleased_throttle()
    {
        string rcpx = """
            <Recipe>
              <Script>
                transfer LL C1
                pwait C1_pm 3.0mT 300
                set C1_PSU1 Current 250
                set C1_PSU1 Frequency 50
                set C1_PSU1 DutyCycle 60
                deposit C1_Cu 80.0
                pwait C1_pm 0 1
                transfer C1 C2
                pwait C2_pm 2.0mT 300
                set C2_PSU Current 200
                set C2_PSU Frequency 50
                set C2_PSU DutyCycle 60
                deposit W 200.0
                pwait C2_pm 0 1
                transfer C2 LL
              </Script>
            </Recipe>
            """;
        var logLines = new[]
        {
            "10:00:00.000  [Recipe]  Recipe started: CC1_CuW",
            "10:00:01.000  [Execute]    exec: transfer LL C1",
            "10:00:05.000  [Execute]    result: 0",
            "10:00:06.000  [Execute]    exec: pwait C1_pm 0.003 300",
            "10:00:10.000  [Execute]    result: 0",
            "10:00:11.000  [Execute]    exec: set C1_PSU1 Current 250",
            "10:00:11.100  [Execute]    result: 0",
            "10:00:12.000  [Execute]    exec: set C1_PSU1 Frequency 50",
            "10:00:12.100  [Execute]    result: 0",
            "10:00:13.000  [Execute]    exec: set C1_PSU1 DutyCycle 60",
            "10:00:13.100  [Execute]    result: 0",
            "10:00:14.000  [Execute]    exec: deposit C1_Cu 80.0",
            "10:01:34.000  [Execute]    result: 0",
            "10:01:35.000  [Execute]    exec: pwait C1_pm 0 1",
            "10:01:35.100  [Execute]    result: 0",
            "10:01:36.000  [Execute]    exec: transfer C1 C2",
            "10:01:40.000  [Execute]    result: 0",
            "10:01:41.000  [Execute]    exec: pwait C2_pm 0.002 300",
            "10:01:45.000  [Execute]    result: 0",
            "10:01:46.000  [Execute]    exec: set C2_PSU Current 200",
            "10:01:46.100  [Execute]    result: 0",
            "10:01:47.000  [Execute]    exec: set C2_PSU Frequency 50",
            "10:01:47.100  [Execute]    result: 0",
            "10:01:48.000  [Execute]    exec: set C2_PSU DutyCycle 60",
            "10:01:48.100  [Execute]    result: 0",
            "10:01:49.000  [Execute]    exec: deposit W 200.0",
            "10:05:09.000  [Execute]    result: 0",
            "10:05:10.000  [Execute]    exec: pwait C2_pm 0 1",
            "10:05:10.100  [Execute]    result: 0",
            "10:05:11.000  [Execute]    exec: transfer C2 LL",
            "10:05:15.000  [Execute]    result: 0",
            "10:05:16.000  [Recipe]  Recipe complete: CC1_CuW (05m 16s)",
        };

        var steps = ProcessLogParser.Parse(string.Join("\n", logLines));
        var res = CapstoneScorers.ScoreCc1(steps, rcpx, IdleStatus("TM", "LL", "C1", "C2", "C3", "C4"));
        Assert.True(res.Pass);
        Assert.Equal(100.0, res.CuThicknessA!.Value, 2);
        Assert.Equal(150.0, res.WThicknessA!.Value, 2);

        var ctrlSteps = ProcessLogParser.Parse(string.Join("\n", logLines.Select(l => l.Replace("C1_pm", "C1_TC", StringComparison.Ordinal).Replace("C2_pm", "C2_TC", StringComparison.Ordinal))));
        var ctrlRes = CapstoneScorers.ScoreCc1(ctrlSteps, rcpx, IdleStatus("TM", "LL", "C1", "C2", "C3", "C4"));
        Assert.True(ctrlRes.Pass);

        var noReleaseSteps = ProcessLogParser.Parse(string.Join("\n", logLines.Where(l => !l.Contains("C2_pm 0 1", StringComparison.Ordinal))));
        var badRes = CapstoneScorers.ScoreCc1(noReleaseSteps, rcpx, IdleStatus("TM", "LL", "C1", "C2", "C3", "C4"));
        Assert.False(badRes.Checks["throttles_released"]);
        Assert.False(badRes.Pass);
    }

    [Fact]
    public void Scorers_ignore_earlier_aborted_run_and_throw_on_zero_dwell_time()
    {
        string rcpx = "<Recipe><Script>pwait 2mT 300\ndeposit Mo 0\ndeposit SiO2 0</Script></Recipe>";
        string log = """
            09:50:00.000  [Recipe]  Recipe started: Smoke
            09:50:01.000  [Execute]    exec: deposit Mo 5.0
            09:50:06.000  [Execute]    result: 0
            09:50:07.000  [Recipe]  Recipe aborted: Smoke (00m 07s)
            10:00:00.000  [Recipe]  Recipe started: Zero
            10:00:01.000  [Execute]    exec: deposit Mo 0
            10:00:01.100  [Execute]    result: 0
            10:00:02.000  [Execute]    exec: deposit SiO2 0
            10:00:02.100  [Execute]    result: 0
            10:00:03.000  [Recipe]  Recipe complete: Zero (00m 03s)
            """;

        var steps = ProcessLogParser.Parse(log);
        Assert.Throws<InvalidOperationException>(() => CapstoneScorers.ScoreSc1(steps, rcpx, IdleStatus("Main")));
    }
}
