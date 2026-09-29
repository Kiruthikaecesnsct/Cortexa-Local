using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Messaging;

public sealed class ExponentialBackoffRetryPolicy : IRetryPolicy
{
    private readonly ServiceBusSettings _settings;

    public ExponentialBackoffRetryPolicy(IOptions<ServiceBusSettings> settings)
    {
        _settings = settings.Value;
    }

    public int MaxRetries => _settings.MaxRetries;

    public bool ShouldRetry(int attempt) => attempt <= _settings.MaxRetries;

    public TimeSpan DelayFor(int attempt)
    {
        var index = Math.Clamp(attempt - 1, 0, _settings.RetryBackoffSeconds.Length - 1);
        return TimeSpan.FromSeconds(_settings.RetryBackoffSeconds[index]);
    }
}
