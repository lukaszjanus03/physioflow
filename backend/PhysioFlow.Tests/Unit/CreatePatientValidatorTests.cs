using FluentAssertions;
using PhysioFlow.Application.Features.Patients.Commands.CreatePatient;
using Xunit;

namespace PhysioFlow.Tests.Unit;

public class CreatePatientValidatorTests
{
    private readonly CreatePatientCommandValidator _validator = new();

    [Fact]
    public void Should_reject_empty_first_name()
    {
        var command = new CreatePatientCommand("", "Kowalski", null, null, null, null, null, null, null, null);
        var result = _validator.Validate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreatePatientCommand.FirstName));
    }

    [Fact]
    public void Should_reject_invalid_pesel()
    {
        var command = new CreatePatientCommand("Jan", "Kowalski", "12345", null, null, null, null, null, null, null);
        var result = _validator.Validate(command);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Should_accept_valid_patient()
    {
        var command = new CreatePatientCommand("Jan", "Kowalski", "12345678901", null, "jan@example.com", null, null, null, null, null);
        var result = _validator.Validate(command);
        result.IsValid.Should().BeTrue();
    }
}
