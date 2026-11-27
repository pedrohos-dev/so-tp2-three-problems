namespace MontanhaRussa;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
            {
                PrintHelp();
                return 0;
            }

            Timing.EnableFineGrainedSleep();

            if (args.Contains("--validate"))
            {
                var results = TestSuite.Run(new Progress<string>(s => Console.WriteLine(s)));
                string report = TestSuite.ToText(results);
                Directory.CreateDirectory("results");
                File.WriteAllText("results/validacao.txt", report);
                Console.WriteLine();
                Console.WriteLine(report);
                return results.All(r => r.Passed) ? 0 : 1;
            }

            if (args.Contains("--single"))
            {
                SingleCarDemo.Validate(new Config(
                    Scenario.FixedPassengers, 1, 4, 12, 20, 1, 10, 1, 1));
                return 0;
            }

            bool quick = args.Contains("--quick");
            Scenario? scenario = GetInt(args, "--scenario") switch
            {
                1 => Scenario.FixedPassengers,
                2 => Scenario.ProportionalPassengers,
                3 => Scenario.NoWork,
                _ => null,
            };

            int repetitions = GetInt(args, "--reps") ?? (quick ? 3 : 30);
            string output = GetString(args, "--output")
                ?? (quick ? "results/medicoes_quick.csv" : "results/medicoes.csv");

            var configs = Experiment.Matrix(
                quick: quick,
                scenario: scenario,
                cars: GetInt(args, "--m"),
                capacity: GetInt(args, "--c"),
                repetitions: repetitions);

            var progress = new Progress<string>(message => Console.WriteLine(message));
            Console.WriteLine($"Executando experimento; saída: {Path.GetFullPath(output)}");
            var rows = Experiment.Run(configs, progress);
            Experiment.WriteCsv(rows, output);

            Console.WriteLine($"\n{rows.Count} medições gravadas em {Path.GetFullPath(output)}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERRO: " + ex);
            return 1;
        }
        finally
        {
            Timing.Restore();
        }
    }

    private static string? GetString(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name)
                return args[i + 1];
        return null;
    }

    private static int? GetInt(string[] args, string name)
        => int.TryParse(GetString(args, name), out int value) ? value : null;

    private static void PrintHelp()
    {
        Console.WriteLine("""
            Montanha-Russa — solução e experimento

            Uso:
              dotnet run -c Release                    Experimento completo (r = 30)
              dotnet run -c Release -- --quick         Matriz reduzida (r = 3)
              dotnet run -c Release -- --validate      Verifica os invariantes
              dotnet run -c Release -- --single        Demonstração do caso 1 carro

            Opções:
              --scenario 1|2|3   Executa apenas um cenário
              --m N              Executa apenas com N carros
              --c N              Executa apenas com capacidade N
              --reps N           Repetições por configuração
              --output CAMINHO   Arquivo CSV de saída
              --help             Esta ajuda
            """);
    }
}
