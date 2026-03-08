using System.Numerics;
using System.Reflection;
using System.Text;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.JsonRpc.Client;
using Nethereum.Util;
using BattleRecordsRouter.Repositories;
using BattleRecordsRouter.Services.Database;
using UP.HTTP;

namespace BattleRecordsRouter.Helper;

/// <summary>
/// Handles contract execution with comprehensive error mapping and retry logic.
/// </summary>
public static class ContractExecutionHelper
{
    // -------------------------- Error mapping helpers -----------------------------

    private static readonly Dictionary<Type, string> ErrorMappings = new()
    {
        { typeof(NotFound), "NotFound" },
        { typeof(PlayerAlreadyInMatch), "PlayerAlreadyInMatch" },
        { typeof(MatchFull), "MatchFull" },
        { typeof(MatchFinished), "MatchFinished" },
        { typeof(MatchAlreadyEnded), "MatchAlreadyEnded" },
        { typeof(DuplicatePlayer), "DuplicatePlayer" },
        { typeof(InvalidTime), "InvalidTime" },
        { typeof(IndexOutOfBounds), "IndexOutOfBounds" },
        { typeof(AlreadyExists), "AlreadyExists" },
        { typeof(ZeroAddress), "ZeroAddress" },
        { typeof(EmptyString), "EmptyString" },
        { typeof(TooManyItems), "TooManyItems" },
        { typeof(TooManyPlayers), "TooManyPlayers" },
        { typeof(EmptyArray), "EmptyArray" },
        { typeof(Invalid), "Invalid" }
    };

    private static volatile Dictionary<string, string>? _errorSelectorMapCache;
    private static readonly object _errorMapLock = new();

    // ----------------------- Core execution with error mapping --------------------

    /// <summary>
    /// Executes a blockchain call with comprehensive error handling, logging, and optional failure tolerance.
    /// </summary>
    /// <typeparam name="T">The return type of the blockchain call.</typeparam>
    /// <param name="func">The blockchain function to execute.</param>
    /// <param name="defaultValue">Default value to return if tolerateFailures is true and an error occurs.</param>
    /// <param name="logger">Logger instance for recording events.</param>
    /// <param name="operationName">Name of the operation for logging context.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <param name="loggingService">Optional blockchain logging service for database persistence.</param>
    /// <param name="contractAddress">Contract address related to the operation.</param>
    /// <param name="walletAddress">Wallet address related to the operation.</param>
    /// <param name="tolerateFailures">If true, returns defaultValue on error instead of throwing.</param>
    /// <param name="payload">Optional payload data to include in error messages for debugging.</param>
    /// <returns>The result of the blockchain call, or defaultValue if tolerateFailures is true and an error occurs.</returns>
    public static async Task<T> ExecuteSafeCall<T>(
        Func<CancellationToken, Task<T>> func,
        T defaultValue,
        ILogger logger,
        string operationName,
        CancellationToken cancellationToken,
        IBlockchainLoggingService? loggingService,
        string? contractAddress,
        string? walletAddress,
        bool tolerateFailures,
        object? payload = null)
    {
        try
        {
            return await func(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("{Op}: operation cancelled", operationName);
            if (tolerateFailures) return defaultValue;
            throw;
        }
        catch (SmartContractCustomErrorRevertException customRevert)
        {
            var (errorName, baseMsg) = ProcessCustomError(customRevert, payload, logger, operationName);

            if (loggingService is not null)
            {
                _ = loggingService.LogErrorAsync(
                        operationName,
                        errorName is not null ? "SmartContractCustomError" : "UnhandledCustomError",
                        baseMsg,
                        customRevert.ToString(),
                        contractAddress,
                        walletAddress)
                    .ContinueWith(
                        t => logger.LogWarning(t.Exception, "{Op}: failed to persist custom error", operationName),
                        TaskContinuationOptions.OnlyOnFaulted);
            }

            if (tolerateFailures) return defaultValue;

            throw new SmartContractRevertException(
                errorName ?? "UnknownCustomError",
                customRevert.ExceptionEncodedData,
                contractAddress,
                operationName,
                customRevert);
        }
        catch (RpcResponseException rpcEx)
        {
            var (message, logMessage) = ProcessRpcError(rpcEx, logger, operationName);

            if (loggingService is not null)
            {
                _ = loggingService.LogErrorAsync(
                        operationName,
                        "RpcError",
                        logMessage,
                        rpcEx.ToString(),
                        contractAddress,
                        walletAddress)
                    .ContinueWith(
                        t => logger.LogWarning(t.Exception, "{Op}: failed to persist RPC error", operationName),
                        TaskContinuationOptions.OnlyOnFaulted);
            }

            if (tolerateFailures) return defaultValue;

            var revertReason = TryExtractRevertReason(rpcEx);
            throw new RpcCallFailedException(logMessage, revertReason, contractAddress, operationName, rpcEx);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Op}: unhandled exception", operationName);

            if (loggingService is not null)
            {
                _ = loggingService.LogErrorAsync(
                        operationName,
                        ex.GetType().Name,
                        ex.Message,
                        ex.ToString(),
                        contractAddress,
                        walletAddress)
                    .ContinueWith(
                        t => logger.LogWarning(t.Exception, "{Op}: failed to persist general exception", operationName),
                        TaskContinuationOptions.OnlyOnFaulted);
            }

            if (tolerateFailures) return defaultValue;
            throw;
        }
    }

