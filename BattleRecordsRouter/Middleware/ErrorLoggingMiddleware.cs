// -----------------------------------------------------------------------------
//  ErrorLoggingMiddleware
//  Uprising Labs · Gamers L.A.B.
// -----------------------------------------------------------------------------
//  ? Purpose
//      ? Intercepts HTTP responses with status codes >= 400 (both 4xx and 5xx)
//      ? Logs comprehensive error details to database for monitoring and debugging
//      ? Captures request/response context including IP, headers, and body content
//  ? Features
//      ? Response body capture via MemoryStream interception
//      ? Fire-and-forget logging to avoid blocking responses
//      ? Request body buffering with size limits (5KB max)
//      ? Proxy-aware IP detection (X-Forwarded-For, X-Real-IP)
//      ? JWT wallet extraction from claims
//      ? Safe error handling to prevent middleware crashes
//  ? Updates
//      ? 2025-01-XX: Fixed memory leaks - added proper StreamReader disposal with leaveOpen
//      ? 2025-01-XX: Optimized request body reading - now reads once and caches result
//      ? 2025-01-XX: Added 5KB max body size check to prevent memory issues
//      ? 2025-01-XX: Extended error logging to include 500+ status codes
//      ? 2025-01-XX: Moved EnableBuffering to start of InvokeAsync for efficiency
// -----------------------------------------------------------------------------

using BattleRecordsRouter.Repositories;
using System.Text.Json;

namespace BattleRecordsRouter.Middleware;

