using BattleRecordsRouter.Models;
using BattleRecordsRouter.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace BattleRecordsRouter.Helper.Blockchain.Response;

public static class ApiResponseHelper
{
    /// <summary>Set once in Program.cs.</summary>
    public static bool IsDevelopment { get; set; }

    #region Private Helper Methods

    /// <summary>
    /// Extracts the full request path from HttpContext.
    /// </summary>
    private static string GetRequestPath(HttpContext? httpContext)
    {
        if (httpContext == null) return string.Empty;

        var request = httpContext.Request;
        return $"{request.Path}{request.QueryString}";
    }

    /// <summary>
    /// Extracts the user agent from HttpContext.
    /// </summary>
    private static string? GetUserAgent(HttpContext? httpContext)
    {
        return httpContext?.Request.Headers.UserAgent.ToString();
    }

    /// <summary>
    /// Extracts the client IP address from HttpContext.
    /// </summary>
    private static string? GetClientIp(HttpContext? httpContext)
    {
        if (httpContext == null) return null;

        // Try X-Forwarded-For header first (for proxies/load balancers)
        var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        // Fall back to RemoteIpAddress
        return httpContext.Connection.RemoteIpAddress?.ToString();
    }

    /// <summary>
    /// Logs an error to the database asynchronously (fire-and-forget).
    /// </summary>
    private static void LogErrorToDatabase(
        IErrorLogDBServices? errorLogService,
        string operationName,
        string errorType,
        string errorMessage,
        int httpStatusCode,
        HttpContext? httpContext,
        string? contractAddress = null,
        string? walletAddress = null,
        string? stackTrace = null)
    {
        if (errorLogService == null) return;

        _ = Task.Run(async () => await errorLogService.LogErrorAsync(
            operationName,
            errorType,
            errorMessage,
            stackTrace,
            contractAddress,
            walletAddress,
            httpStatusCode,
            GetRequestPath(httpContext),
            GetUserAgent(httpContext),
            GetClientIp(httpContext)));
    }

    #endregion

