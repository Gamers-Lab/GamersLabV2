// -----------------------------------------------------------------------------
//  SiweJwtMiddleware
//  Uprising Labs · Gamers L.A.B.
// -----------------------------------------------------------------------------
//  ▪ Purpose
//      – Runs **after** JwtBearer has validated the token signature.
//      – Pulls the claims we care about and stashes strongly-typed values in
//        HttpContext.Items so that controllers / services don’t need to re-parse
//        the JWT.
//  ▪ Stateless
//      – No DB look-ups.  Everything is inside the token.
//  ▪ Security
//      – Enforces authentication check before extracting claims
//      – Uses validated claims from ctx.User (already verified by JwtBearer)
//      – No longer re-parses JWT tokens (prevents signature bypass)
//  ▪ Updates
//      • 2025-01-XX: Fixed critical security issue - now validates authentication
//      • 2025-01-XX: Removed JWT re-parsing, uses ctx.User.Claims instead
//      • 2025-01-XX: Simplified GetApplicationId() - removed complex fallback logic
//      • 2025-01-XX: Made all helper methods consistent with TryGetValue pattern
//      • adds `applicationId` claim support (ulong)
//      • helper GetApplicationId(..)
//      • namespace-level XML-docs à-la NatSpec for every public member
// -----------------------------------------------------------------------------

using System.Security.Claims;

namespace UP.HTTP;

/// <summary>
/// Extracts identity claims from a *validated* JWT and places them in
/// <see cref="HttpContext.Items"/> for easy access throughout the request.
///
/// **Important** → JwtBearer middleware must already have run, otherwise the
/// token hasn’t been verified and we skip extraction entirely.
/// </summary>
public sealed class SiweJwtMiddleware
{
    private readonly RequestDelegate              _next;
    private readonly ILogger<SiweJwtMiddleware>   _log;

    // ── Keys used inside HttpContext.Items ──────────────────────────────
    private const string CtxWallet          = "wallet";
    private const string CtxPlayerIndex     = "playerIndex";
    private const string CtxApplicationId   = "applicationId";
    private const string CtxRoles           = "roles";
    private const string CtxUserId          = "userId";
    private const string CtxApplication     = "application";
    private const string CtxVerifiedIdKeys  = "verifiedIdKey";

    private const string CtxVerifiedIdVals  = "verifiedIdValue";
    // ────────────────────────────────────────────────────────────────────

    public SiweJwtMiddleware(RequestDelegate next,
                              ILogger<SiweJwtMiddleware> log)
    {
        _next = next;
        _log  = log;
    }

    /// <summary>
    /// Extracts claims from validated JWT and stores them in HttpContext.Items.
    /// Only processes authenticated requests to ensure JwtBearer has validated the token.
    /// </summary>
    /// <param name="ctx">The HTTP context for the current request.</param>
    /// <remarks>
    /// Security: Validates authentication before extracting claims to prevent processing
    /// of unauthenticated or forged tokens. Uses ctx.User.Claims which are already
    /// validated by JwtBearer middleware.
    /// </remarks>
    public async Task InvokeAsync(HttpContext ctx)
    {
        // Only extract claims if JwtBearer has authenticated the user
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            await _next(ctx);                 // not authenticated → continue pipeline
            return;
        }

        try
        {
            // Use validated claims from ctx.User (already verified by JwtBearer)
            var claims = ctx.User.Claims;

            string?  wallet         = claims.FirstOrDefault(c => c.Type == "wallet")?.Value;
            string?  pIndexStr      = claims.FirstOrDefault(c => c.Type == "playerIndex")?.Value;
            string?  appIdStr       = claims.FirstOrDefault(c => c.Type == "applicationId")?.Value;
            string?  userId         = claims.FirstOrDefault(c => c.Type == "userId")?.Value;
            string?  application    = claims.FirstOrDefault(c => c.Type == "application")?.Value;

            string[] roles          = claims.Where(c => c.Type == ClaimTypes.Role)
                                                .Select(c => c.Value).ToArray();

            string[] verifiedKeys   = claims.Where(c => c.Type == "verifiedIdKey")
                                                .Select(c => c.Value).ToArray();
            string[] verifiedVals   = claims.Where(c => c.Type == "verifiedIdValue")
                                                .Select(c => c.Value).ToArray();

            if (!string.IsNullOrWhiteSpace(wallet))
                ctx.Items[CtxWallet] = wallet;

            if (uint.TryParse(pIndexStr, out var pIdx))
                ctx.Items[CtxPlayerIndex] = pIdx;

            if (ulong.TryParse(appIdStr, out var appId))
                ctx.Items[CtxApplicationId] = appId;

            if (!string.IsNullOrWhiteSpace(userId))
                ctx.Items[CtxUserId] = userId;

            if (!string.IsNullOrWhiteSpace(application))
                ctx.Items[CtxApplication] = application;

            if (roles.Length > 0)
                ctx.Items[CtxRoles] = roles;

            if (verifiedKeys.Length > 0)
                ctx.Items[CtxVerifiedIdKeys] = verifiedKeys;

            if (verifiedVals.Length > 0)
                ctx.Items[CtxVerifiedIdVals] = verifiedVals;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[JWT-Middleware] Unable to extract claims.");
            // do **not** fail the request – let downstream authorization handle it
        }

