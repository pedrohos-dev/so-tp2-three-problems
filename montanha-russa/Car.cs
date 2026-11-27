namespace MontanhaRussa;

/// <summary>
/// Thread do carro: repete R voltas executando load -> run -> unload.
/// </summary>
public sealed class Car
{
    private readonly RollerCoaster _coaster;
    private readonly int _id;
    private Handoff _handoff;

    public Car(RollerCoaster coaster, int id)
    {
        _coaster = coaster;
        _id = id;
    }

    public void Drive()
    {
        for (int round = 0; round < _coaster.RidesPerCar; round++)
        {
            Load();
            Ride();
            Unload();
        }

        _coaster.Terminate(_id);
    }

    private void Load()
    {
        _coaster.StartLoading(_id);      // load() e exclusão do embarque
        _coaster.OpenBoarding(_id);      // autoriza C passageiros
        _coaster.WaitForBoarded(_id);    // espera os C embarques
        _handoff = _coaster.JoinUnloadQueue(_id);
        _coaster.EndLoading(_id);        // próximo carro pode embarcar
    }

    private void Ride()
    {
        _coaster.RunTrack();             // run(): vários carros podem estar na pista
        _coaster.AwaitTurn(_handoff);    // mas desembarcam na ordem de embarque
    }

    private void Unload()
    {
        _coaster.StartUnloading(_id);    // unload()
        _coaster.OpenUnboarding(_id);
        _coaster.WaitForUnboarded(_id);  // carga inteira desembarca
        _coaster.CompleteUnload(_id, _handoff);
    }
}
