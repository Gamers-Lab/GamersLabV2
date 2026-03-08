namespace BattleRecordsRouter.Repositories;

public interface IErrorLogDBServices
{
    /// <summary>
    /// Log an error to the database.
    /// </summary>
    /// <param name="operationName">Name of the operation that failed</param>
    /// <param name="errorType">Type of error (e.g., SmartContractCustomError, RpcError, etc.)</param>
    /// <param name="errorMessage">Error message</param>
    /// <param name="stackTrace">Stack trace (optional)</param>
    /// <param name="contractAddress">Contract address (optional)</param>
    /// <param name="walletAddress">Wallet address (optional)</param>
    /// <param name="httpStatusCode">HTTP status code (optional)</param>
    /// <param name="requestPath">Request path (optional)</param>
    /// <param name="userAgent">User agent (optional)</param>
    /// <param name="clientIp">Client IP address (optional)</param>
    Task LogErrorAsync(string operationName, string errorType, string errorMessage, 
        string? stackTrace = null, string? contractAddress = null, string? walletAddress = null,
        int? httpStatusCode = null, string? requestPath = null, string? userAgent = null, string? clientIp = null);
}