using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using PhysioFlow.Application.Common.Behaviors;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.ExerciseJournal;
using PhysioFlow.Domain.Entities;
using Xunit;

namespace PhysioFlow.Tests.Unit;

public class TherapistExerciseJournalPrivacyTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public List<string> PropertyNames { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (state is IEnumerable<KeyValuePair<string, object?>> fields)
                PropertyNames.AddRange(fields.Select(f => f.Key));
        }
    }

    [Fact]
    public async Task List_detail_and_summary_log_only_operation_names_never_clinical_response_data()
    {
        var patientId = Guid.NewGuid();
        var entry = new ExerciseJournalEntryDto(Guid.NewGuid(), patientId, Guid.NewGuid(), Guid.NewGuid(),
            1, Guid.NewGuid(), new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc), "PRIVATE_PLAN_NAME",
            new("PRIVATE_EXERCISE_NAME", "PRIVATE_INSTRUCTIONS", 0, 3, 10, null, "PRIVATE_FREQUENCY", "PRIVATE_NOTES"),
            new(2026, 11, 16), ExerciseCompletionStatus.Partial, 2, 5, null, 7,
            "PRIVATE_PATIENT_COMMENT", new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc), null, Guid.NewGuid());
        await AssertOperationOnlyLogs(new ListTherapistExerciseJournalQuery(patientId, new()),
            new PaginatedList<ExerciseJournalEntryDto>([entry], 1, 1, 20));
        await AssertOperationOnlyLogs(new GetTherapistExerciseJournalQuery(patientId, entry.Id), entry);
        await AssertOperationOnlyLogs(new GetTherapistExerciseJournalSummaryQuery(patientId, new()),
            new ExerciseJournalSummaryDto(12345, 23456, 34567, entry.PerformedOn));
    }

    private static async Task AssertOperationOnlyLogs<TRequest, TResponse>(TRequest request, TResponse response)
        where TRequest : notnull, IRequest<TResponse>
    {
        var logger = new CapturingLogger<LoggingBehavior<TRequest, TResponse>>();
        var behavior = new LoggingBehavior<TRequest, TResponse>(logger);
        (await behavior.Handle(request, () => Task.FromResult(response), CancellationToken.None)).Should().Be(response);
        logger.Messages.Should().Equal($"Handling {typeof(TRequest).Name}", $"Handled {typeof(TRequest).Name}");
        // Structured log properties must not carry a request or response hidden from the rendered message.
        logger.PropertyNames.Should().OnlyContain(name => name == "RequestName" || name == "{OriginalFormat}");
    }
}
