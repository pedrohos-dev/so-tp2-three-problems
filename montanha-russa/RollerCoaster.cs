using System.Diagnostics;

namespace MontanhaRussa;

/// <summary>
/// "Bastão" de desembarque: o carro espera o semáforo do predecessor (se houver)
/// antes de descarregar e, ao terminar, acorda o sucessor.
/// </summary>
public readonly record struct Handoff(Semaphore? Predecessor, Semaphore Done);

/// <summary>
/// Estado compartilhado da solução com vários carros.
///
/// Restrições atendidas:
///  - apenas um carro embarcando por vez ......... semáforo _loading
///  - vários carros podem correr ao mesmo tempo .. o predecessor só é esperado no desembarque
///  - desembarque na ordem de embarque ........... fila _delivery + semáforos Handoff
///  - carga inteira desembarca antes da próxima .. o sucessor só é acordado após C unboards
/// </summary>
public sealed class RollerCoaster
{
    private readonly Config _cfg;
    private readonly Semaphore _loading = new(1);

    private readonly Semaphore[] _readyToBoard;
    private readonly Semaphore[] _boarded;
    private readonly Semaphore[] _readyToUnboard;
    private readonly Semaphore[] _unboarded;

    private readonly object _deliveryLock = new();
    private readonly Queue<Semaphore> _delivery = new();
    private Semaphore? _tail;

    private readonly bool[] _terminated;
    private readonly int[] _passengersPerCar;

    // Uma posição por (passageiro, volta): cada passageiro embarca no máximo uma vez por volta.
    private readonly long[] _latencyTicks;

    private readonly Stopwatch _clock;
    private readonly TimeSpan _cpuStart;

    // Instrumentação de correção (somente quando cfg.Validate == true).
    private readonly int[]? _rideLoadId;
    private readonly int[]? _unboardLoadId;
    private readonly int[] _loadsPerCar;
    private readonly int[] _boardingCount;
    private readonly int[] _unboardingCount;
    private readonly bool[] _loadCalled;
    private readonly bool[] _unloadCalled;
    private readonly int[] _currentLoadId;
    private readonly List<string> _violations = new();
    private readonly object _violationLock = new();
    private int _loadCounter;
    private int _activeLoaders;
    private int _boardSeq;
    private int _unboardSeq;

    public RollerCoaster(Config cfg)
    {
        _cfg = cfg;
        int m = cfg.Cars;

        _readyToBoard = new Semaphore[m];
        _boarded = new Semaphore[m];
        _readyToUnboard = new Semaphore[m];
        _unboarded = new Semaphore[m];
        _terminated = new bool[m];
        _loadsPerCar = new int[m];
        _boardingCount = new int[m];
        _unboardingCount = new int[m];
        _loadCalled = new bool[m];
        _unloadCalled = new bool[m];
        _currentLoadId = new int[m];

        for (int i = 0; i < m; i++)
        {
            _readyToBoard[i] = new Semaphore(0);
            _boarded[i] = new Semaphore(0);
            _readyToUnboard[i] = new Semaphore(0);
            _unboarded[i] = new Semaphore(0);
        }

        _passengersPerCar = new int[m];
        for (int p = 0; p < cfg.Passengers; p++)
            _passengersPerCar[p % m]++;

        _latencyTicks = new long[checked(cfg.Passengers * cfg.RidesPerCar)];

        if (cfg.Validate)
        {
            _rideLoadId = new int[cfg.TotalBoardings];
            _unboardLoadId = new int[cfg.TotalBoardings];
        }

        _clock = Stopwatch.StartNew();
        using var process = Process.GetCurrentProcess();
        _cpuStart = process.TotalProcessorTime;
    }

    public int Cars => _cfg.Cars;
    public int Capacity => _cfg.Capacity;
    public int Passengers => _cfg.Passengers;
    public int RidesPerCar => _cfg.RidesPerCar;

    // ---------------------------------------------------------------- carro

    /// <summary>load(): exclusão de embarque + trabalho simulado.</summary>
    public void StartLoading(int car)
    {
        _loading.wait();

        if (_cfg.Validate)
        {
            if (Interlocked.Increment(ref _activeLoaders) != 1)
                Report("mais de um carro executando load() ao mesmo tempo");

            Volatile.Write(ref _loadCalled[car], true);
            Volatile.Write(ref _unloadCalled[car], false);
            _currentLoadId[car] = ++_loadCounter;
            _loadsPerCar[car]++;
        }

        Timing.Simulate(_cfg.LoadMs);
    }

