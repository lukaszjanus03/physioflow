using Microsoft.Extensions.Options;
using PhysioFlow.Infrastructure.Email;

namespace PhysioFlow.Api.Services;

public class AppointmentReminderWorker(IServiceScopeFactory scopes, IOptions<AppointmentReminderOptions> options,
    ILogger<AppointmentReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        // Host najpierw kończy start (w tym seed); procesor nie blokuje uruchamiania API.
        await Task.Yield();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AppointmentReminderProcessor>().ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning("Reminder job failed; error {ErrorType}", ex.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
