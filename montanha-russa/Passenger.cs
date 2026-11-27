namespace MontanhaRussa;

/// <summary>
/// Thread de passageiro: pede voltas repetidamente no carro que lhe foi atribuído
/// (atribuição estática: carro = id % m).
/// </summary>
public sealed class Passenger
{
    private readonly RollerCoaster _coaster;
    private readonly int _id;
    private readonly int _car;
    private int _ride;

    public Passenger(RollerCoaster coaster, int id, int car)
    {
        _coaster = coaster;
        _id = id;
        _car = car;
    }

    public void Ride()
    {
        while (true)
        {
            long request = RollerCoaster.RequestTimestamp();

            _coaster.WaitForBoarding(_car);
            if (_coaster.IsTerminated(_car))
                return;

            _coaster.RecordLatency(_id, _ride, request);
            int boardSeq = _coaster.Board(_id, _ride, _car);

            _coaster.WaitForUnboarding(_car);
            _coaster.Unboard(_car, boardSeq);

            _ride++;
        }
    }
}
