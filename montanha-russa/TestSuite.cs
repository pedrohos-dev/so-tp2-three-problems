using System.Globalization;

namespace MontanhaRussa;

public sealed record TestResult(
    string Id,
    string Description,
    string Configuration,
    bool Passed,
    double Seconds,
    string Detail);

/// <summary>Bateria de testes de correção com os invariantes instrumentados ligados.</summary>
public static class TestSuite
{
    private sealed record Case(string Id, string Description, Config Config);

    private static readonly Case[] Cases =
    [
        new("TC1", "Um carro, sem trabalho simulado",
            new Config(Scenario.FixedPassengers, 1, 4, 8, 5, 0, 0, 0, 1, true)),
        new("TC2", "Um carro, com trabalho simulado",
            new Config(Scenario.FixedPassengers, 1, 4, 8, 5, 1, 2, 1, 1, true)),
        new("TC3", "Três carros correndo em paralelo",
            new Config(Scenario.ProportionalPassengers, 3, 4, 24, 5, 1, 2, 1, 1, true)),
        new("TC4", "Oito carros, capacidade 2, sem trabalho",
            new Config(Scenario.ProportionalPassengers, 8, 2, 32, 4, 0, 1, 0, 1, true)),
        new("TC5", "Passageiros fixos (200) com 5 carros",
            new Config(Scenario.FixedPassengers, 5, 4, 200, 3, 0, 0, 0, 1, true)),
        new("TC6", "Capacidade 8 com seis carros",
            new Config(Scenario.ProportionalPassengers, 6, 8, 96, 3, 1, 10, 1, 1, true)),
        new("TC7", "Número máximo de carros (50), sem trabalho",
            new Config(Scenario.ProportionalPassengers, 50, 4, 200, 2, 0, 0, 0, 1, true)),
        new("TC8", "Doze carros, dez voltas, trabalho simulado",
            new Config(Scenario.ProportionalPassengers, 12, 4, 96, 10, 1, 10, 1, 1, true)),
    ];

    public static List<TestResult> Run(IProgress<string>? progress = null)
    {
        var results = new List<TestResult>();

        foreach (var test in Cases)
        {
            progress?.Report($"{test.Id}: {test.Description}");
            string configuration = $"m={test.Config.Cars} C={test.Config.Capacity} " +
                                   $"n={test.Config.Passengers} R={test.Config.RidesPerCar} " +
                                   $"t=({test.Config.LoadMs},{test.Config.RunMs},{test.Config.UnloadMs})";
            try
            {
                var row = Experiment.RunOnce(test.Config, 1);
                results.Add(new TestResult(
                    test.Id, test.Description, configuration, true, row.TS,
                    $"{test.Config.TotalBoardings} embarques/desembarques; invariantes OK"));
            }
            catch (Exception ex)
            {
                results.Add(new TestResult(
                    test.Id, test.Description, configuration, false, 0, ex.Message));
            }
        }

        var single = new Config(Scenario.FixedPassengers, 1, 4, 12, 10, 1, 2, 1, 1);
        progress?.Report("TC9: solução do problema 1 (arquivo separado)");
        try
        {
            double seconds = SingleCarDemo.Validate(single);
            results.Add(new TestResult(
                "TC9", "Solução isolada do problema 1",
                "m=1 C=4 n=12 R=10 t=(1,2,1)", true, seconds,
                "40 embarques/desembarques; contagens conferem"));
        }
        catch (Exception ex)
        {
            results.Add(new TestResult(
                "TC9", "Solução isolada do problema 1",
                "m=1 C=4 n=12 R=10 t=(1,2,1)", false, 0, ex.Message));
        }

        return results;
    }

    public static string ToText(IReadOnlyList<TestResult> results)
    {
        var lines = new List<string>
        {
            "Bateria de testes de correção — montanha-russa",
            "Data: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            "Invariantes instrumentados: ligados em todos os casos.",
            "",
            "ID | Caso | Configuração | Resultado | Tempo (s) | Detalhe",
            "---|------|--------------|-----------|-----------|--------",
        };

        foreach (var r in results)
        {
            lines.Add(string.Join(" | ",
                r.Id,
                r.Description,
                r.Configuration,
                r.Passed ? "PASSOU" : "FALHOU",
                r.Seconds.ToString("F4", CultureInfo.InvariantCulture),
                r.Detail));
        }

        int passed = results.Count(r => r.Passed);
        lines.Add("");
        lines.Add($"Resumo: {passed}/{results.Count} casos passaram.");
        return string.Join(Environment.NewLine, lines);
    }
}