    private static (string? errorName, string baseMsg) ProcessCustomError(
        SmartContractCustomErrorRevertException customRevert, 
        object? payload, 
        ILogger logger, 
        string operationName)
    {
        string? encodedData = customRevert.ExceptionEncodedData;
        string? errorName = null;

        if (!string.IsNullOrEmpty(encodedData) &&
            encodedData.StartsWith("0x", StringComparison.Ordinal) &&
            encodedData.Length >= 10)
        {
            var selector = encodedData[..10].ToLowerInvariant();
            var map = GetErrorSelectorMap(logger);
            map.TryGetValue(selector, out errorName);
        }

        if (string.IsNullOrEmpty(errorName))
        {
            // Fallback to reflection-based lookup (should rarely be needed if selector map is working)
            logger.LogDebug("{Op}: Falling back to reflection-based error lookup for encoded data: {EncodedData}",
                operationName, encodedData);

            foreach (var kvp in ErrorMappings)
            {
                if (customRevert.IsCustomErrorFor(kvp.Key))
                {
                    errorName = kvp.Value;
                    break;
                }
            }
        }

        var baseMsg = errorName is not null
            ? $"reverted with custom error '{errorName}()'"
            : "reverted with unknown custom error";

        var payloadText = FormatPayload(payload);
        if (!string.IsNullOrEmpty(payloadText)) baseMsg += $" | Payload: {payloadText}";

        if (errorName is not null)
            logger.LogWarning("{Op}: {Msg}", operationName, baseMsg);
        else
            logger.LogWarning("{Op}: {Msg} (raw={Encoded})", operationName, baseMsg, encodedData);

        return (errorName, baseMsg);
    }

    private static (string message, string logMessage) ProcessRpcError(
        RpcResponseException rpcEx, 
        ILogger logger, 
        string operationName)
    {
        var message = rpcEx.Message ?? "RPC error occurred";
        var revertReason = TryExtractRevertReason(rpcEx);
        var logMessage = !string.IsNullOrEmpty(revertReason) ? $"{message} | Revert: {revertReason}" : message;

        logger.LogWarning(rpcEx, "{Op}: RPC error: {Msg}", operationName, logMessage);

        return (message, logMessage);
    }

