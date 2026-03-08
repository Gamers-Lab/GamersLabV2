using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BattleRecordsRouter.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nethereum.Util;
using BattleRecordsRouter.Repositories;
using BattleRecordsRouter.Services;

namespace BattleRecordsRouter.Siwe.Authorisation;

/// <summary>
/// Authorizes users via Ethereum wallet signature or Sequence ID token (JWT).
/// Supports nonce generation, signature verification, and custom JWT issuance.
/// </summary>
public sealed class GamersLabAuthorisationService : IGamersLabAuthorisationService
{
    private readonly byte[] _jwtKey;
    private readonly ILogger<GamersLabAuthorisationService> _log;
    private readonly string _applicationName;
    private readonly string _applicationAudience;
    private readonly string _sequenceProjectId;
    private readonly string _adminPassword;
    private readonly string _createPlayerAnonymousRequestPassword;
    private readonly IGamersLabStorageService _storage;
    private readonly string _steamKey;
    private readonly string _steamAppid;
    private readonly bool _serverSidePlayerCreationAllowed;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly bool _enableDebugLogging = true;

    // Cached JWKS (used for validating Sequence JWTs)
    private static JsonWebKeySet? _cachedJwks;
    private static DateTime _jwksLastFetched = DateTime.MinValue;
    private static readonly TimeSpan _jwksCacheDuration = TimeSpan.FromHours(1);

    public GamersLabAuthorisationService(
        IOptions<AppSettingsModel> cfg,
        ILogger<GamersLabAuthorisationService> log,
        IGamersLabStorageService storage,
        IHttpClientFactory httpClientFactory)
    {
        _jwtKey = Encoding.ASCII.GetBytes(cfg.Value.JWTKey);
        _applicationName = cfg.Value.ApplicationName;
        _applicationAudience = cfg.Value.ApplicationAudience;
        _sequenceProjectId = cfg.Value.SequenceProjectId;
        _steamKey = cfg.Value.SteamKey;
        _steamAppid = cfg.Value.SteamAppid;
        _createPlayerAnonymousRequestPassword = cfg.Value.CreatePlayerAnonymousRequest;
        _serverSidePlayerCreationAllowed = cfg.Value.ServerSidePlayerCreationAllowed;
        _log = log;
        _storage = storage;
        _adminPassword = cfg.Value.AdminPassword;
        _httpClientFactory = httpClientFactory;
    }

    #region Authentication

    public enum SequenceJwtAuthResult
    {
        Success,
        InvalidAddress,
        AddressNotFound,
        Banned,
        JWTFailed,
        Expired,
        InvalidSignature
    }

    /// <summary>
    /// Authenticates admin by validating password and returns custom JWT.
    /// </summary>
    public async Task<string?> AuthenticateAdminPassword(string password)
    {
        if (!IsPasswordValid(password, _adminPassword)) return null;
        return await GenerateJwtFromPlayerIndexAsync(0, refreshCount: 0, EnumRoles.admin);
    }

    /// <summary>
    /// Authenticates simple password for trying to create a new account
    /// </summary>
    /// <param name="password"></param>
    /// <returns></returns>
    public Task<bool> AuthenticateAccountCreationPassword(string password)
    {
        return Task.FromResult(IsPasswordValid(password, _createPlayerAnonymousRequestPassword));
    }

    /// <summary>
    /// Stateless JWT refresh: issues a new access token with incremented refresh count.
    /// Max 24 refreshes allowed before requiring re-authentication.
    /// </summary>
    /// <param name="token">The current valid JWT to refresh.</param>
    /// <returns>The new JWT if refresh is allowed; otherwise null.</returns>
    public async Task<string?> RefreshAccessTokenAsync(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);

            var wallet = jwt.Claims.FirstOrDefault(c => c.Type == "wallet")?.Value;
            var playerIndexStr = jwt.Claims.FirstOrDefault(c => c.Type == "playerIndex")?.Value;
            var refreshCountStr = jwt.Claims.FirstOrDefault(c => c.Type == "refreshCount")?.Value;

            if (!uint.TryParse(playerIndexStr, out var playerIndex)) return null;
            if (!int.TryParse(refreshCountStr, out var count)) count = 0;

            if (count >= 24)
            {
                _log.LogWarning("[JWT Refresh] Max refresh limit reached for #{index} ({wallet})", playerIndex,
                    wallet);
                return null;
            }

            var newJwt = await GenerateJwtFromPlayerIndexAsync(playerIndex, count + 1, EnumRoles.user);

            _log.LogInformation("[JWT Refresh] Refreshed token for #{index} ({wallet}) → count={count}",
                playerIndex,
                wallet, count + 1);

