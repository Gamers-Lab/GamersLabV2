using System.Reflection;
using BattleRecordsRouter.Models;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Supabase;

namespace BattleRecordsRouter.Services.Database;

/// <summary>
/// Service for logging blockchain write operations to Supabase database.
/// Tracks the full lifecycle of blockchain transactions from intent to completion.
/// </summary>
/// <remarks>
/// This service implements a two-phase logging pattern:
/// 1. LogWriteIntentAsync - Called BEFORE sending transaction to blockchain (status: "pending")
/// 2. UpdateWriteRecordAsync - Called AFTER transaction completes (status: "sent", "failed", etc.)
///
/// Logged Information:
/// - Operation metadata (name, contract address, wallet address, player index)
/// - Function details (name, parameters extracted from Nethereum FunctionMessage)
/// - Request payload (serialized to JSON)
/// - Transaction results (hash, status, duration, error messages)
/// - Retry tracking (attempt number, isRetry flag)
///
/// Use Cases:
/// - Debugging failed blockchain transactions
/// - Performance monitoring (transaction duration)
/// - Audit trail for compliance
/// - Retry analysis and optimization
///
/// Thread Safety: Safe for concurrent use via Supabase client and ILogger.
/// </remarks>
public class WriteOperationLoggerToDB : IWriteOperationLogger
{
    private readonly Client _supabase;
    private readonly ILogger<WriteOperationLoggerToDB> _logger;

    /// <summary>
    /// Initializes a new instance of the WriteOperationLogger class.
    /// </summary>
    /// <param name="supabase">Supabase client for database operations</param>
    /// <param name="logger">Logger for diagnostic output</param>
    public WriteOperationLoggerToDB(Client supabase, ILogger<WriteOperationLoggerToDB> logger)
    {
        _supabase = supabase;
        _logger = logger;
    }

    /// <summary>
    /// Logs the intent to perform a blockchain write operation BEFORE sending the transaction.
    /// Creates a new record in the database with status "pending".
    /// </summary>
    /// <typeparam name="TFunctionMessage">Nethereum FunctionMessage type representing the smart contract function</typeparam>
    /// <param name="operationName">Name of the operation (e.g., "SetRecord", "CreatePlayer")</param>
    /// <param name="contractAddress">Ethereum contract address being called</param>
    /// <param name="walletAddress">Wallet address sending the transaction (optional)</param>
    /// <param name="init">Action to initialize the FunctionMessage with parameters</param>
    /// <param name="payload">Additional payload data to log (will be JSON serialized)</param>
    /// <param name="playerIndex">Player index associated with this operation (optional)</param>
    /// <returns>
    /// The created WriteOperationRecord with database-assigned ID, or null if logging failed.
    /// The returned record's ID should be passed to UpdateWriteRecordAsync after transaction completes.
    /// </returns>
    /// <remarks>
    /// This method extracts function parameters using reflection on [Parameter] attributes from Nethereum.
    /// If database insert fails, returns null and logs error - does not throw exceptions.
    /// </remarks>
    public async Task<WriteOperationRecord?> LogWriteIntentAsync<TFunctionMessage>(
        string operationName,
        string contractAddress,
        string? walletAddress,
        Action<TFunctionMessage> init,
        object? payload,
        uint? playerIndex = null)
        where TFunctionMessage : FunctionMessage, new()
    {
        _logger.LogInformation("LogWriteIntentAsync called: {OperationName}", operationName);

        try
        {
            var fn = new TFunctionMessage();
            init?.Invoke(fn);

            var record = new WriteOperationRecord
            {
                OperationName = operationName,
                ContractAddress = contractAddress,
                WalletAddress = walletAddress,
                PlayerIndex = playerIndex,
                FunctionName = typeof(TFunctionMessage).Name,
                FunctionParameters = SerializeFunction(fn),
                Payload = SerializePayload(payload),
                Status = "pending",
                AttemptNumber = 1,
                IsRetry = false,
                CreatedAt = DateTime.UtcNow
            };

            _logger.LogInformation("About to insert record for operation: {OperationName}, Function: {FunctionName}",
                operationName, typeof(TFunctionMessage).Name);

            var response = await _supabase
                .From<WriteOperationRecord>()
                .Insert(record);

            _logger.LogInformation("Insert response: Models count = {Count}, ResponseMessage = {Message}",
                response.Models?.Count, response.ResponseMessage);

            if (response.Models == null || response.Models.Count == 0)
            {
                _logger.LogError("Failed to insert write operation record for {OperationName}: No models returned from Supabase. ResponseMessage: {ResponseMessage}",
                    operationName, response.ResponseMessage);
                return null;
            }

            var insertedRecord = response.Models.First();
            _logger.LogInformation("Insert successful, returned record with ID: {Id}", insertedRecord.Id);
            return insertedRecord;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log write intent for operation {OperationName}", operationName);
            return null;
        }
    }