    private static Dictionary<string, string> GetErrorSelectorMap(ILogger logger)
    {
        var cached = _errorSelectorMapCache;
        if (cached != null) return cached;

        lock (_errorMapLock)
        {
            cached = _errorSelectorMapCache;
            if (cached != null) return cached;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in ErrorMappings)
            {
                try
                {
                    var selector = GetErrorSelector(kvp.Key);
                    map[selector] = kvp.Value;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to compute selector for {ErrorType}", kvp.Key.Name);
                }
            }

            _errorSelectorMapCache = map;
            return map;
        }
    }

    private static string GetErrorSelector(Type errorType)
    {
        var errAttr = errorType.GetCustomAttribute<ErrorAttribute>()
                      ?? throw new InvalidOperationException($"{errorType.Name} is missing [Error] attribute.");

        var paramTypes = errorType
            .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(m => new { Member = m, Param = m.GetCustomAttribute<ParameterAttribute>() })
            .Where(x => x.Param != null)
            .OrderBy(x => x.Param!.Order)
            .Select(x => x.Param!.Type)
            .ToArray();

        var signature = new StringBuilder()
            .Append(errAttr.Name)
            .Append('(')
            .Append(string.Join(",", paramTypes))
            .Append(')')
            .ToString();

        var hash = Sha3Keccack.Current.CalculateHash(Encoding.UTF8.GetBytes(signature));
        return "0x" + Convert.ToHexString(hash.Take(4).ToArray()).ToLowerInvariant();
    }

    // ------------------------ Revert reason decoding helpers ----------------------

    // Standard Solidity error selectors
    private const string ErrorStringSelector = "0x08c379a0";  // Error(string)
    private const string PanicSelector = "0x4e487b71";         // Panic(uint256)

    private static string? TryExtractRevertReason(RpcResponseException ex)
    {
        var data = ex.RpcError?.Data?.ToString();
        if (!string.IsNullOrWhiteSpace(data))
        {
            var hex = ExtractHex(data);
            var reason = TryDecodeRevertData(hex);
            if (!string.IsNullOrEmpty(reason)) return reason;
        }

        var msg = ex.Message ?? string.Empty;
        var i = msg.IndexOf("revert", StringComparison.OrdinalIgnoreCase);
        return i >= 0 ? msg.Substring(i) : null;
    }

    private static string? TryDecodeRevertData(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Length < 10) return null;

        try
        {
            var cleanHex = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
            if (cleanHex.Length < 8) return null;

            var selector = ("0x" + cleanHex[..8]).ToLowerInvariant();

            if (selector == ErrorStringSelector)
            {
                var dataHex = cleanHex[8..];
                var bytes = Convert.FromHexString(dataHex);

                if (bytes.Length >= 64)
                {
                    var length = new BigInteger(bytes.Skip(32).Take(32).Reverse().ToArray());
                    if (length > 0 && length <= bytes.Length - 64)
                    {
                        var stringBytes = bytes.Skip(64).Take((int)length).ToArray();
                        return Encoding.UTF8.GetString(stringBytes);
                    }
                }
            }
            else if (selector == PanicSelector)
            {
                var dataHex = cleanHex[8..];
                if (dataHex.Length >= 64)
                {
                    var panicBytes = Convert.FromHexString(dataHex[..64]);
                    var code = new BigInteger(panicBytes.Reverse().ToArray());
                    return MapPanicCode(code);
                }
            }
        }
        catch
        {
            return $"Raw revert data: {hex}";
        }

        return null;
    }

    private static string MapPanicCode(BigInteger code) =>
        code.ToString() switch
        {
            "1" => "Panic(0x01): Assertion failed",
            "17" => "Panic(0x11): Arithmetic overflow/underflow",
            "18" => "Panic(0x12): Division by zero",
            "33" => "Panic(0x21): Invalid enum value",
            "34" => "Panic(0x22): Invalid storage byte array access",
            "49" => "Panic(0x31): Pop from empty array",
            "50" => "Panic(0x32): Array index out of bounds",
            "65" => "Panic(0x41): Out of memory",
            "81" => "Panic(0x51): Invalid internal function call",
            _ => $"Panic(0x{code:x2}): Unknown panic code"
        };

    private static string? ExtractHex(string jsonLike)
    {
        var idx = jsonLike.IndexOf("0x", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var start = idx;
        var end = jsonLike.Length;

        for (int i = start + 2; i < jsonLike.Length; i++)
        {
            var c = jsonLike[i];
            if (!Uri.IsHexDigit(c))
            {
                end = i;
                break;
            }
        }

        return jsonLike[start..end];
    }

    private static string FormatPayload(object? payload)
    {
        if (payload is null) return string.Empty;
        try
        {
            return System.Text.Json.JsonSerializer.Serialize(payload);
        }
        catch (Exception ex)
        {
            // Log serialization failure but don't throw - this is just for debugging
            try
            {
                return payload.ToString() ?? string.Empty;
            }
            catch
            {
                return $"<Payload serialization failed: {ex.GetType().Name}>";
            }
        }
    }
}
