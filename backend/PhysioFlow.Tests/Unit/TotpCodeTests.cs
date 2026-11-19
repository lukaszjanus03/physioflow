using FluentAssertions;
using Xunit;
using PhysioFlow.Infrastructure.Identity;

namespace PhysioFlow.Tests.Unit;

public class TotpCodeTests
{
    // Independent reference vectors from RFC 6238 Appendix B (SHA-1).
    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    [InlineData(20000000000L, "65353130")]
    public void Matches_rfc_sha1_vectors(long timestamp, string expected)
    {
        TotpCode.Generate("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", timestamp / 30, 8).Should().Be(expected);
    }

    [Fact]
    public void Only_adjacent_steps_are_accepted_and_consumed_steps_are_rejected()
    {
        const string secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);
        var step = now.ToUnixTimeSeconds() / 30;
        foreach (var offset in new[] { -1, 0, 1 })
        {
            var code = TotpCode.Generate(secret, step + offset);
            TotpCode.Match(secret, code, now, -1).Should().Be(step + offset);
            TotpCode.Match(secret, code, now, step + offset).Should().BeNull();
        }
        TotpCode.Match(secret, TotpCode.Generate(secret, step - 2), now, -1).Should().BeNull();
        TotpCode.Match(secret, TotpCode.Generate(secret, step + 2), now, -1).Should().BeNull();
        TotpCode.Match(secret, "１２３４５６", now, -1).Should().BeNull();
    }
}
