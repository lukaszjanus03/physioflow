using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Infrastructure.Email;
using Resend;
using Xunit;

namespace PhysioFlow.Tests.Unit;

public class ResendEmailServiceTests
{
    private const string Link = "https://app.physioflow.test/confirm-email?userId=abc&token=SECRET_TOKEN";

    private readonly Mock<IResend> _resend = new();
    private readonly CapturingLogger _logger = new();

    private ResendEmailService CreateService() => new(
        _resend.Object,
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Resend:FromAddress"] = "noreply@physioflow.test" })
            .Build(),
        _logger);

    [Fact]
    public async Task Idempotent_email_should_forward_stable_key_and_payload_to_provider()
    {
        const string key = "appointment-reminder/test/schedule";
        _resend.Setup(r => r.EmailSendAsync(key, It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResendResponse<Guid>(Guid.NewGuid(), null!));
        await CreateService().SendIdempotentAsync("a@b.pl", "reminder", "<p>Termin</p>", key);
        _resend.Verify(r => r.EmailSendAsync(key,
            It.Is<EmailMessage>(m => m.HtmlBody == "<p>Termin</p>" && m.Subject == "reminder"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unsuccessful_response_should_throw_and_not_log_link()
    {
        var error = new ResendException(HttpStatusCode.Unauthorized, ErrorType.RestrictedApiKey, "API key is invalid");
        _resend.Setup(r => r.EmailSendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResendResponse<Guid>(error, null!));

        var act = () => CreateService().SendEmailConfirmationAsync("a@b.pl", "Anna", Link);

        await act.Should().ThrowAsync<EmailDeliveryException>();
        _logger.Messages.Should().NotContain(m => m.Contains("SECRET_TOKEN") || m.Contains("confirm-email"));
    }

    [Fact]
    public async Task Thrown_resend_exception_should_be_wrapped()
    {
        _resend.Setup(r => r.EmailSendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResendException(HttpStatusCode.TooManyRequests, ErrorType.RateLimitExceeded, "slow down"));

        var act = () => CreateService().SendEmailConfirmationAsync("a@b.pl", "Anna", Link);

        (await act.Should().ThrowAsync<EmailDeliveryException>())
            .Which.Message.Should().NotContain("SECRET_TOKEN");
    }

    [Fact]
    public async Task Network_failure_should_be_wrapped()
    {
        _resend.Setup(r => r.EmailSendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var act = () => CreateService().SendEmailConfirmationAsync("a@b.pl", "Anna", Link);

        await act.Should().ThrowAsync<EmailDeliveryException>();
    }

    [Fact]
    public async Task Confirmation_email_should_encode_user_name_and_not_log_link()
    {
        EmailMessage? sent = null;
        _resend.Setup(r => r.EmailSendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(new ResendResponse<Guid>(Guid.NewGuid(), null!));

        await CreateService().SendEmailConfirmationAsync("a@b.pl", "<script>x</script>", Link);

        sent!.HtmlBody.Should().NotContain("<script>");
        sent.HtmlBody.Should().Contain("confirm-email");
        _logger.Messages.Should().NotContain(m => m.Contains("SECRET_TOKEN") || m.Contains("confirm-email"));
    }

    private sealed class CapturingLogger : ILogger<ResendEmailService>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (exception is not null) Messages.Add(exception.ToString());
        }
    }
}
