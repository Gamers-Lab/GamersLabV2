namespace BattleRecordsRouter.Helper;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

/// <summary>Thrown when lock acquisition fails fast.</summary>
public sealed class BusyException : Exception
{
    public BusyException(string message) : base(message)
    {
    }
}

/// <summary>Options for the send lock.</summary>
public sealed class AccountLockOptions
{
    /// <summary>
    /// Lock acquisition timeout. If ≤ 0, waits indefinitely using only the caller's CancellationToken.
    /// Note: When using default (TimeSpan.Zero), lock acquisition time counts against the overall operation timeout.
    /// </summary>
    public TimeSpan AcquireTimeout { get; init; } = TimeSpan.Zero; // default: no internal timeout

    /// <summary>
    /// If ≤ 0, queue is unbounded. If &gt; 0, fail fast when more than MaxWaiters are already queued.
    /// </summary>
    public int MaxWaiters { get; init; } = 0; // default: unbounded queue
}

/// <summary>Disposable wrapper that releases the semaphore.</summary>
public struct LockReleaser : IAsyncDisposable
{
    private readonly SemaphoreSlim? _gate;
    private bool _disposed;

    public LockReleaser(SemaphoreSlim? gate)
    {
        _gate = gate;
        _disposed = false;
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed && _gate != null)
        {
            _gate.Release();
            _disposed = true;
        }
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// GLOBAL send lock for blockchain transactions (single-account minter pattern).
/// This is a singleton that serializes all blockchain write operations to prevent nonce conflicts.
/// Uses a single global semaphore regardless of account address (address parameter kept for API compatibility).
///
/// Design Notes:
/// - Static fields ensure all instances share the same lock (intentional singleton behavior)
/// - Lock is held for the entire send operation including retries to prevent nonce desync
/// - Dispose() releases the static semaphore and should ONLY be called on application shutdown
/// - Creating multiple instances is safe, but calling Dispose() on any instance affects all users of the lock
/// </summary>
public sealed class AccountSendLock : IDisposable
{
    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static int _waiting;
    private readonly ILogger<AccountSendLock>? _logger;

    /// <summary>
    /// Creates a new AccountSendLock instance. Note: All instances share the same underlying global lock.
    /// </summary>
    /// <param name="logger">Optional logger for lock acquisition metrics and diagnostics</param>
    public AccountSendLock(ILogger<AccountSendLock>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Acquires the global send lock, optionally with timeout and queue depth limits.
    /// The lock is held until the returned LockReleaser is disposed (use with 'await using').
    /// </summary>
    /// <param name="accountAddress">Account address (kept for API compatibility, currently ignored as this is a global lock)</param>
    /// <param name="options">Lock acquisition options (timeout, max waiters)</param>
    /// <param name="ct">Cancellation token for the lock acquisition</param>
    /// <returns>A LockReleaser that must be disposed to release the lock</returns>
    /// <exception cref="BusyException">Thrown when MaxWaiters limit is exceeded or timeout occurs</exception>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested</exception>
    public async Task<LockReleaser> AcquireAsync(
        string accountAddress, // kept for API compatibility (ignored)
        AccountLockOptions? options = null,
        CancellationToken ct = default)
    {
        var opt = options ?? new AccountLockOptions();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Check bounded queue BEFORE incrementing to avoid race condition
        if (opt.MaxWaiters > 0)
        {
            var currentWaiting = Volatile.Read(ref _waiting);
            if (currentWaiting >= opt.MaxWaiters)
            {
                _logger?.LogWarning(
                    "Send lock rejected: queue full. Current waiters: {Waiters}, max allowed: {MaxWaiters}",
                    currentWaiting, opt.MaxWaiters);
                throw new BusyException(
                    $"Too many pending sends (global). Current queue depth: {currentWaiting}, max allowed: {opt.MaxWaiters}");
            }
        }

        // Now increment the counter
        var cur = Interlocked.Increment(ref _waiting);

        try
        {
            // If AcquireTimeout <= 0, wait indefinitely using only the caller's CancellationToken
            // Note: This means lock acquisition time counts against the overall operation timeout
            if (opt.AcquireTimeout <= TimeSpan.Zero)
            {
                await _gate.WaitAsync(ct).ConfigureAwait(false);
            }
            else
            {
                if (!await _gate.WaitAsync(opt.AcquireTimeout, ct).ConfigureAwait(false))
                {
                    _logger?.LogWarning(
                        "Send lock acquisition timed out after {Timeout}ms. Waiters: {Waiters}",
                        opt.AcquireTimeout.TotalMilliseconds, cur);
                    throw new BusyException($"Timed out acquiring global send slot after {opt.AcquireTimeout.TotalMilliseconds}ms.");
                }
            }

            sw.Stop();

            // Log slow lock acquisitions (> 1 second indicates contention)
            if (sw.ElapsedMilliseconds > 1000)
            {
                _logger?.LogWarning(
                    "Send lock acquired after {Ms}ms (slow). Queue depth: {Waiters}/100",
                    sw.ElapsedMilliseconds, cur);
            }
            else
            {
                _logger?.LogInformation(
                    "Send lock acquired in {Ms}ms. Queue depth: {Waiters}/100",
                    sw.ElapsedMilliseconds, cur);
            }

            return new LockReleaser(_gate);
        }
        finally
        {
            Interlocked.Decrement(ref _waiting);
        }
    }

    /// <summary>
    /// Disposes the semaphore. Note: This should only be called on application shutdown
    /// as this is a singleton lock shared across all instances.
    /// </summary>
    public void Dispose()
    {
        _gate?.Dispose();
    }
}