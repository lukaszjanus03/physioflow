using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PhysioFlow.Api.Middleware;
using PhysioFlow.Application.Common.Behaviors;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Therapy;
using Xunit;

namespace PhysioFlow.Tests.Unit;

public class TherapyValidationAndPrivacyTests
{
    [Theory]
    [InlineData(1, 1, null)]
    [InlineData(100, 1000, null)]
    [InlineData(1, null, 1)]
    [InlineData(100, null, 86400)]
    public void Complete_parameters_accept_documented_boundaries(int sets, int? repetitions, int? seconds)
    {
        new TherapyPlanItemValidator(complete: true).Validate(new TherapyPlanItemRequest
        {
            ExerciseId = Guid.NewGuid(), Sets = sets, Repetitions = repetitions, DurationSeconds = seconds, Frequency = "Daily"
        }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Draft_allows_missing_parameters_but_activation_requires_exactly_one_dose_and_frequency()
    {
        var draft = new TherapyPlanItemRequest { ExerciseId = Guid.NewGuid() };
        new TherapyPlanItemValidator().Validate(draft).IsValid.Should().BeTrue();
        new TherapyPlanItemValidator(true).Validate(draft).IsValid.Should().BeFalse();
        new TherapyPlanContentValidator().Validate(new TherapyPlanContent()).IsValid.Should().BeTrue();
        new TherapyPlanContentValidator(true).Validate(new TherapyPlanContent()).IsValid.Should().BeFalse();
        new TherapyPlanContentValidator().Validate(new TherapyPlanContent() { Exercises = null! }).IsValid.Should().BeFalse();
        new TherapyPlanContentValidator().Validate(new TherapyPlanContent() { Exercises = [null!] }).IsValid.Should().BeFalse();
        new TherapyPlanContentValidator().Validate(new TherapyPlanContent() { Exercises = Enumerable.Range(0, 101).Select(_ => draft).ToList() }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Text_limits_are_enforced_without_interpreting_markup()
    {
        var exercise = new ExerciseContent { Name = "A", Instructions = "<script>alert('text')</script>", Category = "Back" };
        var validator = new ExerciseContentValidator();
        validator.Validate(exercise).IsValid.Should().BeTrue();
        validator.Validate(exercise with { Name = new string('n', 201) }).IsValid.Should().BeFalse();
        validator.Validate(exercise with { Instructions = new string('i', 10001) }).IsValid.Should().BeFalse();
        validator.Validate(exercise with { Category = new string('c', 101) }).IsValid.Should().BeFalse();
        validator.Validate(exercise with { Notes = new string('n', 2001) }).IsValid.Should().BeFalse();
        var planValidator = new TherapyPlanContentValidator();
        planValidator.Validate(new TherapyPlanContent() { Name = new string('n', 201) }).IsValid.Should().BeFalse();
        planValidator.Validate(new TherapyPlanContent() { Goal = new string('g', 10001) }).IsValid.Should().BeFalse();
        planValidator.Validate(new TherapyPlanContent() { GeneralRecommendations = new string('g', 10001) }).IsValid.Should().BeFalse();
        new TherapyPlanItemValidator().Validate(new TherapyPlanItemRequest() { ExerciseId = Guid.NewGuid(), Frequency = new string('f', 201) }).IsValid.Should().BeFalse();
        new TherapyPlanItemValidator().Validate(new TherapyPlanItemRequest() { ExerciseId = Guid.NewGuid(), Notes = new string('n', 2001) }).IsValid.Should().BeFalse();
    }

    private sealed class Capture<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    [Fact]
    public async Task Pipeline_and_validation_errors_do_not_log_or_echo_plan_text()
    {
        const string secret = "PRIVATE_THERAPY_TEXT";
        var content = new CorrectTherapyPlanRequest
        {
            Name = secret, Goal = new string('x', 10000) + secret, GeneralRecommendations = secret,
            Version = Guid.NewGuid(), CorrectionReason = secret,
            Exercises = [new() { ExerciseId = Guid.NewGuid(), Notes = secret, Frequency = secret }]
        };
        var logger = new Capture<LoggingBehavior<CorrectTherapyPlanCommand, bool>>();
        await new LoggingBehavior<CorrectTherapyPlanCommand, bool>(logger).Handle(
            new(Guid.NewGuid(), content), () => Task.FromResult(true), CancellationToken.None);
        logger.Messages.Should().HaveCount(2).And.OnlyContain(m => !m.Contains(secret));
        var errors = new TherapyPlanContentValidator().Validate(content).Errors;
        var middlewareLogger = new Capture<ExceptionHandlingMiddleware>();
        var middleware = new ExceptionHandlingMiddleware(_ => throw new FluentValidation.ValidationException(errors), middlewareLogger);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);
        context.Response.StatusCode.Should().Be(400);
        context.Response.Body.Position = 0;
        (await new StreamReader(context.Response.Body).ReadToEndAsync()).Should().NotContain(secret);
        middlewareLogger.Messages.Should().ContainSingle().Which.Should().NotContain(secret);
    }
}