    /// <summary>Autoriza C passageiros a embarcarem.</summary>
    public void OpenBoarding(int car)
    {
        for (int k = 0; k < _cfg.Capacity; k++)
            _readyToBoard[car].signal();
    }

    /// <summary>Espera os C sinais de "embarque concluído".</summary>
    public void WaitForBoarded(int car)
    {
        for (int k = 0; k < _cfg.Capacity; k++)
            _boarded[car].wait();

        if (_cfg.Validate)
        {
            if (Volatile.Read(ref _boardingCount[car]) != _cfg.Capacity)
                Report($"carro {car}: número de embarques diferente de C");
            Volatile.Write(ref _boardingCount[car], 0);
        }
    }

    /// <summary>
    /// Entra na fila de desembarque ainda sob o semáforo de carregamento,
    /// de modo que a ordem da fila seja exatamente a ordem de embarque.
    /// </summary>
    public Handoff JoinUnloadQueue(int car)
    {
        var done = new Semaphore(0);
        Semaphore? predecessor;

        lock (_deliveryLock)
        {
            predecessor = _tail;
            _tail = done;
            _delivery.Enqueue(done);
        }

        return new Handoff(predecessor, done);
    }

    /// <summary>Libera o próximo carro para embarcar.</summary>
    public void EndLoading(int car)
    {
        if (_cfg.Validate)
            Interlocked.Decrement(ref _activeLoaders);

        _loading.signal();
    }

    /// <summary>run(): trabalho simulado na pista.</summary>
    public void RunTrack() => Timing.Simulate(_cfg.RunMs);

    /// <summary>Espera o carro que embarcou antes terminar de descarregar.</summary>
    public void AwaitTurn(Handoff handoff) => handoff.Predecessor?.wait();

    /// <summary>unload(): trabalho simulado + autorização para desembarcar.</summary>
    public void StartUnloading(int car)
    {
        if (_cfg.Validate)
            Volatile.Write(ref _unloadCalled[car], true);

        Timing.Simulate(_cfg.UnloadMs);
    }

    public void OpenUnboarding(int car)
    {
        for (int k = 0; k < _cfg.Capacity; k++)
            _readyToUnboard[car].signal();
    }

    public void WaitForUnboarded(int car)
    {
        for (int k = 0; k < _cfg.Capacity; k++)
            _unboarded[car].wait();

        if (_cfg.Validate)
        {
            if (Volatile.Read(ref _unboardingCount[car]) != _cfg.Capacity)
                Report($"carro {car}: número de desembarques diferente de C");
            Volatile.Write(ref _unboardingCount[car], 0);
        }
    }

    /// <summary>Acorda o próximo carro da fila e retira este da fila.</summary>
    public void CompleteUnload(int car, Handoff handoff)
    {
        lock (_deliveryLock)
        {
            var head = _delivery.Dequeue();
            if (_cfg.Validate && !ReferenceEquals(head, handoff.Done))
                Report("desembarque concluído fora da ordem da fila");
        }

        handoff.Done.signal();
        if (_cfg.Validate)
            Volatile.Write(ref _loadCalled[car], false);
    }

    /// <summary>
    /// Após a última volta, acorda todos os passageiros ainda bloqueados para
    /// que possam sair; eles verificam _terminated ao serem acordados.
    /// </summary>
    public void Terminate(int car)
    {
        Volatile.Write(ref _terminated[car], true);
        for (int k = 0; k < _passengersPerCar[car]; k++)
            _readyToBoard[car].signal();
    }

    // ----------------------------------------------------------- passageiro

    public bool IsTerminated(int car) => Volatile.Read(ref _terminated[car]);

    public static long RequestTimestamp() => Stopwatch.GetTimestamp();

    public void WaitForBoarding(int car) => _readyToBoard[car].wait();