    /// <summary>
    /// Updates an existing write operation record with transaction results AFTER the blockchain operation completes.
    /// </summary>
    /// <param name="recordId">Database ID of the record created by LogWriteIntentAsync (0 = skip update)</param>
    /// <param name="status">Transaction status (e.g., "sent", "failed", "timeout", "confirmed")</param>
    /// <param name="txHash">Blockchain transaction hash (optional)</param>
    /// <param name="errorMessage">Error message if transaction failed (optional)</param>
    /// <param name="durationMs">Transaction duration in milliseconds (optional)</param>
    /// <param name="attemptNumber">Attempt number for retry tracking (optional)</param>
    /// <param name="isRetry">Whether this was a retry attempt (optional)</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method is fire-and-forget safe. If update fails, it logs a warning but does not throw.
    /// If recordId is 0 (indicating LogWriteIntentAsync failed), this method silently returns.
    /// Verifies update success by checking response models and logs warnings if update appears to fail.
    /// </remarks>
    public async Task UpdateWriteRecordAsync(int recordId, string status, string? txHash = null,
        string? errorMessage = null, long? durationMs = null, int? attemptNumber = null, bool? isRetry = null)
    {
        if (recordId == 0) return;

        try
        {
            var existingRecord = await _supabase.From<WriteOperationRecord>()
                .Where(x => x.Id == recordId)
                .Single();

            if (existingRecord == null)
            {
                _logger.LogWarning("Cannot update write record {RecordId}: Record not found in database", recordId);
                return;
            }

            existingRecord.Status = status;
            existingRecord.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrEmpty(txHash))
                existingRecord.TransactionHash = txHash;

            if (!string.IsNullOrEmpty(errorMessage))
                existingRecord.ErrorMessage = errorMessage;

            if (durationMs.HasValue)
                existingRecord.SendDurationMs = durationMs.Value;

            if (attemptNumber.HasValue)
                existingRecord.AttemptNumber = attemptNumber.Value;

            if (isRetry.HasValue)
                existingRecord.IsRetry = isRetry.Value;

            var updateResponse = await _supabase.From<WriteOperationRecord>()
                .Where(x => x.Id == recordId)
                .Update(existingRecord);

            // Verify update succeeded
            if (updateResponse?.Models == null || updateResponse.Models.Count == 0)
            {
                _logger.LogWarning("Update write record {RecordId} may have failed: No models returned from Supabase update. ResponseMessage: {ResponseMessage}",
                    recordId, updateResponse?.ResponseMessage);
            }
            else
            {
                _logger.LogDebug("Successfully updated write record {RecordId} with status {Status}", recordId, status);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update write record {RecordId}", recordId);
        }
    }

    /// <summary>
    /// Serializes a Nethereum FunctionMessage to JSON by extracting properties marked with [Parameter] attribute.
    /// </summary>
    /// <typeparam name="TFunctionMessage">Nethereum FunctionMessage type</typeparam>
    /// <param name="fn">Function message instance to serialize</param>
    /// <returns>JSON string of parameter name-value pairs, or ToString() fallback if serialization fails</returns>
    /// <remarks>
    /// Uses reflection to find properties with Nethereum's [Parameter] attribute.
    /// Falls back to ToString() if JSON serialization fails (e.g., circular references, unsupported types).
    /// </remarks>
    private string? SerializeFunction<TFunctionMessage>(TFunctionMessage fn)
        where TFunctionMessage : FunctionMessage
    {
        if (fn == null) return null;

        try
        {
            // Extract all properties with [Parameter] attributes
            var parameters = new Dictionary<string, object?>();
            var properties = typeof(TFunctionMessage).GetProperties();

            foreach (var prop in properties)
            {
                var paramAttr = prop.GetCustomAttribute<ParameterAttribute>();
                if (paramAttr != null)
                {
                    var value = prop.GetValue(fn);
                    parameters[paramAttr.Name ?? prop.Name] = value;
                }
            }

            return System.Text.Json.JsonSerializer.Serialize(parameters);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to serialize function {FunctionType}, falling back to ToString()", typeof(TFunctionMessage).Name);
            return fn.ToString();
        }
    }

    /// <summary>
    /// Serializes an arbitrary payload object to JSON.
    /// </summary>
    /// <param name="payload">Object to serialize (typically an anonymous object with operation parameters)</param>
    /// <returns>JSON string, or ToString() fallback if serialization fails</returns>
    /// <remarks>
    /// Falls back to ToString() if JSON serialization fails (e.g., circular references, unsupported types).
    /// Logs warning on serialization failure for debugging.
    /// </remarks>
    private string? SerializePayload(object? payload)
    {
        if (payload == null) return null;

        try
        {
            return System.Text.Json.JsonSerializer.Serialize(payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to serialize payload of type {PayloadType}, falling back to ToString()", payload.GetType().Name);
            return payload.ToString();
        }
    }
}