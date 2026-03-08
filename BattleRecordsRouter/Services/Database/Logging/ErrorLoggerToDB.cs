using BattleRecordsRouter.Models;
using Supabase;

namespace BattleRecordsRouter.Repositories;

/// <summary>
/// Service for logging application errors to Supabase database.
/// Provides persistent error tracking for debugging, monitoring, and audit purposes.
/// </summary>
/// <remarks>
/// This service logs errors to the 'error_logs' table in Supabase, capturing:
/// - Operation context (operation name, HTTP status codes, request paths)
/// - Error details (type, message, stack trace)
/// - Blockchain context (contract address, wallet address)
/// - Request metadata (user agent, client IP)
///
/// Error Handling: Uses Console.Error fallback to avoid infinite loops if database logging fails.
/// Thread Safety: Safe for concurrent use via Supabase client.
/// </remarks>
public sealed class ErrorLoggerToDB : IErrorLogDBServices
{
    private readonly Client _supabase;

    /// <summary>
    /// Initializes a new instance of the ErrorLogDBServices class.
    /// </summary>
    /// <param name="supabase">Supabase client for database operations</param>
    public ErrorLoggerToDB(Client supabase) => _supabase = supabase;

    /// <summary>
    /// Logs an error to the Supabase database asynchronously.
    /// </summary>
    /// <param name="operationName">Name of the operation where the error occurred (e.g., "SetRecord", "CreatePlayer")</param>
    /// <param name="errorType">Type/category of error (e.g., "ValidationError", "BlockchainTimeout", "DatabaseError")</param>
    /// <param name="errorMessage">Human-readable error message</param>
    /// <param name="stackTrace">Optional stack trace for debugging</param>
    /// <param name="contractAddress">Optional blockchain contract address involved in the error</param>
    /// <param name="walletAddress">Optional wallet address involved in the error</param>
    /// <param name="httpStatusCode">Optional HTTP status code (e.g., 400, 500)</param>
    /// <param name="requestPath">Optional HTTP request path (e.g., "/api/records/set")</param>
    /// <param name="userAgent">Optional user agent string from HTTP request</param>
    /// <param name="clientIp">Optional client IP address</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method is fire-and-forget safe. If database logging fails, it writes to Console.Error
    /// instead of throwing an exception to prevent cascading failures.
    /// </remarks>
    public async Task LogErrorAsync(string operationName, string errorType, string errorMessage,
        string? stackTrace = null, string? contractAddress = null, string? walletAddress = null,
        int? httpStatusCode = null, string? requestPath = null, string? userAgent = null, string? clientIp = null)
    {
        try
        {
            var record = new ErrorLogRecord
            {
                OperationName = operationName,
                ErrorType = errorType,
                ErrorMessage = errorMessage,
                StackTrace = stackTrace,
                ContractAddress = contractAddress,
                WalletAddress = walletAddress,
                HttpStatusCode = httpStatusCode,
                RequestPath = requestPath,
                UserAgent = userAgent,
                ClientIp = clientIp,
                CreatedAt = DateTime.UtcNow
            };

            await _supabase
                .From<ErrorLogRecord>()
                .Insert(record);
        }
        catch (Exception ex)
        {
            // Avoid infinite loop - cannot use ILogger here as it might trigger another error log
            // Use Console.Error for production-safe fallback logging
            Console.Error.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] CRITICAL: Failed to log error to database for operation '{operationName}': {ex.Message}");
            Console.Error.WriteLine($"Original error being logged: {errorType} - {errorMessage}");
        }
    }
}