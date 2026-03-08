namespace BattleRecordsRouter.Helper;

using System.Numerics;
using Microsoft.Extensions.Logging;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.RPC.NonceServices;

/// <summary>
/// Thread-safe shared nonce service for a single account.
/// Tracks nonces in memory to avoid "nonce too low" errors when transactions
/// are sent faster than the chain confirms them.
///
/// Usage:
/// - Initialize once at startup with the account address and RPC client
/// - All requests share the same instance
/// - Nonce is incremented locally after each send (before chain confirmation)
/// - Reset is only needed on startup or after catastrophic errors
/// </summary>
public sealed class SharedNonceService : INonceService
{
    private readonly string _accountAddress;
    private IClient _client;
    private readonly ILogger<SharedNonceService>? _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private BigInteger _currentNonce;
    private bool _initialized;

    public SharedNonceService(
        string accountAddress,
        IClient client,
        ILogger<SharedNonceService>? logger = null)
    {
        _accountAddress = accountAddress ?? throw new ArgumentNullException(nameof(accountAddress));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger;
    }

    public IClient Client
    {
        get => _client;
        set => _client = value;
    }

    /// <summary>
    /// When true, always fetches the latest nonce from chain (defeats the purpose of this service).
    /// We keep this false to use our in-memory tracking.
    /// </summary>
    public bool UseLatestTransactionsOnly { get; set; } = false;

    /// <summary>
    /// Gets the next nonce and increments the internal counter.
    /// Thread-safe - only one caller gets each nonce.
    /// </summary>
    public async Task<HexBigInteger> GetNextNonceAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_initialized)
            {
                await InitializeFromChainAsync().ConfigureAwait(false);
            }

            var nonce = _currentNonce;
            _currentNonce++;

            _logger?.LogDebug(
                "SharedNonceService: Issued nonce {Nonce} for {Account}, next will be {NextNonce}",
                nonce, _accountAddress, _currentNonce);

            return new HexBigInteger(nonce);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Resets the nonce from the chain's pending transaction count.
    /// Call this on startup or after a catastrophic error.
    /// </summary>
    public async Task ResetNonceAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await InitializeFromChainAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Call this if a transaction fails with "nonce too low" to resync with chain.
    /// </summary>
    public async Task ResyncAfterErrorAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var oldNonce = _currentNonce;
            await InitializeFromChainAsync().ConfigureAwait(false);

            _logger?.LogWarning(
                "SharedNonceService: Resynced after error. Old nonce: {OldNonce}, New nonce: {NewNonce}",
                oldNonce, _currentNonce);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task InitializeFromChainAsync()
    {
        var txCount = new Nethereum.RPC.Eth.Transactions.EthGetTransactionCount(_client);
        var result = await txCount.SendRequestAsync(_accountAddress, BlockParameter.CreatePending())
            .ConfigureAwait(false);

        _currentNonce = result.Value;
        _initialized = true;

        _logger?.LogInformation(
            "SharedNonceService: Initialized for {Account} with nonce {Nonce} (from pending)",
            _accountAddress, _currentNonce);
    }
}

