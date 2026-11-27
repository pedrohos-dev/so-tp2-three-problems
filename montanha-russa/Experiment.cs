namespace MontanhaRussa;

public static class Experiment
{
    /// <summary>
    /// Matriz do experimento. Combinações em que m*C &gt; n são descartadas,
    /// pois não haveria passageiros suficientes para encher todos os carros.
    /// </summary>
    public static IEnumerable<Config> Matrix(
        bool quick = false,
        Scenario? scenario = null,
        int? cars = null,
        int? capacity = null,
        int repetitions = 30)
    {
        int[] carCounts = quick ? [1, 2, 5] : [1, 2, 5, 10, 15, 20, 25, 30, 40, 50];
        int[] capacities = quick ? [4] : [4, 2, 8];

        foreach (Scenario s in Enum.GetValues<Scenario>())
        {
            if (scenario is not null && s != scenario)
                continue;

            foreach (int c in capacities)
            {
                if (capacity is not null && c != capacity)
                    continue;

                foreach (int m in carCounts)
                {
                    if (cars is not null && m != cars)
                        continue;

                    int n = s == Scenario.FixedPassengers ? 200 : 2 * m * c;
                    if (m * c > n)
                        continue;

                    bool noWork = s == Scenario.NoWork;
                    yield return new Config(
                        s, m, c, n,
                        RidesPerCar: 20,
                        LoadMs: noWork ? 0 : 1,
                        RunMs: noWork ? 0 : 10,
                        UnloadMs: noWork ? 0 : 1,
                        Repetitions: repetitions);
                }
            }
        }
    }

    public static List<ExperimentRow> Run(
        IEnumerable<Config> configs,
        IProgress<string>? progress = null)
    {
        var rows = new List<ExperimentRow>();

        foreach (var cfg in configs)
        {
            progress?.Report(
                $"cenário={cfg.ScenarioName} m={cfg.Cars} C={cfg.Capacity} " +
                $"n={cfg.Passengers} rep={cfg.Repetitions}");

            for (int rep = 1; rep <= cfg.Repetitions; rep++)
                rows.Add(RunOnce(cfg, rep));
        }

        ApplySpeedup(rows);
        return rows;
    }

    /// <summary>Executa uma repetição completa: cria as threads, sincroniza o fim e mede.</summary>
    public static ExperimentRow RunOnce(Config cfg, int rep)
    {
        var coaster = new RollerCoaster(cfg);
        var threads = new List<Thread>(cfg.Cars + cfg.Passengers);

        for (int i = 0; i < cfg.Cars; i++)
        {
            var car = new Car(coaster, i);
            threads.Add(new Thread(car.Drive) { IsBackground = true, Name = $"carro-{i}" });
        }

        for (int p = 0; p < cfg.Passengers; p++)
        {
            var passenger = new Passenger(coaster, p, p % cfg.Cars);
            threads.Add(new Thread(passenger.Ride) { IsBackground = true, Name = $"passageiro-{p}" });
        }

        foreach (var thread in threads)
            thread.Start();
        foreach (var thread in threads)
            thread.Join();

        return coaster.Snapshot(cfg, rep);
    }

    public static void WriteCsv(IEnumerable<ExperimentRow> rows, string path)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        using var writer = new StreamWriter(fullPath, append: false);
        writer.WriteLine(ExperimentRow.Header);
        foreach (var row in rows)
            writer.WriteLine(row.ToCsv());
    }

    /// <summary>
    /// Speedup e eficiência são definidos apenas para o cenário 1, tomando como
    /// base o tempo médio de m = 1 com a mesma capacidade C.
    /// </summary>
    private static void ApplySpeedup(List<ExperimentRow> rows)
    {
        var baseline = rows
            .Where(r => r.Scenario == "fixa" && r.M == 1)
            .GroupBy(r => r.C)
            .ToDictionary(g => g.Key, g => g.Average(r => r.TS));

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Scenario != "fixa" || !baseline.TryGetValue(row.C, out double t1))
                continue;

            double speedup = t1 / row.TS;
            rows[i] = row with { Speedup = speedup, Efficiency = speedup / row.M };
        }
    }
}
