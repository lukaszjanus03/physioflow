using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PhysioFlow.Api.Middleware;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Chat;
using PhysioFlow.Domain.Entities;
using Xunit;

namespace PhysioFlow.Tests.Unit;

public class ChatPrivacyTests
{
    private sealed class Logger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            exception.Should().BeNull();
            Messages.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task Diagnostic_strings_and_unexpected_error_logs_never_include_bodies_or_tokens()
    {
        const string secret = "PRIVATE_BODY_OR_TOKEN";
        var body = new SendChatRequest(Guid.NewGuid(), secret);
        body.ToString().Should().NotContain(secret);
        var m = new PrivateChatMessage { Content = secret };
        m.ToString().Should().NotContain(secret);
        ChatService.ToDto(m).ToString().Should().NotContain(secret);
        var logger = new Logger<ExceptionHandlingMiddleware>();
        var middleware = new ExceptionHandlingMiddleware(_ => throw new IOException(secret), logger);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/chat/conversations";
        context.Request.QueryString = new QueryString("?access_token=" + secret);
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);
        context.Response.StatusCode.Should().Be(500);
        logger.Messages.Should().ContainSingle().Which.Should().NotContain(secret);
        context.Response.Body.Position = 0;
        (await new StreamReader(context.Response.Body).ReadToEndAsync()).Should().NotContain(secret);
    }

    [Fact]
    public void Invalid_message_errors_never_echo_the_input()
    {
        var input = string.Concat(Enumerable.Repeat("PRIVATE_BODY", 500));
        var act = () => ChatValidation.Content(new(Guid.NewGuid(), input));
        act.Should().Throw<FluentValidation.ValidationException>().Which.Message.Should().NotContain("PRIVATE_BODY");
    }
}