            return newJwt;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[JWT Refresh] Failed to refresh token.");
            return null;
        }
    }

    #endregion

    #region Public Functions

    /// <summary>
    /// Generate a JWT for a player index. Wrapper for internal function.
    /// </summary>
    /// <param name="playerIndex"></param>
    /// <returns></returns>
    public Task<string?> GenerateJwtFromPlayerIndexAsync(uint playerIndex)
    {
        return GenerateJwtFromPlayerIndexAsync(playerIndex, 0, EnumRoles.user);
    }

    /// <summary>
    /// Generates a signed JWT for the given player index by resolving full on-chain metadata.
    /// </summary>
    /// <param name="playerIndex">The unique on-chain player index.</param>
    /// <param name="refreshCount">The number of times the token has been refreshed.</param>
    /// <param name="enumRoles">The roles to assign to the JWT.</param>
    private async Task<string?> GenerateJwtFromPlayerIndexAsync(uint playerIndex, int refreshCount = 0,
        EnumRoles enumRoles = EnumRoles.user)
    {
        try
        {
            _log.LogInformation("[JWT] Starting JWT generation for player index #{Index}, role: {Role}",
                playerIndex, enumRoles);

            // get player data from index
            _log.LogDebug("[JWT] Calling GetPlayerByIndex for index #{Index}", playerIndex);
            var (txHash, player) = await _storage.GetPlayerByIndex(playerIndex);

            _log.LogDebug(
                "[JWT] GetPlayerByIndex result - TxHash: {TxHash}, Player Address: {Address}, PlayerId: {PlayerId}",
                txHash ?? "null",
                player?.Address ?? "null",
                player?.PlayerId ?? "null");

            if (player == null)
            {
                _log.LogWarning("[JWT] Player object is null for index #{Index}", playerIndex);
                return null;
            }

            if (string.IsNullOrWhiteSpace(player.Address))
            {
                _log.LogWarning("[JWT] Failed to resolve player metadata for index #{Index} - Address is empty",
                    playerIndex);
                return null;
            }

            // Look-up the latest Application record ID
            ulong applicationId = 0;
            try
            {
                _log.LogDebug("[JWT] Getting latest application record for player #{Index}", playerIndex);
                var latestAppId = await _storage.GetLatestApplicationRecord();

                _log.LogDebug("[JWT] Latest application ID result: {AppId}",
                    latestAppId.HasValue ? latestAppId.Value.ToString() : "null");

                if (latestAppId.HasValue && latestAppId.Value != ulong.MaxValue)
                {
                    applicationId = latestAppId.Value;
                    _log.LogDebug("[JWT] Using latest application ID: {AppId}", applicationId);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[JWT] Failed to get latest application record for #{Index}, using default (0)",
                    playerIndex);
            }

            _log.LogDebug("[JWT] Building claims for player #{Index}, address: {Address}, appId: {AppId}",
                playerIndex, player.Address, applicationId);

            var claims = new List<Claim>
            {
                new("wallet", player.Address),
                new("playerIndex", playerIndex.ToString()),
                new("userId", player.PlayerId),
                new("application", _applicationName),
                new("applicationId", applicationId.ToString()),
                new("refreshCount", refreshCount.ToString()),
            };

            if (player.Identifiers != null)
            {
                _log.LogDebug("[JWT] Adding {Count} verified identifiers for player #{Index}",
                    player.Identifiers.Count, playerIndex);

                foreach (var (key, val) in player.Identifiers)
                {
                    claims.Add(new Claim("verifiedIdKey", key));
                    claims.Add(new Claim("verifiedIdValue", val));
                    _log.LogDebug("[JWT] Added identifier {Key}={Value}", key, val);
                }
            }
            else
            {
                _log.LogDebug("[JWT] No identifiers found for player #{Index}", playerIndex);
            }

            claims.Add(new Claim(ClaimTypes.Role, enumRoles.ToString()));
            _log.LogDebug("[JWT] Added role claim: {Role}", enumRoles);

            var token = new JwtSecurityTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Issuer = _applicationName,
                Audience = _applicationAudience,
                IssuedAt = DateTime.UtcNow,
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(_jwtKey),
                    SecurityAlgorithms.HmacSha256Signature)
            });

            var jwt = new JwtSecurityTokenHandler().WriteToken(token);
            _log.LogInformation("[JWT] Successfully issued token for #{Index} ({Wallet}) with appId={AppId}",
                playerIndex, player.Address, applicationId);
            return jwt;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[JWT] Failed to generate token for player #{Index}", playerIndex);
            return null;
        }
    }

    /// <summary>
    /// Validates app-level password (used for admin login and nonce generation).
    /// </summary>
    public bool IsApplicationPasswordValid(string password)
    {
        return IsPasswordValid(password, _createPlayerAnonymousRequestPassword);
    }

    /// <summary>
    /// Gets the Ethereum wallet from a JWT token.
    /// </summary>
    public string? GetWalletFromToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        try
        {
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(_jwtKey),
                ClockSkew = TimeSpan.Zero
            }, out _);

            return principal.FindFirst("wallet")?.Value;
        }
        catch
        {
            return null;
        }
    }

    #endregion


    #region Helpers

    private bool IsPasswordValid(string password, string passwordToCheck)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(passwordToCheck))
            return false;

        // Use secure comparison
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(password),
            Encoding.UTF8.GetBytes(passwordToCheck));
    }

    /// <summary>
    /// Fetches and caches the JWKS used to verify Sequence-issued JWTs.
    /// </summary>
    /// <param name="forceRefresh">If true, bypasses the cache and fetches fresh JWKS</param>
    private async Task<JsonWebKeySet?> GetCachedSequenceJwksAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedJwks != null && DateTime.UtcNow - _jwksLastFetched < _jwksCacheDuration)
        {
            _log.LogInformation("[AuthSequenceJWT] Using cached JWKS.");
            return _cachedJwks;
        }

        try
        {
            var http = _httpClientFactory.CreateClient();
            var keys = await http.GetFromJsonAsync<JsonWebKeySet>(
                "https://waas.sequence.app/.well-known/jwks.json");

            if (keys != null && keys.Keys.Count > 0)
            {
                _cachedJwks = keys;
                _jwksLastFetched = DateTime.UtcNow;
                _log.LogInformation("[AuthSequenceJWT] JWKS downloaded and cached.");
            }

            return _cachedJwks;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[AuthSequenceJWT] JWKS fetch failed.");
            return null;
        }
    }

    public Task<bool> AllowServerSidePlayerCreation()
    {
        return Task.FromResult(_serverSidePlayerCreationAllowed);
    }

    public async Task<string?> AuthenticateSteamTicketAsync(string sessionTicket, string sessionUsername)
    {
        if (_enableDebugLogging)
            _log.LogInformation("[AuthSequenceJWT] Verifying Steam Ticket for {Username}...", sessionUsername);

        string url = $"https://api.steampowered.com/ISteamUserAuth/AuthenticateUserTicket/v1/" +
                     $"?key={_steamKey}&appid={_steamAppid}&ticket={sessionTicket}";

        var httpClient = _httpClientFactory.CreateClient();

        try
        {
            var response = await httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("Steam ticket check failed for {Username}. Status: {StatusCode}", sessionUsername,
                    response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.TryGetProperty("response", out var responseElement) &&
                responseElement.TryGetProperty("params", out var paramsElement) &&
                paramsElement.TryGetProperty("steamid", out var steamId))
            {
                string? steamIdStr = steamId.GetString();
                if (string.IsNullOrEmpty(steamIdStr))
                {
                    _log.LogWarning("Steam ticket missing steamid for {Username}", sessionUsername);
                    return null;
                }

                _log.LogInformation("Steam ticket verified for {Username}. SteamID: {SteamID}", sessionUsername,
                    steamIdStr);
                return steamIdStr;
            }

            _log.LogWarning("Steam ticket invalid or missing steamid for {Username}. Raw JSON: {Json}", sessionUsername,
                json);
            return null;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error verifying Steam session ticket for {Username}", sessionUsername);
            return null;
        }
    }


    public async Task<(string? Jwt, SequenceJwtAuthResult Status)> AuthenticateWithSequenceJwtAsync(string sequenceJwt)
    {
        if (_enableDebugLogging)
            _log.LogInformation("[AuthSequenceJWT] Verifying Sequence JWT...");

        var handler = new JwtSecurityTokenHandler();
        JwtSecurityToken? jwtToken;

        try
        {
            jwtToken = handler.ReadJwtToken(sequenceJwt);
            var jwtKid = jwtToken.Header.Kid;

            _log.LogInformation("[AuthSequenceJWT] JWT header kid: {Kid}", jwtKid);
            _log.LogInformation("[AuthSequenceJWT] Raw JWT length: {Length}", sequenceJwt.Length);

            var preview = sequenceJwt?.Length > 50 ? sequenceJwt.Substring(0, 50) : sequenceJwt ?? "null";
            _log.LogInformation("[AuthSequenceJWT] Raw JWT preview: {Preview}...", preview);

            var jwks = await GetCachedSequenceJwksAsync(forceRefresh: true);
            if (jwks == null || jwks.Keys.Count == 0)
            {
                _log.LogWarning("[AuthSequenceJWT] Failed to fetch JWKS keys");
                return (null, SequenceJwtAuthResult.AddressNotFound);
            }

            foreach (var key in jwks.Keys)
            {
                _log.LogInformation("[JWKS] Available key: {Kid}", key.Kid);
            }

            if (!jwks.Keys.Any(k => k.Kid == jwtKid))
            {
                _log.LogWarning("[AuthSequenceJWT] JWKS does not contain expected kid: {Kid}", jwtKid);
                return (null, SequenceJwtAuthResult.InvalidSignature);
            }

            ClaimsPrincipal principal;
            try
            {
                principal = handler.ValidateToken(sequenceJwt, new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = "https://waas.sequence.app",
                    ValidateAudience = true,
                    ValidAudience = $"https://sequence.build/project/{_sequenceProjectId}",
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = jwks.Keys,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(5)
                }, out _);
            }
            catch (SecurityTokenExpiredException ex)
            {
                _log.LogWarning(ex, "[AuthSequenceJWT] JWT has expired");
                return (null, SequenceJwtAuthResult.Expired);
            }
            catch (SecurityTokenInvalidSignatureException ex)
            {
                _log.LogWarning(ex, "[AuthSequenceJWT] JWT signature is invalid");
                return (null, SequenceJwtAuthResult.InvalidSignature);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[AuthSequenceJWT] JWT validation failed");
                return (null, SequenceJwtAuthResult.InvalidAddress);
            }

            // Step 1: Extract wallet from jwtToken.Subject
            string? wallet = jwtToken.Subject;
            if (_enableDebugLogging)
                _log.LogInformation("[AuthSequenceJWT] Extracted wallet from jwtToken.Subject: {Wallet}", wallet);

            // Step 2: fallback to identity.sub only if wallet is not valid
            if (string.IsNullOrEmpty(wallet) ||
                !AddressUtil.Current.IsValidAddressLength(wallet) ||
                !AddressUtil.Current.IsChecksumAddress(wallet))
            {
                _log.LogWarning("[AuthSequenceJWT] Wallet from 'sub' is invalid: {Wallet}", wallet);

                if (jwtToken.Payload.TryGetValue("https://waas.sequence.app/identity", out var identityObj) &&
                    identityObj is JsonElement identityJson &&
                    identityJson.ValueKind == JsonValueKind.Object &&
                    identityJson.TryGetProperty("sub", out var subProp))
                {
                    var fallbackWallet = subProp.GetString();

                    if (!string.IsNullOrEmpty(fallbackWallet))
                    {
                        _log.LogInformation("[AuthSequenceJWT] Parsed fallback wallet: {FallbackWallet}", fallbackWallet);

                        if (AddressUtil.Current.IsValidAddressLength(fallbackWallet) &&
                            AddressUtil.Current.IsChecksumAddress(fallbackWallet))
                        {
                            wallet = fallbackWallet;
                        }
                        else
                        {
                            _log.LogWarning("[AuthSequenceJWT] Fallback wallet is also invalid: {FallbackWallet}", fallbackWallet);
                        }
                    }
                }
            }

            // Step 3: validate final wallet
            if (string.IsNullOrEmpty(wallet) ||
                !AddressUtil.Current.IsValidAddressLength(wallet) ||
                !AddressUtil.Current.IsChecksumAddress(wallet))
            {
                _log.LogWarning("[AuthSequenceJWT] Final wallet address is invalid: {Wallet}", wallet);
                return (null, SequenceJwtAuthResult.InvalidAddress);
            }

            uint playerIndex = await _storage.GetPlayerIndexByAddress(wallet);
            if (playerIndex == uint.MaxValue)
            {
                _log.LogWarning("[AuthSequenceJWT] Wallet not registered: {Wallet}", wallet);
                return (null, SequenceJwtAuthResult.AddressNotFound);
            }

            var jwt = await GenerateJwtFromPlayerIndexAsync(playerIndex);
            if (jwt == null)
            {
                _log.LogError("[AuthSequenceJWT] Failed to generate platform JWT for playerIndex: {PlayerIndex}", playerIndex);
                return (null, SequenceJwtAuthResult.JWTFailed);
            }

            if (_enableDebugLogging)
                _log.LogInformation("[AuthSequenceJWT] Success. Wallet: {Wallet}, PlayerIndex: {PlayerIndex}", wallet, playerIndex);

            return (jwt, SequenceJwtAuthResult.Success);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[AuthSequenceJWT] Unexpected error during Sequence JWT authentication");
            return (null, SequenceJwtAuthResult.JWTFailed);
        }
    }

    #endregion
}