    public void RecordLatency(int passenger, int ride, long requestTimestamp)
    {
        long elapsedTicks = Stopwatch.GetTimestamp() - requestTimestamp;
        int index = passenger * _cfg.RidesPerCar + ride;
        if ((uint)index < (uint)_latencyTicks.Length)
            _latencyTicks[index] = elapsedTicks;
    }

    /// <summary>board(): retorna o número sequencial da carga, para validação.</summary>
    public int Board(int passenger, int ride, int car)
    {
        int boardSeq = 0;

        if (_cfg.Validate)
        {
            if (!Volatile.Read(ref _loadCalled[car]))
                Report($"passageiro {passenger}: board() antes do load() do carro {car}");

            boardSeq = Interlocked.Increment(ref _boardSeq);
            _rideLoadId![boardSeq - 1] = _currentLoadId[car];
            Interlocked.Increment(ref _boardingCount[car]);
        }

        _boarded[car].signal();
        return boardSeq;
    }

    public void WaitForUnboarding(int car) => _readyToUnboard[car].wait();

    /// <summary>unboard()</summary>
    public void Unboard(int car, int boardSeq)
    {
        if (_cfg.Validate)
        {
            if (!Volatile.Read(ref _unloadCalled[car]))
                Report($"carro {car}: unboard() antes do unload()");

            int seq = Interlocked.Increment(ref _unboardSeq);
            _unboardLoadId![seq - 1] = _rideLoadId![boardSeq - 1];
            Interlocked.Increment(ref _unboardingCount[car]);
        }

        _unboarded[car].signal();
    }

    // ----------------------------------------------------------- resultados

    public ExperimentRow Snapshot(Config cfg, int rep)
    {
        _clock.Stop();

        double cpuSeconds;
        using (var process = Process.GetCurrentProcess())
            cpuSeconds = (process.TotalProcessorTime - _cpuStart).TotalSeconds;

        double latencyMs = 0;
        long sum = 0;
        int count = 0;
        foreach (long ticks in _latencyTicks)
        {
            if (ticks <= 0)
                continue;
            sum += ticks;
            count++;
        }
        if (count > 0)
            latencyMs = (double)sum / count * 1000.0 / Stopwatch.Frequency;

        if (cfg.Validate)
            Verify();

        double t = _clock.Elapsed.TotalSeconds;
        return new ExperimentRow(
            cfg.ScenarioName, cfg.Cars, cfg.Capacity, cfg.Passengers, rep,
            t, cfg.TotalBoardings / t, latencyMs, cpuSeconds,
            OverheadMs: t * 1000.0 / (cfg.Cars * cfg.RidesPerCar));
    }

    private void Verify()
    {
        if (_boardSeq != _cfg.TotalBoardings)
            Report($"embarques: esperado {_cfg.TotalBoardings}, obtido {_boardSeq}");

        if (_unboardSeq != _cfg.TotalBoardings)
            Report($"desembarques: esperado {_cfg.TotalBoardings}, obtido {_unboardSeq}");

        if (_loadCounter != _cfg.Cars * _cfg.RidesPerCar)
            Report($"cargas: esperado {_cfg.Cars * _cfg.RidesPerCar}, obtido {_loadCounter}");

        for (int i = 0; i < _cfg.Cars; i++)
            if (_loadsPerCar[i] != _cfg.RidesPerCar)
                Report($"carro {i}: {_loadsPerCar[i]} cargas, esperado {_cfg.RidesPerCar}");

        if (_activeLoaders != 0)
            Report("carro ainda dentro de load() ao final da execução");

        lock (_deliveryLock)
            if (_delivery.Count != 0)
                Report("fila de desembarque não vazia ao final da execução");

        // A sequência de ids de carga no desembarque deve ser não decrescente:
        // todos os passageiros da carga k desembarcam antes da carga k+1.
        for (int seq = 2; seq <= _unboardSeq && seq <= _unboardLoadId!.Length; seq++)
        {
            if (_unboardLoadId[seq - 1] < _unboardLoadId[seq - 2])
            {
                Report("desembarque fora da ordem de embarque das cargas");
                break;
            }
        }

        lock (_violationLock)
        {
            if (_violations.Count > 0)
                throw new InvalidOperationException(
                    "Invariantes violadas: " + string.Join(" | ", _violations));
        }
    }

    private void Report(string message)
    {
        lock (_violationLock)
            _violations.Add(message);
    }
}
