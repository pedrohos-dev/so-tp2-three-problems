using System.Runtime.InteropServices;

namespace MontanhaRussa;

/// <summary>
/// Trabalho simulado das fases load/run/unload.
/// No Windows, a resolução padrão do Sleep é de ~15,6 ms; timeBeginPeriod(1)
/// aproxima isso de 1 ms, necessário para que tL = 1 ms e tU = 1 ms sejam
/// realmente medidos como 1 ms.
/// </summary>
public static class Timing
{
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint period);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint period);

    private static bool _adjusted;

    public static void EnableFineGrainedSleep()
    {
        if (!OperatingSystem.IsWindows() || _adjusted)
            return;

        TimeBeginPeriod(1);
        _adjusted = true;
    }

    public static void Restore()
    {
        if (!OperatingSystem.IsWindows() || !_adjusted)
            return;

        TimeEndPeriod(1);
        _adjusted = false;
    }

    public static void Simulate(int milliseconds)
    {
        if (milliseconds > 0)
            Thread.Sleep(milliseconds);
    }
}