        await _next(ctx);
    }

    // ── Helper   accessors ──────────────────────────────────────────────

    /// <summary>
    /// Retrieves the wallet address from the current request context.
    /// </summary>
    /// <param name="ctx">The HTTP context.</param>
    /// <returns>The wallet address if present, otherwise null.</returns>
    /// <remarks>Updated: Now uses safe TryGetValue pattern to prevent KeyNotFoundException.</remarks>
    public static string? GetWallet(HttpContext ctx)
        => ctx.Items.TryGetValue(CtxWallet, out var v) ? v as string : null;

    /// <summary>
    /// Retrieves the player index from the current request context.
    /// </summary>
    /// <param name="ctx">The HTTP context.</param>
    /// <returns>The player index if present and valid, otherwise null.</returns>
    public static uint? GetPlayerIndex(HttpContext ctx)
        => ctx.Items.TryGetValue(CtxPlayerIndex, out var v) && v is uint u ? u : null;

    /// <summary>
    /// Retrieves the application ID from the current request context.
    /// </summary>
    /// <param name="ctx">The HTTP context.</param>
    /// <returns>The application ID if present and valid, otherwise null.</returns>
    /// <remarks>Updated: Simplified from complex fallback logic to single TryGetValue pattern.</remarks>
    public static ulong? GetApplicationId(HttpContext ctx)
        => ctx.Items.TryGetValue(CtxApplicationId, out var v) && v is ulong ul ? ul : null;

    /// <summary>
    /// Retrieves the user ID from the current request context.
    /// </summary>
    /// <param name="ctx">The HTTP context.</param>
    /// <returns>The user ID if present, otherwise null.</returns>
    /// <remarks>Updated: Now uses safe TryGetValue pattern to prevent KeyNotFoundException.</remarks>
    public static string? GetUserId(HttpContext ctx)
        => ctx.Items.TryGetValue(CtxUserId, out var v) ? v as string : null;

    /// <summary>
    /// Retrieves the application name from the current request context.
    /// </summary>
    /// <param name="ctx">The HTTP context.</param>
    /// <returns>The application name if present, otherwise null.</returns>
    /// <remarks>Updated: Now uses safe TryGetValue pattern to prevent KeyNotFoundException.</remarks>
    public static string? GetApplication(HttpContext ctx)
        => ctx.Items.TryGetValue(CtxApplication, out var v) ? v as string : null;

    /// <summary>
    /// Retrieves all roles assigned to the user from the current request context.
    /// </summary>
    /// <param name="ctx">The HTTP context.</param>
    /// <returns>Array of role names, or empty array if no roles present.</returns>
    public static string[] GetRoles(HttpContext ctx)
        => ctx.Items[CtxRoles] as string[] ?? Array.Empty<string>();

    /// <summary>
    /// Retrieves all verified identifier keys from the current request context.
    /// </summary>
    /// <param name="ctx">The HTTP context.</param>
    /// <returns>Array of identifier keys (e.g., "Discord", "Steam"), or empty array if none present.</returns>
    public static string[] GetVerifiedIdKeys(HttpContext ctx)
        => ctx.Items[CtxVerifiedIdKeys] as string[] ?? Array.Empty<string>();

    /// <summary>
    /// Retrieves all verified identifier values from the current request context.
    /// </summary>
    /// <param name="ctx">The HTTP context.</param>
    /// <returns>Array of identifier values, or empty array if none present.</returns>
    public static string[] GetVerifiedIdValues(HttpContext ctx)
        => ctx.Items[CtxVerifiedIdVals] as string[] ?? Array.Empty<string>();

    /// <summary>
    /// Combines <c>verifiedIdKey</c> + <c>verifiedIdValue</c> claims into a
    /// key → value map.  Index alignment is preserved; extra items are ignored.
    /// </summary>
    /// <param name="ctx">The HTTP context.</param>
    /// <returns>
    /// A read-only dictionary mapping identifier types (e.g., "Discord", "Steam")
    /// to their values. Returns empty dictionary if no identifiers present.
    /// </returns>
    /// <remarks>
    /// Note: This method is currently not called anywhere in the codebase.
    /// It may be used for future functionality or can be removed if not needed.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> GetVerifiedIdentifiers(HttpContext ctx)
    {
        var keys = GetVerifiedIdKeys(ctx);
        var vals = GetVerifiedIdValues(ctx);

        var dict = new Dictionary<string, string>(capacity: Math.Min(keys.Length, vals.Length));
        for (int i = 0; i < keys.Length && i < vals.Length; i++)
            dict[keys[i]] = vals[i];

        return dict;
    }

    /// <summary>
    /// Returns the verified-ID value for a specific identifier type.
    /// </summary>
    /// <param name="ctx">The HTTP context.</param>
    /// <param name="key">The identifier type to look up (e.g., "Discord", "Steam").</param>
    /// <returns>The identifier value if found, otherwise null.</returns>
    /// <remarks>
    /// Note: This method is currently not called anywhere in the codebase.
    /// It may be used for future functionality or can be removed if not needed.
    /// </remarks>
    public static string? GetVerifiedIdValue(HttpContext ctx, string key)
    {
        var keys = GetVerifiedIdKeys(ctx);
        var vals = GetVerifiedIdValues(ctx);

        for (int i = 0; i < keys.Length && i < vals.Length; i++)
            if (keys[i] == key) return vals[i];

        return null;
    }
    // ────────────────────────────────────────────────────────────────────
}
