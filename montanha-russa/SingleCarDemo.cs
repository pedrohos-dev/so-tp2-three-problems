using System.Diagnostics;

namespace MontanhaRussa;

/// <summary>
/// Solução clássica do problema 1 (um único carro):
///   prontoParaEmbarcar, embarcados, prontoParaDesembarcar, desembarcados.
/// Mantida como código separado para deixar explícita a solução antes da
/// generalização para vários carros.
/// </summary>
public static class SingleCarDemo
{
    public static double Validate(Config cfg)
    {
        var readyToBoard = new Semaphore(0);
        var boarded = new Semaphore(0);
        var readyToUnboard = new Semaphore(0);
        var unboarded = new Semaphore(0);

        bool terminated = false;
        int boards = 0;
        int unboards = 0;

        var threads = new List<Thread>
        {
            new(() =>
            {
                for (int round = 0; round < cfg.RidesPerCar; round++)
                {
                    // load()
                    Timing.Simulate(cfg.LoadMs);
                    for (int k = 0; k < cfg.Capacity; k++)
                        readyToBoard.signal();
                    for (int k = 0; k < cfg.Capacity; k++)
                        boarded.wait();

                    // run()
                    Timing.Simulate(cfg.RunMs);

                    // unload()
                    Timing.Simulate(cfg.UnloadMs);
                    for (int k = 0; k < cfg.Capacity; k++)
                        readyToUnboard.signal();
                    for (int k = 0; k < cfg.Capacity; k++)
                        unboarded.wait();
                }

                Volatile.Write(ref terminated, true);
                for (int p = 0; p < cfg.Passengers; p++)
                    readyToBoard.signal();
            }) { Name = "carro-unico" },
        };

        for (int p = 0; p < cfg.Passengers; p++)
        {
            threads.Add(new Thread(() =>
            {
                while (true)
                {
                    readyToBoard.wait();
                    if (Volatile.Read(ref terminated))
                        return;

                    // board()
                    Interlocked.Increment(ref boards);
                    boarded.signal();

                    readyToUnboard.wait();

                    // unboard()
                    Interlocked.Increment(ref unboards);
                    unboarded.signal();
                }
            }) { Name = $"passageiro-{p}" });
        }

        var clock = Stopwatch.StartNew();
        foreach (var thread in threads)
            thread.Start();
        foreach (var thread in threads)
            thread.Join();
        clock.Stop();

        int expected = cfg.RidesPerCar * cfg.Capacity;
        if (boards != expected || unboards != expected)
            throw new InvalidOperationException(
                $"um carro: esperado {expected} embarques/desembarques, obtidos {boards}/{unboards}");

        Console.WriteLine(
            $"  um carro: OK  C={cfg.Capacity} n={cfg.Passengers} R={cfg.RidesPerCar} " +
            $"embarques={boards} tempo={clock.Elapsed.TotalSeconds:F3}s");

        return clock.Elapsed.TotalSeconds;
    }
}
