using BattleRecordsRouter.Models;
using BattleRecordsRouter.Repositories;
using Nethereum.Contracts;

namespace BattleRecordsRouter.Services.Database;

/// <summary>
/// Unified logging service that combines error logging and blockchain write operation logging.
/// Implements the Composite pattern to provide a single interface for all blockchain-related logging needs.
/// </summary>
/// <remarks>
/// This service acts as a facade/wrapper around two specialized logging services:
/// - IErrorLogDBServices: For logging general application errors
/// - IWriteOperationLogger: For logging blockchain transaction lifecycle
///
/// Benefits of this design:
/// - Single dependency injection: Inject IBlockchainLoggingService instead of two separate services
/// - Simplified constructor signatures in services that need both types of logging
/// - Maintains separation of concerns while providing convenience
///
/// Usage:
/// Inject IBlockchainLoggingService in services that perform blockchain operations and need
/// comprehensive logging (e.g., GamersLabStorageService, ContractUtils).
///
/// Alternative:
/// You can still inject IErrorLogDBServices or IWriteOperationLogger individually if you only
/// need one type of logging.
/// </remarks>
public class DatabaseLoggingService : IBlockchainLoggingService
{
    private readonly IErrorLogDBServices _errorLogger;
    private readonly IWriteOperationLogger _writeLogger;

    /// <summary>
    /// Initializes a new instance of the BlockchainLoggingService class.
    /// </summary>
    /// <param name="errorLogger">Service for logging general errors</param>
    /// <param name="writeLogger">Service for logging blockchain write operations</param>
    public DatabaseLoggingService(
        IErrorLogDBServices errorLogger,
        IWriteOperationLogger writeLogger)
    {
        _errorLogger = errorLogger;
        _writeLogger = writeLogger;
    }

    #region IErrorLogDBServices Implementation

    /// <summary>
    /// Logs an error to the database. Delegates to IErrorLogDBServices.
    /// </summary>
    /// <inheritdoc cref="IErrorLogDBServices.LogErrorAsync"/>
    public Task LogErrorAsync(string operationName, string errorType, string errorMessage,
        string? stackTrace = null, string? contractAddress = null, string? walletAddress = null,
        int? httpStatusCode = null, string? requestPath = null, string? userAgent = null, string? clientIp = null)
        => _errorLogger.LogErrorAsync(operationName, errorType, errorMessage, stackTrace, contractAddress, walletAddress, httpStatusCode, requestPath, userAgent, clientIp);

    #endregion

    #region IWriteOperationLogger Implementation

    /// <summary>
    /// Logs the intent to perform a blockchain write operation. Delegates to IWriteOperationLogger.
    /// </summary>
    /// <inheritdoc cref="IWriteOperationLogger.LogWriteIntentAsync"/>
    public Task<WriteOperationRecord?> LogWriteIntentAsync<TFunctionMessage>(
        string operationName, string contractAddress, string? walletAddress,
        Action<TFunctionMessage> init, object? payload, uint? playerIndex = null)
        where TFunctionMessage : FunctionMessage, new()
        => _writeLogger.LogWriteIntentAsync(operationName, contractAddress, walletAddress, init, payload, playerIndex);

    /// <summary>
    /// Updates an existing write operation record with results. Delegates to IWriteOperationLogger.
    /// </summary>
    /// <inheritdoc cref="IWriteOperationLogger.UpdateWriteRecordAsync"/>
    public Task UpdateWriteRecordAsync(int recordId, string status, string? txHash = null,
        string? errorMessage = null, long? durationMs = null, int? attemptNumber = null, bool? isRetry = null)
        => _writeLogger.UpdateWriteRecordAsync(recordId, status, txHash, errorMessage, durationMs, attemptNumber, isRetry);

    #endregion
}