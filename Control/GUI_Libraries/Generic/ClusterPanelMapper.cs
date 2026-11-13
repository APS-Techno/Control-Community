using System.Linq;
using APS.ProcessPanel.Contracts;

namespace APS.Controls.Generic;

internal static class ClusterPanelMapper
{
    // Pump ids contain a space so they can never collide with a device key.
    public static string SlitId(string key)   => "SV_" + key;
    public static string TurboId(string key)  => key + " TP";
    public static string ScrollId(string key) => key + " SP";

    private static bool IsProcessChamber(string key)
        => key.Length > 0 && (key[0] == 'C' || key[0] == 'c');

    // During a recipe a chamber back at Idle stays live (the wafer moved on); Stopped and Maintenance always win.
    private static ReadingState StateFor(SystemStateCode s, bool recipeActive) => s switch
    {
        SystemStateCode.Processing  => ReadingState.Ok,
        SystemStateCode.Stopped     => ReadingState.Alarm,
        SystemStateCode.Maintenance => ReadingState.Warning,
        _                           => recipeActive ? ReadingState.Ok : ReadingState.Off,
    };

    public static ProcessReading[] ToReadings(ClusterSnapshot snap, bool recipeActive = false)
    {
        var list = new List<ProcessReading>();
        foreach (var c in snap.Chambers)
        {
            var st = StateFor(c.State, recipeActive);
            list.Add(new($"{c.Key}·pm", c.Pressure, st));
            if (IsProcessChamber(c.Key))
            {
                list.Add(new($"{c.Key}·tmp", c.Temperature, st));
                foreach (var f in c.Flows ?? System.Array.Empty<ClusterReadingInfo>())
                    list.Add(new($"{c.Key}·{f.Label}", f.Value, st));
                foreach (var p in c.Powers ?? System.Array.Empty<ClusterReadingInfo>())
                    list.Add(new($"{c.Key}·{p.Label}", p.Value, st));
            }
        }
        list.Add(new("TM·pm", snap.Robot.Pressure,
            snap.Robot.ErrorActive ? ReadingState.Alarm
            : recipeActive ? ReadingState.Ok : ReadingState.Off));
        return list.ToArray();
    }

    public static ValveInfo[] ToValves(ClusterSnapshot snap)
    {
        var list = new List<ValveInfo>();
        foreach (var c in snap.Chambers)
        {
            list.Add(new(SlitId(c.Key), c.SlitOpen));
            foreach (var v in c.Valves ?? System.Array.Empty<ClusterValveInfo>())
                list.Add(new(v.Id, v.IsOpen));
            list.Add(new(TurboId(c.Key),  c.TurboOn));
            list.Add(new(ScrollId(c.Key), c.ScrollOn));
        }
        list.Add(new(TurboId("TM"),  snap.Robot.TurboOn));
        list.Add(new(ScrollId("TM"), snap.Robot.ScrollOn));
        return list.ToArray();
    }

    public static string ToHeaderSummary(ClusterSnapshot snap)
    {
        int proc = snap.Chambers.Count(c => c.State == SystemStateCode.Processing);
        int idle = snap.Chambers.Count(c => c.State == SystemStateCode.Idle);
        string face = snap.Robot.CurrentFace > 0 ? snap.Robot.CurrentFace.ToString() : "—";
        return $"Cluster  ·  {proc} processing  ·  {idle} idle  ·  robot ▸ face {face}";
    }
}