/// <summary>
/// Middleware that logs HTTP errors (4xx and 5xx status codes) to the database.
/// Captures comprehensive request/response context for debugging and monitoring.
/// </summary>
public class ErrorLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorLoggingMiddleware> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Initializes a new instance of the ErrorLoggingMiddleware.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">Logger for diagnostic messages.</param>
    /// <param name="scopeFactory">Factory for creating service scopes to access scoped services.</param>
    public ErrorLoggingMiddleware(RequestDelegate next, ILogger<ErrorLoggingMiddleware> logger, IServiceScopeFactory scopeFactory)
    {
        _next = next;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Processes an HTTP request and logs errors if the response status code is >= 400.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <remarks>
    /// This method intercepts the response body stream to capture error responses,
    /// then asynchronously logs the error details to the database without blocking
    /// the response to the client.
    /// Updated: Now calls EnableBuffering at the start for efficiency.
    /// </remarks>
    public async Task InvokeAsync(HttpContext context)
    {
        var originalBodyStream = context.Response.Body;

        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        // Enable request body buffering once at the start
        context.Request.EnableBuffering();

        try
        {
            await _next(context);
        }
        finally
        {
            // Check if it's an error response we want to log (both 4xx and 5xx)
            if (context.Response.StatusCode >= 400)
            {
                await LogHttpErrorAsync(context, responseBody);
            }

            responseBody.Seek(0, SeekOrigin.Begin);
            await responseBody.CopyToAsync(originalBodyStream);
        }
    }

    /// <summary>
    /// Asynchronously logs HTTP error details to the database.
    /// </summary>
    /// <param name="context">The HTTP context containing request/response information.</param>
    /// <param name="responseBody">The captured response body stream.</param>
    /// <remarks>
    /// This method runs in a fire-and-forget manner using Task.Run to avoid blocking
    /// the response to the client. It captures comprehensive error context including
    /// request/response bodies, headers, IP address, and JWT claims.
    /// Updated: Now reads request body once and caches it for efficiency.
    /// Updated: Added 5KB max body size check to prevent memory issues.
    /// Updated: Uses proper StreamReader disposal with leaveOpen to prevent memory leaks.
    /// </remarks>
    private async Task LogHttpErrorAsync(HttpContext context, MemoryStream responseBody)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var errorLogService = scope.ServiceProvider.GetService<IErrorLogDBServices>();

            if (errorLogService == null) return;

            responseBody.Seek(0, SeekOrigin.Begin);
            string responseText;
            using (var reader = new StreamReader(responseBody, leaveOpen: true))
            {
                responseText = await reader.ReadToEndAsync();
            }

            // Read request body once and cache it
            string? requestBodyText = null;
            const int maxBodySize = 5 * 1024; // 5KB limit
            if (context.Request.ContentLength > 0 && context.Request.ContentLength <= maxBodySize)
            {
                try
                {
                    context.Request.Body.Position = 0;
                    using (var reader = new StreamReader(context.Request.Body, leaveOpen: true))
                    {
                        requestBodyText = await reader.ReadToEndAsync();
                    }
                    context.Request.Body.Position = 0;
                }
                catch
                {
                    // Ignore request body read errors
                }
            }

            var fullPath = GetFullRequestPath(context);
            var operationName = $"{context.Request.Method} {fullPath}";
            var errorType = GetErrorType(context.Response.StatusCode);
            var errorMessage = BuildErrorMessage(context, responseText, requestBodyText);
            var walletAddress = ExtractWalletFromContext(context, requestBodyText);
            var clientIp = GetClientIpAddress(context);

            _ = Task.Run(async () => await errorLogService.LogErrorAsync(
                operationName,
                errorType,
                errorMessage,
                stackTrace: null,
                contractAddress: null,
                walletAddress: walletAddress,
                httpStatusCode: context.Response.StatusCode,
                requestPath: fullPath,
                userAgent: context.Request.Headers.UserAgent.ToString(),
                clientIp: clientIp));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log HTTP error to database");
        }
    }

    /// <summary>
    /// Maps HTTP status codes to human-readable error type names.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <returns>A descriptive error type name.</returns>
    /// <remarks>
    /// Updated: Now includes 500+ status codes (InternalServerError, NotImplemented,
    /// BadGateway, ServiceUnavailable, GatewayTimeout).
    /// </remarks>
    private static string GetErrorType(int statusCode) => statusCode switch
    {
        400 => "ValidationError",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "NotFound",
        409 => "Conflict",
        422 => "UnprocessableEntity",
        429 => "TooManyRequests",
        500 => "InternalServerError",
        501 => "NotImplemented",
        502 => "BadGateway",
        503 => "ServiceUnavailable",
        504 => "GatewayTimeout",
        _ => $"HttpError{statusCode}"
    };

    /// <summary>
    /// Constructs the full request path including query string.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The full request path with query string if present.</returns>
    private static string GetFullRequestPath(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        var queryString = context.Request.QueryString.Value;
        
        return string.IsNullOrEmpty(queryString) ? path : $"{path}{queryString}";
    }

    /// <summary>
    /// Extracts the client's IP address from the request, checking proxy headers first.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The client's IP address, or "Unknown" if not available.</returns>
    /// <remarks>
    /// Checks headers in order: X-Forwarded-For, X-Real-IP, then falls back to
    /// connection remote IP. This ensures correct IP detection behind proxies and load balancers.
    /// </remarks>
    private static string GetClientIpAddress(HttpContext context)
    {
        // Check for forwarded IP first (common in load balancers/proxies)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // X-Forwarded-For can contain multiple IPs, take the first one
            return forwardedFor.Split(',')[0].Trim();
        }

        // Check for real IP header (some proxies use this)
        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        // Fall back to connection remote IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
    }

    /// <summary>
    /// Builds a comprehensive error message including request/response details.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="responseText">The response body text.</param>
    /// <param name="requestBodyText">The cached request body text (may be null if too large or unavailable).</param>
    /// <returns>A formatted error message with all relevant context.</returns>
    /// <remarks>
    /// Updated: Now accepts cached requestBodyText parameter to avoid re-reading request body.
    /// </remarks>
    private static string BuildErrorMessage(HttpContext context, string responseText, string? requestBodyText)
    {
        var fullPath = GetFullRequestPath(context);
        var clientIp = GetClientIpAddress(context);
        var message = $"HTTP {context.Response.StatusCode} - {context.Request.Method} {fullPath}";

        // Add IP address
        message += $" | IP: {clientIp}";

        // Add User-Agent for context
        var userAgent = context.Request.Headers.UserAgent.ToString();
        if (!string.IsNullOrEmpty(userAgent))
        {
            message += $" | User-Agent: {userAgent}";
        }

        // Add Referer if available
        var referer = context.Request.Headers.Referer.ToString();
        if (!string.IsNullOrEmpty(referer))
        {
            message += $" | Referer: {referer}";
        }

        // Add Host header to see what domain they're hitting
        var host = context.Request.Headers.Host.ToString();
        if (!string.IsNullOrEmpty(host))
        {
            message += $" | Host: {host}";
        }

        // Add request headers that might be useful for debugging
        var acceptHeader = context.Request.Headers.Accept.ToString();
        if (!string.IsNullOrEmpty(acceptHeader))
        {
            message += $" | Accept: {acceptHeader}";
        }

        if (!string.IsNullOrEmpty(responseText))
        {
            try
            {
                // Try to parse and format JSON response
                var jsonDoc = JsonDocument.Parse(responseText);
                if (jsonDoc.RootElement.TryGetProperty("errors", out var errors))
                {
                    message += $" | Validation Errors: {errors}";
                }
                else if (jsonDoc.RootElement.TryGetProperty("error", out var error))
                {
                    message += $" | Error: {error}";
                }
                else
                {
                    message += $" | Response: {responseText}";
                }
            }
            catch
            {
                message += $" | Response: {responseText}";
            }
        }

        // Add request body if available (already read and cached)
        if (!string.IsNullOrEmpty(requestBodyText))
        {
            message += $" | Request: {requestBodyText}";
        }
        else if (context.Request.ContentLength > 5 * 1024)
        {
            message += $" | Request: [Body too large: {context.Request.ContentLength} bytes]";
        }

        return message;
    }

    /// <summary>
    /// Attempts to extract the wallet address from JWT claims or request body.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="requestBodyText">The cached request body text (may be null).</param>
    /// <returns>The wallet address if found, otherwise null.</returns>
    /// <remarks>
    /// Checks JWT claims first, then falls back to parsing request body for auth endpoints.
    /// Updated: Now accepts cached requestBodyText parameter to avoid re-reading request body.
    /// </remarks>
    private static string? ExtractWalletFromContext(HttpContext context, string? requestBodyText)
    {
        // Try to get wallet from JWT claims
        var walletClaim = context.User?.FindFirst("wallet")?.Value;
        if (!string.IsNullOrEmpty(walletClaim))
            return walletClaim;

        // Try to get from request body (for auth endpoints) - use cached request body
        if (!string.IsNullOrEmpty(requestBodyText))
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(requestBodyText);
                if (jsonDoc.RootElement.TryGetProperty("address", out var address))
                {
                    return address.GetString();
                }
            }
            catch
            {
                // Ignore parsing errors
            }
        }

        return null;
    }
}