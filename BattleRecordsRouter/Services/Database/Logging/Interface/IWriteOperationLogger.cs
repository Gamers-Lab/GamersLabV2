using BattleRecordsRouter.Models;
using Nethereum.Contracts;

namespace BattleRecordsRouter.Services.Database;

public interface IWriteOperationLogger
{
    /// <summary>
    /// Log a write operation intent before sending to blockchain.
    /// Returns the created record, or null if logging failed.
    /// </summary>
    Task<WriteOperationRecord?> LogWriteIntentAsync<TFunctionMessage>(
        string operationName,
        string contractAddress,
        string? walletAddress,
        Action<TFunctionMessage> init,
        object? payload,
        uint? playerIndex = null)
        where TFunctionMessage : FunctionMessage, new();

    /// <summary>
    /// Update an existing write operation record with results
    /// </summary>
    Task UpdateWriteRecordAsync(
        int recordId, 
        string status, 
        string? txHash = null,
        string? errorMessage = null, 
        long? durationMs = null, 
        int? attemptNumber = null, 
        bool? isRetry = null);
}