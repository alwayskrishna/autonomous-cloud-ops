namespace AutonomousCloudOps.Core;

/// <summary>Bounded retry policy for explicitly transient failures; never retries cancellations.</summary>
public sealed class ResilientOperation
{
    private readonly int _maxAttempts;
    private readonly TimeSpan _delay;

    public ResilientOperation(int maxAttempts = 3, TimeSpan? delay = null)
    {
        if (maxAttempts is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        _delay = delay ?? TimeSpan.FromMilliseconds(50);
        if (_delay < TimeSpan.Zero || _delay > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(delay));
        _maxAttempts = maxAttempts;
    }

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation,
        Func<Exception, bool> isTransient, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(isTransient);
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await operation(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException &&
                attempt < _maxAttempts && isTransient(ex))
            {
                if (_delay > TimeSpan.Zero)
                    await Task.Delay(_delay, cancellationToken);
            }
        }
    }
}
