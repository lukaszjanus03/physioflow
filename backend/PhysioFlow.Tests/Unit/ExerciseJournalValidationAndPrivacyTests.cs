using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PhysioFlow.Api.Middleware;
using PhysioFlow.Application.Common.Behaviors;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.ExerciseJournal;
using PhysioFlow.Domain.Entities;
using Xunit;

namespace PhysioFlow.Tests.Unit;

public class ExerciseJournalValidationAndPrivacyTests
{
    private sealed class Clock(DateTime now) : IDateTimeProvider { public DateTime UtcNow => now; }
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    [Theory]
    [InlineData("2026-11-16T22:59:00Z", "2026-11-17", false)]
    [InlineData("2026-11-16T23:00:00Z", "2026-11-17", true)]
    [InlineData("2026-07-16T22:00:00Z", "2026-07-17", true)]
    [InlineData("2026-03-29T00:30:00Z", "2026-03-29", true)]
    [InlineData("2026-10-25T01:30:00Z", "2026-10-25", true)]
    public async Task Today_uses_Warsaw_including_midnight_and_DST(string utc, string performedOn, bool valid)
    {
        var validator = new ExerciseJournalContentValidator(new Clock(DateTime.Parse(utc).ToUniversalTime()));
        var result = await validator.ValidateAsync(new ExerciseJournalContent
        { PerformedOn = DateOnly.Parse(performedOn), Status = ExerciseCompletionStatus.Completed });
        result.IsValid.Should().Be(valid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Nonpositive_doses_are_invalid_even_when_patient_reports_partial_completion(int value)
    {
        var validator = new ExerciseJournalContentValidator(new Clock(new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc)));
        var content = new ExerciseJournalContent { PerformedOn = new(2026, 11, 16), Status = ExerciseCompletionStatus.Partial };
        (await validator.ValidateAsync(content with { ActualSets = value })).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(content with { ActualRepetitions = value })).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(content with { ActualDurationSeconds = value })).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Operation_and_validation_logs_never_contain_patient_comments_or_health_values()
    {
        const string secret = "PRIVATE_JOURNAL_COMMENT";
        var logger = new CapturingLogger<LoggingBehavior<CreateExerciseJournalCommand, bool>>();
        var behavior = new LoggingBehavior<CreateExerciseJournalCommand, bool>(logger);
        var body = new CreateExerciseJournalRequest
        { PerformedOn = new(2026, 11, 16), Status = ExerciseCompletionStatus.Partial, PainLevel = 7, Comment = secret };
        await behavior.Handle(new(body), () => Task.FromResult(true), CancellationToken.None);
        logger.Messages.Should().HaveCount(2).And.OnlyContain(m => m.Contains(nameof(CreateExerciseJournalCommand)) && !m.Contains(secret));
        var validation = await new ExerciseJournalContentValidator(new Clock(new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc)))
            .ValidateAsync(body with { Comment = string.Concat(Enumerable.Repeat(secret, 1000)), PainLevel = 999999 });
        var errors = new CapturingLogger<ExceptionHandlingMiddleware>();
        var middleware = new ExceptionHandlingMiddleware(_ => throw new FluentValidation.ValidationException(validation.Errors), errors);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);
        context.Response.StatusCode.Should().Be(400);
        context.Response.Body.Position = 0;
        var response = await new StreamReader(context.Response.Body).ReadToEndAsync();
        response.Should().NotContain(secret).And.NotContain("999999");
        errors.Messages.Should().ContainSingle().Which.Should().NotContain(secret).And.NotContain("999999");
    }
}
