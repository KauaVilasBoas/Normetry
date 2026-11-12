namespace Normetry.UnitTests;

public class DeliberateFailure
{
    [Xunit.Fact]
    public void Fails_on_purpose() => Xunit.Assert.Fail("CI check for #10");
}
