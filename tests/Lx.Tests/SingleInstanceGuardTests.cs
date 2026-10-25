using Lx.Probe.Diagnostics;

namespace Lx.Tests;

public class SingleInstanceGuardTests
{
    [Fact]
    public void SecondInstance_SignalsFirst()
    {
        string name = @"Local\LxTest-" + Guid.NewGuid().ToString("N");

        using var first = new SingleInstanceGuard(name);
        using var second = new SingleInstanceGuard(name);

        Assert.True(first.IsFirstInstance);
        Assert.False(second.IsFirstInstance);
        Assert.True(second.SignalExisting());
        Assert.True(first.WaitForActivation(TimeSpan.FromSeconds(2)));
    }
}
