using EmulatorCore.Interfaces;

namespace EmulatorCore.Models;

public class SimulationState
{
    public float SimTime { get; set; }
    public float DeltaTime { get; set; }
    public float FilmThickness { get; set; }
    public required IPhysicsModel Physics { get; init; }

    private IDataSource _dataSource = null!;
    public required IDataSource DataSource { get => _dataSource; init => _dataSource = value; }
    public void SetDataSource(IDataSource source) => _dataSource = source;

    private readonly Dictionary<string, ChamberState> _chambers
        = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, ChamberState> Chambers => _chambers;

    public ChamberState SetChamber(ChamberState chamber)
    {
        _chambers[chamber.Id] = chamber;
        return chamber;
    }

    public void ClearChambers() => _chambers.Clear();

    public ThermalState Thermal { get; } = new();

    public Dictionary<string, bool> ValveStates { get; } = new();

    public List<VentMapping> VentMappings { get; } = [];

    public List<SlitConnection> SlitConnections { get; } = [];
}

public class VentMapping
{
    public required string ValveId { get; init; }
    public required string Chamber { get; init; }
    public float Conductance { get; init; } = 2.0f;
}

public class SlitConnection
{
    public required string ValveId { get; init; }
    public required string ChamberA { get; init; }
    public required string ChamberB { get; init; }
}
