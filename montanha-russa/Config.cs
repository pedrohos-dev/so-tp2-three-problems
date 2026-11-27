namespace MontanhaRussa;

public enum Scenario
{
    FixedPassengers = 1,
    ProportionalPassengers = 2,
    NoWork = 3,
}

/// <summary>
/// Configuração de uma execução do experimento.
/// </summary>
public sealed record Config(
    Scenario Scenario,
    int Cars,
    int Capacity,
    int Passengers,
    int RidesPerCar,
    int LoadMs,
    int RunMs,
    int UnloadMs,
    int Repetitions = 30,
    bool Validate = false)
{
    public string ScenarioName => Scenario switch
    {
        Scenario.FixedPassengers => "fixa",
        Scenario.ProportionalPassengers => "proporcional",
        Scenario.NoWork => "sem-trabalho",
        _ => throw new ArgumentOutOfRangeException(nameof(Scenario)),
    };

    /// <summary>Total de embarques (e desembarques) que devem ocorrer na execução.</summary>
    public int TotalBoardings => Cars * RidesPerCar * Capacity;

    public bool HasSimulatedWork => LoadMs > 0 || RunMs > 0 || UnloadMs > 0;
}
