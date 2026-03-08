using System.Numerics;
using System.Diagnostics;
using Nethereum.Contracts;
using BattleRecordsRouter.Repositories;
using BattleRecordsRouter.Services.Database;
using Nethereum.RPC.Eth.DTOs;
using System.Runtime.CompilerServices;
using BattleRecordsRouter.Helper;
using BattleRecordsRouter.Models;
using UP.HTTP;
using Web3 = Nethereum.Web3.Web3;

public static class ContractUtils
{
    private static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultSendTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DefaultReceiptTimeout = TimeSpan.FromMinutes(2);

    // ------------------------- Fast-path logs (low alloc) -------------------------

    private static readonly Action<ILogger, string, long, Exception?> _logReadOk =
        LoggerMessage.Define<string, long>(LogLevel.Debug, new EventId(1001, "ReadOk"),
            "{Op} read ok in {Ms} ms");

    private static readonly Action<ILogger, string, string, long, Exception?> _logSentTx =
        LoggerMessage.Define<string, string, long>(LogLevel.Information, new EventId(2001, "SentTx"),
            "{Op} sent tx {Tx} in {Ms} ms");

    private static readonly Action<ILogger, string, string, long, BigInteger?, BigInteger?, Exception?> _logMinedOk =
        LoggerMessage.Define<string, string, long, BigInteger?, BigInteger?>(LogLevel.Information,
            new EventId(2002, "MinedOk"),
            "{Op}: mined {Tx} in {Ms} ms at block {Block} (gasUsed {GasUsed})");

    private static readonly Action<ILogger, string, string, long, BigInteger?, Exception?> _logMinedFail =
        LoggerMessage.Define<string, string, long, BigInteger?>(LogLevel.Warning,
            new EventId(2003, "MinedFail"),
            "{Op}: {Tx} mined in {Ms} ms with failing status {Status}");

    private static readonly Action<ILogger, string, string, long, Exception?> _logReceiptTimeout =
        LoggerMessage.Define<string, string, long>(LogLevel.Warning, new EventId(2004, "ReceiptTimeout"),
            "{Op}: receipt polling timed out for {Tx} after {Ms} ms");

