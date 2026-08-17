namespace QServe.Services;

/// <summary>
/// REC-6: runs RecomputeRecentOrderedAsync on a schedule. Uses IServiceScopeFactory rather
/// than injecting IRecommendationService/ApplicationDbContext directly, since this service
/// itself is a singleton (BackgroundService lifetime) but DbContext is scoped — this creates
/// a fresh scope for each run rather than holding one DbContext open for the app's lifetime.
/// </summary>
public class RecentOrderedRecalculationService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecentOrderedRecalculationService> _logger;

    public RecentOrderedRecalculationService(IServiceScopeFactory scopeFactory, ILogger<RecentOrderedRecalculationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var recommendationService = scope.ServiceProvider.GetRequiredService<IRecommendationService>();
                await recommendationService.RecomputeRecentOrderedAsync();
                _logger.LogInformation("RecentOrdered recomputed for all menu items.");
            }
            catch (Exception ex)
            {
                // Deliberately swallow-and-log rather than let an exception kill the whole
                // background loop — a failed recompute should try again on the next interval,
                // not take the app down or silently stop running forever.
                _logger.LogError(ex, "Failed to recompute RecentOrdered.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Expected on shutdown — exit the loop quietly.
            }
        }
    }
}
