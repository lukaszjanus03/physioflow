using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PhysioFlow.Api.Middleware;
using PhysioFlow.Application.Common.Behaviors;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.MedicalRecords;
using Xunit;

namespace PhysioFlow.Tests.Unit;

public class MedicalRecordLoggingTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    [Fact]
    public async Task Request_pipeline_should_log_operation_names_without_documentation_content()
    {
        var logger = new CapturingLogger<LoggingBehavior<CorrectMedicalRecordCommand, bool>>();
        var behavior = new LoggingBehavior<CorrectMedicalRecordCommand, bool>(logger);
        var secret = "SENSITIVE_MEDICAL_CONTENT";
        var command = new CorrectMedicalRecordCommand(Guid.NewGuid(), new CorrectMedicalRecordRequest
        {
            Version = Guid.NewGuid(), Interview = secret, FunctionalAssessment = secret,
            PerformedActions = secret, Recommendations = secret, PatientSummary = secret, CorrectionReason = secret
        });
        await behavior.Handle(command, () => Task.FromResult(true), CancellationToken.None);
        logger.Messages.Should().HaveCount(2).And.OnlyContain(m => m.Contains(nameof(CorrectMedicalRecordCommand)) && !m.Contains(secret));
    }

    [Fact]
    public async Task Validation_failure_logging_and_problem_response_should_omit_attempted_clinical_text()
    {
        var secret = "SENSITIVE_MEDICAL_CONTENT";
        var request = new MedicalRecordContent { Interview = string.Concat(Enumerable.Repeat(secret, 1000)) };
        var validation = await new MedicalRecordContentValidator().ValidateAsync(request);
        var logger = new CapturingLogger<ExceptionHandlingMiddleware>();
        var middleware = new ExceptionHandlingMiddleware(_ => throw new FluentValidation.ValidationException(validation.Errors), logger);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);
        context.Response.StatusCode.Should().Be(400);
        context.Response.Body.Position = 0;
        var response = await new StreamReader(context.Response.Body).ReadToEndAsync();
        JsonDocument.Parse(response).RootElement.GetProperty("code").GetString().Should().Be("validation_failed");
        response.Should().NotContain(secret);
        logger.Messages.Should().ContainSingle().Which.Should().NotContain(secret);
    }
}