    private static readonly Action<ILogger, string, Exception?> _logCancelled =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(2005, "Cancelled"),
            "{Op} cancelled by request");

    // ----------------------------- Public API ------------------------------------

    /// READS: call view functions safely, return default on failure/timeout
    public static async Task<T> SafeReadAsync<T>(
        Func<CancellationToken, Task<T>> call,
        T defaultValue,
        ILogger logger,
        [CallerMemberName] string? operationName = null,
        CancellationToken cancellationToken = default,
        IBlockchainLoggingService? loggingService = null,
        string? contractAddress = null,
        string? walletAddress = null,
        bool tolerateFailures = false,
        TimeSpan? timeout = null,
        object? payload = null)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["op"] = operationName, ["contract"] = contractAddress, ["wallet"] = walletAddress
        });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? DefaultReadTimeout);

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await ContractExecutionHelper.ExecuteSafeCall(
                call, defaultValue, logger, operationName!, cts.Token,
                loggingService, contractAddress, walletAddress, tolerateFailures, payload
            ).ConfigureAwait(false);

            _logReadOk(logger, operationName!, sw.ElapsedMilliseconds, null);
            return result;
        }
        finally
        {
            sw.Stop();
        }
    }

    // Wire this up at app start: ContractUtils.SendLock = new AccountSendLock();
    public static AccountSendLock? SendLock { get; set; }

    // Optional default lock behavior (override at startup if desired)
    public static AccountLockOptions DefaultSendLockOptions { get; set; }
        = new AccountLockOptions { AcquireTimeout = TimeSpan.Zero, MaxWaiters = 0 };


    public static async Task<string> SafeWriteSendAsync<TFunctionMessage>(
        Nethereum.Web3.Web3 web3,
        string contractAddress,
        Action<TFunctionMessage> init,
        ILogger logger,
        HttpContext httpContext,
        [CallerMemberName] string? operationName = null,
        CancellationToken cancellationToken = default,
        IBlockchainLoggingService? loggingService = null,
        string? walletAddress = null,
        TimeSpan? sendTimeout = null,
        object? payload = null)
        where TFunctionMessage : FunctionMessage, new()
    {
        // Always lock on the actual sending account from web3
        var keyAddress = web3.TransactionManager?.Account?.Address;

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["op"] = operationName,
            ["contract"] = contractAddress,
            ["wallet"] = keyAddress
        });

        // Extract playerIndex from HttpContext when logging
        uint? playerIndex = null;
        if (httpContext != null)
        {
            playerIndex = SiweJwtMiddleware.GetPlayerIndex(httpContext);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(sendTimeout ?? DefaultSendTimeout);

        var sw = Stopwatch.StartNew();

        // Log write intent
        WriteOperationRecord? writeRecord = null;
        if (loggingService != null)
        {
            try
            {
                logger.LogInformation("{Op}: Attempting to log write intent to database", operationName);
                writeRecord = await loggingService.LogWriteIntentAsync<TFunctionMessage>(
                    operationName!, contractAddress, keyAddress, init, payload, playerIndex);
                logger.LogInformation("{Op}: Successfully logged write intent with ID {Id}", operationName, writeRecord?.Id);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Op}: failed to log write intent", operationName);
            }
        }
        else
        {
            logger.LogWarning("{Op}: loggingService is null, skipping write intent logging", operationName);
        }

        // Build the FunctionMessage & handler (DRY at call sites)
        var fn = new TFunctionMessage();
        init?.Invoke(fn);
        var handler = web3.Eth.GetContractTransactionHandler<TFunctionMessage>();

        // Make Nethereum send cancelable with WaitAsync(ct)
        Task<string> GuardedSend(CancellationToken ct)
            => handler.SendRequestAsync(contractAddress, fn).WaitAsync(ct);

        var useLock = SendLock != null && !string.IsNullOrWhiteSpace(keyAddress);
        IAsyncDisposable? releaser = null;

        try
        {
            if (useLock)
            {
                // Use caller-tunable defaults set on ContractUtils.DefaultSendLockOptions
                releaser = await SendLock!
                    .AcquireAsync(keyAddress!, DefaultSendLockOptions, cts.Token)
                    .ConfigureAwait(false);
            }

            // Try send (attempt 0) + single retry (attempt 1) for nonce desync
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var txHash = await ContractExecutionHelper.ExecuteSafeCall(
                        GuardedSend, string.Empty, logger, operationName!, cts.Token,
                        loggingService, contractAddress, keyAddress, tolerateFailures: false, payload
                    ).ConfigureAwait(false);

                    if (attempt == 1 && loggingService is not null)
                    {
                        // we had a nonce race but recovered
                        _ = loggingService.LogErrorAsync(
                                operationName!,
                                "NonceDesyncRecovered",
                                "Nonce race detected and recovered via retry.",
                                $"Recovered on retry. Payload={FormatPayloadForMetrics(payload)}",
                                contractAddress,
                                keyAddress)
                            .ContinueWith(
                                t => logger.LogWarning(t.Exception, "{Op}: failed to persist NonceDesyncRecovered",
                                    operationName),
                                TaskContinuationOptions.OnlyOnFaulted);
                    }

                    // Update write record with success
                    if (writeRecord != null && loggingService != null)
                    {
                        _ = Task.Run(async () => await loggingService.UpdateWriteRecordAsync(
                            writeRecord.Id, "sent", txHash, null, sw.ElapsedMilliseconds, attempt + 1, attempt > 0));
                    }

                    _logSentTx(logger, operationName!, txHash, sw.ElapsedMilliseconds, null);
                    return txHash;
                }
                catch (RpcCallFailedException ex) when (attempt == 0 &&
                                                        LooksLikeNonceDesync(ex.Message, ex.RevertReason))
                {
                    logger.LogWarning("{Op}: nonce race detected (mapped) — retrying once.", operationName);
                    await BackoffAsync(cts.Token).ConfigureAwait(false);
                    continue;
                }
                catch (Nethereum.JsonRpc.Client.RpcResponseException ex) when (attempt == 0 &&
                                                                               LooksLikeNonceDesync(ex.Message,
                                                                                   ex.RpcError?.Message))
                {
                    logger.LogWarning("{Op}: nonce race detected (raw RpcResponseException) — retrying once.",
                        operationName);
                    await BackoffAsync(cts.Token).ConfigureAwait(false);
                    continue;
                }
                catch (Exception ex) when (attempt == 0 && LooksLikeNonceDesync(FlattenMessages(ex), null))
                {
                    logger.LogWarning("{Op}: nonce race detected (generic) — retrying once.", operationName);
                    await BackoffAsync(cts.Token).ConfigureAwait(false);
                    continue;
                }
                catch (Exception ex) when (attempt == 1 && LooksLikeNonceDesync(FlattenMessages(ex), null))
                {
                    // Second attempt still hit a nonce error: record as failed metric before rethrow
                    if (loggingService is not null)
                    {
                        _ = loggingService.LogErrorAsync(
                                operationName!,
                                "NonceDesyncFailed",
                                "Nonce race persisted after retry.",
                                ex.ToString(),
                                contractAddress,
                                keyAddress)
                            .ContinueWith(
                                t => logger.LogWarning(t.Exception, "{Op}: failed to persist NonceDesyncFailed",
                                    operationName),
                                TaskContinuationOptions.OnlyOnFaulted);
                    }

                    throw;
                }
            }

            throw new InvalidOperationException("Unexpected send loop exit without return.");
        }
        catch (Exception ex)
        {
            // Update write record with failure
            if (writeRecord != null && loggingService != null)
            {
                _ = Task.Run(async () => await loggingService.UpdateWriteRecordAsync(
                    writeRecord.Id, "failed", null, ex.Message, sw.ElapsedMilliseconds));
            }

            throw;
        }
        finally
        {
            if (releaser is not null)
                await releaser.DisposeAsync().ConfigureAwait(false);

            sw.Stop();
        }

        // --- helpers (local) ---
        static async Task BackoffAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(75, ct).ConfigureAwait(false);
            }
            catch
            {
                /* ignore */
            }
        }

        static string FlattenMessages(Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            var cur = ex;
            while (cur != null)
            {
                sb.Append(cur.Message).Append(" | ");
                cur = cur is AggregateException agg && agg.InnerExceptions.Count > 0
                    ? agg.InnerExceptions[0]
                    : cur.InnerException;
            }

            return sb.ToString();
        }

        static string FormatPayloadForMetrics(object? p)
        {
            if (p is null) return string.Empty;
            try
            {
                return System.Text.Json.JsonSerializer.Serialize(p);
            }
            catch
            {
                return p.ToString() ?? string.Empty;
            }
        }
    }


    public static async Task<(string txHash, TransactionReceipt? receipt)> SafeWriteSendAndWaitAsync<TFunctionMessage>(
        Web3 web3,
        string contractAddress,
        Action<TFunctionMessage> init,
        ILogger logger,
        HttpContext httpContext,
        [CallerMemberName] string? operationName = null,
        CancellationToken cancellationToken = default,
        IBlockchainLoggingService? loggingService = null,
        string? walletAddress = null,
        TimeSpan? sendTimeout = null,
        TimeSpan? receiptTimeout = null,
        object? payload = null,
        bool throwOnFailedReceipt = false,
        int? receiptPollMs = null) // Add this
        where TFunctionMessage : FunctionMessage, new()
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["op"] = operationName, ["contract"] = contractAddress, ["wallet"] = walletAddress
        });

        // up to 1 retry after a nonce desync reset
        var attempt = 0;

        while (true)
        {
            try
            {
                // 1) Send the tx (DRY through generic SafeWriteSendAsync)
                var txHash = await SafeWriteSendAsync(
                    web3, contractAddress, init, logger, httpContext, operationName,
                    cancellationToken, loggingService, walletAddress, sendTimeout, payload);

                // 2) Wait for the receipt
                var receipt = await WaitForReceiptAsync(
                    web3, txHash, logger, operationName!, receiptTimeout, cancellationToken, receiptPollMs);

                if (string.IsNullOrEmpty(txHash) || receipt is null)
                    return (txHash, receipt);

                if (receipt.Status?.Value != 1 && throwOnFailedReceipt)
                {
                    throw new RpcCallFailedException(
                        $"Transaction {txHash} failed with status {receipt.Status?.Value}",
                        contractAddress: contractAddress, operation: operationName);
                }

                return (txHash, receipt);
            }
            catch (RpcCallFailedException ex) when (attempt == 0 && LooksLikeNonceDesync(ex.Message, ex.RevertReason))
            {
                attempt = 1;

                var ns = web3?.TransactionManager?.Account?.NonceService;
                if (ns != null)
                {
                    logger.LogWarning(
                        "{Op}: nonce sync issue detected (\"{Msg}\"). Resetting cached nonce and retrying once.",
                        operationName, ex.Message ?? ex.RevertReason);

                    try
                    {
                        await ns.ResetNonceAsync().ConfigureAwait(false);
                    }
                    catch (Exception resetEx)
                    {
                        logger.LogWarning(resetEx, "{Op}: failed to reset nonce; will rethrow original error.",
                            operationName);
                        throw;
                    }

                    continue; // retry once
                }

                throw; // no NonceService
            }
        }
    }


    public static async Task<TransactionReceipt?> WaitForReceiptAsync(
        Web3 web3,
        string txHash,
        ILogger logger,
        string operationName,
        TimeSpan? overallTimeout = null,
        CancellationToken cancellationToken = default,
        int? pollMs = null)
    {
        if (string.IsNullOrWhiteSpace(txHash))
            throw new ArgumentException("txHash cannot be null or empty.", nameof(txHash));

        var timeout = overallTimeout ?? DefaultReceiptTimeout;

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["op"] = operationName
        });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        var sw = Stopwatch.StartNew();
        try
        {
            var receipt = await ContractExecutionHelper.ExecuteSafeCall(
                async ct =>
                {
                    var svc = web3.TransactionManager.TransactionReceiptService;
                    var originalPoll = svc.GetPollingRetryIntervalInMilliseconds();
                    if (pollMs.HasValue) svc.SetPollingRetryIntervalInMilliseconds(pollMs.Value);

                    try
                    {
                        return await svc.PollForReceiptAsync(txHash, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (pollMs.HasValue) svc.SetPollingRetryIntervalInMilliseconds(originalPoll);
                    }
                },
                defaultValue: null,
                logger,
                operationName,
                cts.Token,
                loggingService: null,
                contractAddress: null,
                walletAddress: null,
                tolerateFailures: true
            ).ConfigureAwait(false);

            if (receipt == null)
            {
                _logReceiptTimeout(logger, operationName, txHash, sw.ElapsedMilliseconds, null);
                return null;
            }

            var status = receipt.Status?.Value;
            if (status == 1)
            {
                _logMinedOk(logger, operationName, txHash, sw.ElapsedMilliseconds,
                    receipt.BlockNumber?.Value, receipt.GasUsed?.Value, null);
            }
            else
            {
                _logMinedFail(logger, operationName, txHash, sw.ElapsedMilliseconds, status, null);
            }

            return receipt;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logReceiptTimeout(logger, operationName, txHash, sw.ElapsedMilliseconds, null);
            return null;
        }
        finally
        {
            sw.Stop();
        }
    }

    private static bool LooksLikeNonceDesync(string? msg, string? revert = null)
    {
        var s = ((msg ?? string.Empty) + " " + (revert ?? string.Empty)).ToLowerInvariant();
        return s.Contains("nonce too low")
               || s.Contains("already known")
               || s.Contains("known transaction")
               || s.Contains("replacement transaction underpriced")
               || s.Contains("nonce has already been used");
    }
}


// ========================= Domain exceptions =========================

public class ContractCallFailedException : Exception
{
    public string? ErrorCode { get; }
    public string? EncodedData { get; }
    public string? ContractAddress { get; }
    public string? Operation { get; }

    public ContractCallFailedException(
        string message,
        string? errorCode = null,
        string? encodedData = null,
        string? contractAddress = null,
        string? operation = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        EncodedData = encodedData;
        ContractAddress = contractAddress;
        Operation = operation;
    }
}

public class SmartContractRevertException : ContractCallFailedException
{
    public SmartContractRevertException(
        string errorName,
        string? encodedData = null,
        string? contractAddress = null,
        string? operation = null,
        Exception? innerException = null)
        : base($"Smart contract reverted with error '{errorName}()'",
            errorName, encodedData, contractAddress, operation, innerException)
    {
    }
}

public class RpcCallFailedException : ContractCallFailedException
{
    public string? RevertReason { get; }

    public RpcCallFailedException(
        string message,
        string? revertReason = null,
        string? contractAddress = null,
        string? operation = null,
        Exception? innerException = null)
        : base(message, "RpcError", null, contractAddress, operation, innerException)
    {
        RevertReason = revertReason;
    }
}