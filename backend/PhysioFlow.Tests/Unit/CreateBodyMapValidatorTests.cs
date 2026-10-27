using FluentAssertions;
using PhysioFlow.Application.Features.BodyMapping.Commands.CreateBodyMap;
using PhysioFlow.Domain.Enums;
using Xunit;

namespace PhysioFlow.Tests.Unit;

public class CreateBodyMapValidatorTests
{
    private readonly CreateBodyMapCommandValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(10)]
    public void Should_accept_valid_pain_scale(int? painScale)
    {
        var command = CreateCommand(painScale);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(11)]
    public void Should_reject_invalid_pain_scale(int painScale)
    {
        var command = CreateCommand(painScale);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == "PainPoints[0].PainScale");
    }

    private static CreateBodyMapCommand CreateCommand(int? painScale)
    {
        return new CreateBodyMapCommand(
            PatientId: Guid.NewGuid(),
            AppointmentId: null,
            Notes: null,
            PainPoints: new[]
            {
                new PainPointInput(
                    Side: BodySide.Front,
                    X: 0.5m,
                    Y: 0.5m,
                    Severity: PainPointSeverity.AcutePain,
                    Note: null,
                    PainScale: painScale)
            });
    }
}