    public static IActionResult TransactionOrError(string txHash, ILogger logger, string operationName)
    {
        if (string.IsNullOrWhiteSpace(txHash))
        {
            logger.LogError("{Operation} failed: transaction hash was null or empty", operationName);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        return new OkObjectResult(new TransactionResponse { TransactionHash = txHash });
    }

    public static IActionResult TransactionOrError<T>(
        string txHash, T payload, ILogger logger, string operationName)
    {
        if (string.IsNullOrWhiteSpace(txHash))
        {
            logger.LogError("{Operation} failed: transaction hash was null or empty", operationName);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        return new OkObjectResult(payload);
    }

    // ✅ New overload for raw view result (no txHash)
    public static IActionResult ViewOrError<T>(
        T? payload, ILogger logger, string operationName)
    {
        if (payload == null)
        {
            logger.LogError("{Operation} failed: payload is null", operationName);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        return new OkObjectResult(payload);
    }

    public static async Task<IActionResult> HandleSafe(
        Func<Task<IActionResult>> func,
        ILogger logger,
        string operationName,
        HttpContext? httpContext = null,
        IErrorLogDBServices? errorLogService = null,
        string? contractAddress = null,
        string? walletAddress = null)
    {
        try
        {
            return await func();
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex,
                "Validation error during {Operation}: {Message}",
                operationName, ex.Message);

            // Log validation error to DB with HTTP context
            LogErrorToDatabase(
                errorLogService,
                operationName,
                "ArgumentException",
                ex.Message,
                StatusCodes.Status400BadRequest,
                httpContext,
                contractAddress,
                walletAddress,
                ex.StackTrace);

            return new BadRequestObjectResult(new
            {
                error = "Validation failed",
                details = ex.Message
            });
        }
        catch (BusyException ex)
        {
            logger.LogWarning(ex,
                "Service busy during {Operation}: {Message}",
                operationName, ex.Message);

            // Log busy error to DB with HTTP context
            LogErrorToDatabase(
                errorLogService,
                operationName,
                "BusyException",
                ex.Message,
                StatusCodes.Status503ServiceUnavailable,
                httpContext,
                contractAddress,
                walletAddress,
                ex.StackTrace);

            return new ObjectResult(new
            {
                error = "Service busy",
                code = "BUSY",
                details = ex.Message
            })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Unhandled error during {Operation}",
                operationName);

            // Log general exception to DB with HTTP context
            LogErrorToDatabase(
                errorLogService,
                operationName,
                ex.GetType().Name,
                ex.Message,
                StatusCodes.Status500InternalServerError,
                httpContext,
                contractAddress,
                walletAddress,
                ex.StackTrace);

            var body = new
            {
                error = "Internal server error.",
                details = IsDevelopment ? ex.ToString() : null
            };

            return new ObjectResult(body)
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }

    /// <summary>
    /// Returns a standardized 404 Not Found response.
    /// </summary>
    /// <param name="resource">The type of resource that was not found (e.g., "Player", "Record")</param>
    /// <param name="identifier">The identifier used to search for the resource</param>
    /// <param name="logger">Logger instance for recording the event</param>
    /// <param name="operationName">Name of the operation for logging context</param>
    /// <param name="httpContext">HTTP context for capturing request information (optional)</param>
    /// <param name="errorLogService">Error logging service for database logging (optional)</param>
    /// <param name="contractAddress">Contract address related to the error (optional)</param>
    /// <param name="walletAddress">Wallet address related to the error (optional)</param>
    /// <returns>404 Not Found response with standardized error format</returns>
    public static IActionResult NotFoundError(
        string resource,
        string identifier,
        ILogger logger,
        string operationName,
        HttpContext? httpContext = null,
        IErrorLogDBServices? errorLogService = null,
        string? contractAddress = null,
        string? walletAddress = null)
    {
        var errorMessage = $"No {resource.ToLowerInvariant()} exists with identifier: {identifier}";

        logger.LogInformation("{Operation}: {Resource} not found - {Identifier}",
            operationName, resource, identifier);

        // Log to database
        LogErrorToDatabase(
            errorLogService,
            operationName,
            "NotFound",
            errorMessage,
            StatusCodes.Status404NotFound,
            httpContext,
            contractAddress,
            walletAddress);

        return new NotFoundObjectResult(new
        {
            error = $"{resource} not found",
            details = errorMessage
        });
    }

    /// <summary>
    /// Returns a standardized 400 Bad Request response for validation errors.
    /// </summary>
    /// <param name="message">The validation error message</param>
    /// <param name="logger">Logger instance for recording the event</param>
    /// <param name="operationName">Name of the operation for logging context</param>
    /// <param name="httpContext">HTTP context for capturing request information (optional)</param>
    /// <param name="errorLogService">Error logging service for database logging (optional)</param>
    /// <param name="contractAddress">Contract address related to the error (optional)</param>
    /// <param name="walletAddress">Wallet address related to the error (optional)</param>
    /// <returns>400 Bad Request response with standardized error format</returns>
    public static IActionResult ValidationError(
        string message,
        ILogger logger,
        string operationName,
        HttpContext? httpContext = null,
        IErrorLogDBServices? errorLogService = null,
        string? contractAddress = null,
        string? walletAddress = null)
    {
        logger.LogWarning("{Operation}: Validation error - {Message}", operationName, message);

        // Log to database
        LogErrorToDatabase(
            errorLogService,
            operationName,
            "ValidationError",
            message,
            StatusCodes.Status400BadRequest,
            httpContext,
            contractAddress,
            walletAddress);

        return new BadRequestObjectResult(new
        {
            error = "Validation failed",
            details = message
        });
    }

    /// <summary>
    /// Returns a standardized 401 Unauthorized response for authentication failures.
    /// </summary>
    /// <param name="reason">The reason for authentication failure</param>
    /// <param name="logger">Logger instance for recording the event</param>
    /// <param name="operationName">Name of the operation for logging context</param>
    /// <param name="httpContext">HTTP context for capturing request information (optional)</param>
    /// <param name="errorLogService">Error logging service for database logging (optional)</param>
    /// <param name="contractAddress">Contract address related to the error (optional)</param>
    /// <param name="walletAddress">Wallet address related to the error (optional)</param>
    /// <returns>401 Unauthorized response with standardized error format</returns>
    public static IActionResult AuthenticationError(
        string reason,
        ILogger logger,
        string operationName,
        HttpContext? httpContext = null,
        IErrorLogDBServices? errorLogService = null,
        string? contractAddress = null,
        string? walletAddress = null)
    {
        logger.LogWarning("{Operation}: Authentication failed - {Reason}", operationName, reason);

        // Log to database
        LogErrorToDatabase(
            errorLogService,
            operationName,
            "AuthenticationError",
            reason,
            StatusCodes.Status401Unauthorized,
            httpContext,
            contractAddress,
            walletAddress);

        return new UnauthorizedObjectResult(new
        {
            error = "Authentication failed",
            details = reason
        });
    }

    /// <summary>
    /// Returns a standardized 403 Forbidden response for authorization failures.
    /// </summary>
    /// <param name="reason">The reason for authorization failure</param>
    /// <param name="logger">Logger instance for recording the event</param>
    /// <param name="operationName">Name of the operation for logging context</param>
    /// <param name="httpContext">HTTP context for capturing request information (optional)</param>
    /// <param name="errorLogService">Error logging service for database logging (optional)</param>
    /// <param name="contractAddress">Contract address related to the error (optional)</param>
    /// <param name="walletAddress">Wallet address related to the error (optional)</param>
    /// <returns>403 Forbidden response with standardized error format</returns>
    public static IActionResult AuthorizationError(
        string reason,
        ILogger logger,
        string operationName,
        HttpContext? httpContext = null,
        IErrorLogDBServices? errorLogService = null,
        string? contractAddress = null,
        string? walletAddress = null)
    {
        logger.LogWarning("{Operation}: Authorization failed - {Reason}", operationName, reason);

        // Log to database
        LogErrorToDatabase(
            errorLogService,
            operationName,
            "AuthorizationError",
            reason,
            StatusCodes.Status403Forbidden,
            httpContext,
            contractAddress,
            walletAddress);

        return new ObjectResult(new
        {
            error = "Access denied",
            details = reason
        })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }

    /// <summary>
    /// Returns a standardized 409 Conflict response for resource conflicts.
    /// </summary>
    /// <param name="message">The conflict error message</param>
    /// <param name="logger">Logger instance for recording the event</param>
    /// <param name="operationName">Name of the operation for logging context</param>
    /// <param name="httpContext">HTTP context for capturing request information (optional)</param>
    /// <param name="errorLogService">Error logging service for database logging (optional)</param>
    /// <param name="contractAddress">Contract address related to the error (optional)</param>
    /// <param name="walletAddress">Wallet address related to the error (optional)</param>
    /// <returns>409 Conflict response with standardized error format</returns>
    public static IActionResult ConflictError(
        string message,
        ILogger logger,
        string operationName,
        HttpContext? httpContext = null,
        IErrorLogDBServices? errorLogService = null,
        string? contractAddress = null,
        string? walletAddress = null)
    {
        logger.LogInformation("{Operation}: Conflict - {Message}", operationName, message);

        // Log to database
        LogErrorToDatabase(
            errorLogService,
            operationName,
            "ConflictError",
            message,
            StatusCodes.Status409Conflict,
            httpContext,
            contractAddress,
            walletAddress);

        return new ConflictObjectResult(new
        {
            error = "Resource conflict",
            details = message
        });
    }
}