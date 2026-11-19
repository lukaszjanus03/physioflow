using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PhysioFlow.Api.Controllers;
using PhysioFlow.Api.Middleware;
using PhysioFlow.Infrastructure.Identity;
using Xunit;

namespace PhysioFlow.Tests.Unit;

public class TwoFactorPrivacyTests
{
    private sealed class TestLogger : ILogger<ExceptionHandlingMiddleware>
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

    [Theory]
    [InlineData("/api/auth/2fa/confirm")]
    [InlineData("/api/auth/2fa/verify")]
    [InlineData("/api/auth/login")]
    public async Task Logs_and_diagnostic_strings_never_echo_secret_material(string path)
    {
        const string secret = "SECRET_CODE_OR_CHALLENGE";
        new ConfirmTwoFactorRequest(secret).ToString().Should().NotContain(secret);
        new VerifyTwoFactorRequest(secret, secret, secret).ToString().Should().NotContain(secret);
        new ReauthenticateTwoFactorRequest(secret, secret, secret).ToString().Should().NotContain(secret);
        new TwoFactorSetup(secret, secret, DateTimeOffset.UtcNow).ToString().Should().NotContain(secret);
        new TwoFactorChallenge(true, secret, DateTimeOffset.UtcNow).ToString().Should().NotContain(secret);
        new AuthTokens(secret, secret, DateTime.UtcNow, false).ToString().Should().NotContain(secret);
        new RecoveryCodesResponse([secret]).ToString().Should().NotContain(secret);
        var logger = new TestLogger();
        var middleware = new ExceptionHandlingMiddleware(_ => throw new IOException(secret), logger);
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);
        logger.Messages.Should().ContainSingle().Which.Should().NotContain(secret);
        context.Response.Headers.CacheControl.ToString().Should().Be("no-store");
        context.Response.Body.Position = 0;
        (await new StreamReader(context.Response.Body).ReadToEndAsync()).Should().NotContain(secret);
    }
}
