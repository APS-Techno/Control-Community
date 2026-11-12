namespace APS.SystemControl.Core.Remote.Agent;

internal sealed record DepositionPlanRequest(
    double PeriodA,
    double Gamma,
    double HeavyRateAPerS,
    double LightRateAPerS,
    int BilayerCount,
    double? AdhesionThicknessA = null,
    double? AdhesionRateAPerS = null,
    double? DeadTimeS = null,
    double? DeadTimeRateAPerS = null);

internal static class DepositionPlanCalculator
{
    public static object Compute(DepositionPlanRequest r)
    {
        RequirePositive(r.PeriodA, "period_A");
        if (!double.IsFinite(r.Gamma) || r.Gamma <= 0 || r.Gamma >= 1)
            throw BadRequest("gamma must be a finite number with 0 < gamma < 1");
        RequirePositive(r.HeavyRateAPerS, "heavy_rate_A_per_s");
        RequirePositive(r.LightRateAPerS, "light_rate_A_per_s");
        if (r.BilayerCount < 1)
            throw BadRequest("bilayer_count must be a whole number >= 1");

        if (r.AdhesionThicknessA is { } adhesion)
        {
            if (!double.IsFinite(adhesion) || adhesion < 0)
                throw BadRequest("adhesion_thickness_A must be a finite number >= 0");
            if (adhesion > 0 && r.AdhesionRateAPerS is null)
                throw BadRequest("adhesion_rate_A_per_s is required when adhesion_thickness_A > 0");
        }
        if (r.AdhesionRateAPerS is { } adhesionRate)
            RequirePositive(adhesionRate, "adhesion_rate_A_per_s");

        if (r.DeadTimeS is { } deadTime)
        {
            if (!double.IsFinite(deadTime) || deadTime < 0)
                throw BadRequest("dead_time_s must be a finite number >= 0");
            if (r.DeadTimeRateAPerS is null)
                throw BadRequest("dead_time_rate_A_per_s is required when dead_time_s is given");
        }
        if (r.DeadTimeRateAPerS is { } deadTimeRate)
            RequirePositive(deadTimeRate, "dead_time_rate_A_per_s");

        double heavyThickness = r.PeriodA * r.Gamma;
        double lightThickness = r.PeriodA - heavyThickness;
        double heavyDwell = heavyThickness / r.HeavyRateAPerS;
        double lightDwell = lightThickness / r.LightRateAPerS;
        RequireWithinRange(heavyDwell, "heavy dwell");
        RequireWithinRange(lightDwell, "light dwell");

        double heavyDwellRounded = Round(heavyDwell, 2);
        double lightDwellRounded = Round(lightDwell, 2);
        int heavyDwellInt = (int)Math.Round(heavyDwell, MidpointRounding.AwayFromZero);
        int lightDwellInt = (int)Math.Round(lightDwell, MidpointRounding.AwayFromZero);

        double? adhesionDwell = r.AdhesionThicknessA is not { } thickness ? null
            : thickness == 0 ? 0.0
            : Round(RequireWithinRange(thickness / r.AdhesionRateAPerS!.Value, "adhesion dwell"), 2);
        double? deadTimeHeavyThickness = r.DeadTimeS is not { } lost ? null
            : Round(Math.Max(0, r.DeadTimeRateAPerS!.Value * (heavyDwell - lost)), 2);

        return new
        {
            measurementStatus = "planned_from_rate_and_dwell_not_measured",
            period_A = r.PeriodA,
            gamma = r.Gamma,
            bilayer_count = r.BilayerCount,
            heavy_thickness_A = Round(heavyThickness, 6),
            light_thickness_A = Round(lightThickness, 6),
            heavy_dwell_exact_s = Round(heavyDwell, 6),
            light_dwell_exact_s = Round(lightDwell, 6),
            heavy_dwell_s = heavyDwellRounded,
            light_dwell_s = lightDwellRounded,
            period_from_rounded_dwells_A = Round(
                heavyDwellRounded * r.HeavyRateAPerS + lightDwellRounded * r.LightRateAPerS, 6),
            integer_rounded_check = new
            {
                heavy_dwell_int_s = heavyDwellInt,
                light_dwell_int_s = lightDwellInt,
                period_from_integer_dwells_A = Round(
                    heavyDwellInt * r.HeavyRateAPerS + lightDwellInt * r.LightRateAPerS, 2),
            },
            adhesion_dwell_s = adhesionDwell,
            dead_time_heavy_thickness_A = deadTimeHeavyThickness,
        };
    }

    private static double Round(double value, int digits) =>
        Math.Round(value, digits, MidpointRounding.AwayFromZero);

    private static void RequirePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw BadRequest($"{name} must be a finite number > 0");
    }

    private static double RequireWithinRange(double seconds, string what)
    {
        if (!double.IsFinite(seconds) || seconds >= int.MaxValue)
            throw BadRequest($"{what} is not a usable time; check the rates and thickness");
        return seconds;
    }

    private static AgentOpException BadRequest(string message) => new(AgentErrors.BadRequest, message);
}
