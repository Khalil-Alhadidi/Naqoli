using Wallet.MockApi.Services;

namespace Agent.Core.Tests.Wallet;

public class PinHasherTests
{
    [Fact]
    public void VerifyAcceptsTheRightPin()
        => Assert.True(PinHasher.Verify("4321", PinHasher.Hash("4321")));

    [Fact]
    public void VerifyRejectsTheWrongPin()
        => Assert.False(PinHasher.Verify("4322", PinHasher.Hash("4321")));

    [Fact]
    public void HashIsSaltedSoTwoHashesDiffer()
        => Assert.NotEqual(PinHasher.Hash("4321"), PinHasher.Hash("4321"));
}
