namespace MontanhaRussa;

/// <summary>
/// Semáforo contador mínimo, no estilo P/V do pseudocódigo da disciplina,
/// implementado sobre <see cref="SemaphoreSlim"/>.
/// </summary>
public sealed class Semaphore
{
    private readonly SemaphoreSlim _inner;

    public Semaphore(int initialCount = 0) => _inner = new SemaphoreSlim(initialCount);

    /// <summary>P: espera até que o contador seja maior que zero e o decrementa.</summary>
    public void wait() => _inner.Wait();

    /// <summary>V: incrementa o contador e acorda um esperador, se houver.</summary>
    public void signal() => _inner.Release();
}
