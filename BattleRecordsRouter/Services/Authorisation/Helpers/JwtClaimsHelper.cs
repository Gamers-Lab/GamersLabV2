using Microsoft.AspNetCore.Mvc;
using System.Runtime.CompilerServices;
using UP.HTTP;

namespace BattleRecordsRouter.Helper
{
    public static class JwtClaimsHelper
    {
        /// <summary>
        /// Extracts required player index and application ID from JWT claims
        /// </summary>
        /// <param name="httpContext">The current HTTP context</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="playerIndex">Output parameter for player index</param>
        /// <param name="applicationId">Output parameter for application ID</param>
        /// <param name="errorResult">Output parameter for error result if extraction fails</param>
        /// <param name="callerMemberName">Automatically populated with the calling method name</param>
        /// <returns>True if extraction was successful, false otherwise</returns>
        public static bool TryExtractRequiredClaims(
            HttpContext httpContext, 
            ILogger logger,
            out uint playerIndex,
            out ulong applicationId,
            out IActionResult errorResult,
            [CallerMemberName] string callerMemberName = "")
        {
            // Initialize out parameters
            playerIndex = 0;
            applicationId = 0;
            errorResult = null!;

            // Get values from JWT
            uint? playerIndexNullable = SiweJwtMiddleware.GetPlayerIndex(httpContext);
            ulong? applicationIdNullable = SiweJwtMiddleware.GetApplicationId(httpContext);

            // Check if we have valid player index
            if (playerIndexNullable is null)
            {
                logger.LogWarning("JWT missing playerIndex claim in {Operation}", callerMemberName);
                errorResult = new UnauthorizedObjectResult("JWT missing playerIndex claim");
                return false;
            }

            // Check if we have valid application ID
            if (applicationIdNullable is null)
            {
                logger.LogWarning("JWT missing applicationId claim in {Operation}", callerMemberName);
                errorResult = new UnauthorizedObjectResult("JWT missing applicationId claim");
                return false;
            }

            // Set output values
            playerIndex = playerIndexNullable.Value;
            applicationId = applicationIdNullable.Value;
            return true;
        }

        /// <summary>
        /// Checks if the current user has the admin role
        /// </summary>
        /// <param name="httpContext">The current HTTP context</param>
        /// <returns>True if user has admin role, false otherwise</returns>
        public static bool IsAdmin(HttpContext httpContext)
        {
            var roles = SiweJwtMiddleware.GetRoles(httpContext);
            return roles.Contains("admin", StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolves player index with admin override support.
        /// Player index can ONLY be pulled from JWT claims unless the user has admin role.
        /// </summary>
        /// <param name="httpContext">The current HTTP context</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="requestPlayerIndex">Optional player index from request (only used if admin)</param>
        /// <param name="playerIndex">Output parameter for resolved player index</param>
        /// <param name="errorResult">Output parameter for error result if resolution fails</param>
        /// <param name="callerMemberName">Automatically populated with the calling method name</param>
        /// <returns>True if resolution was successful, false otherwise</returns>
        public static bool TryResolvePlayerIndex(
            HttpContext httpContext,
            ILogger logger,
            uint? requestPlayerIndex,
            out uint playerIndex,
            out IActionResult errorResult,
            [CallerMemberName] string callerMemberName = "")
        {
            // Initialize out parameters
            playerIndex = 0;
            errorResult = null!;

            // Extract JWT claims
            if (!TryExtractRequiredClaims(httpContext, logger, out uint jwtPlayerIndex,
                out _, out errorResult, callerMemberName))
            {
                return false;
            }

            // If no override requested, use JWT value
            if (requestPlayerIndex is null)
            {
                playerIndex = jwtPlayerIndex;
                return true;
            }

            // Override requested - check if user is admin
            if (!IsAdmin(httpContext))
            {
                logger.LogWarning(
                    "Non-admin user attempted to override playerIndex from {JwtIndex} to {RequestIndex} in {Operation}",
                    jwtPlayerIndex, requestPlayerIndex.Value, callerMemberName);

                errorResult = new ObjectResult(new
                {
                    error = "authorization",
                    details = "Only administrators can specify a different player index"
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
                return false;
            }

            // Admin override allowed
            logger.LogInformation(
                "Admin override: Using playerIndex {RequestIndex} instead of JWT {JwtIndex} in {Operation}",
                requestPlayerIndex.Value, jwtPlayerIndex, callerMemberName);

            playerIndex = requestPlayerIndex.Value;
            return true;
        }
    }
}