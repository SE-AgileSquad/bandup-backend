namespace BandUp.IntegrationTests.Infrastructure;

/// <summary>
/// A clock the test moves by hand. It starts at the real current time so tokens minted during
/// the test stay valid for the JWT handler, which reads the real clock.
/// </summary>
public sealed class TestClock : TimeProvider
{
    private readonly object _gate = new();
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public void Advance(TimeSpan by)
    {
        lock (_gate)
        {
            _now += by;
        }
    }
}
