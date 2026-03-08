using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Nethereum.Util;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BattleRecordsRouter.Controllers;
using BattleRecordsRouter.Helper.Blockchain.Response;
using BattleRecordsRouter.Services;
using BattleRecordsRouter.Repositories;
using BattleRecordsRouter.Siwe.Authorisation;

namespace BattleRecordsRouter.Siwe.Controllers;

/// <summary>
/// Handles authentication flows for both users (Ethereum wallets) and administrators (password-based).
/// Issues JWTs after verification.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly IGamersLabAuthorisationService _auth;
    private static readonly AddressUtil _addr = new();
    private readonly IGamersLabStorageService _storageService;
    private readonly ILogger<BlockchainStorageController> _logger;
    private readonly IErrorLogDBServices _errorLogService;

    public AuthController(
        IGamersLabAuthorisationService auth,
        IGamersLabStorageService storageService,
        ILogger<BlockchainStorageController> logger,
        IErrorLogDBServices errorLogService)
    {
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _storageService = storageService;
        _logger = logger;
        _errorLogService = errorLogService;
    }

    /* ════════════════════════  USER FLOW  ════════════════════════ */

    /// <summary>Refresh the JWT for the current caller. Requires valid token.</summary>
    [Tags("Auth Generic")]
    [HttpPost("wallet/request/refresh-token"), Authorize]
    public async Task<IActionResult> RefreshToken()
    {
        var token = Request.Headers.Authorization.ToString()["Bearer ".Length..];
        var newJwt = await _auth.RefreshAccessTokenAsync(token);
        return newJwt is null
            ? Unauthorized("Invalid or expired token. Re-authentication required.")
            : Ok(new { jwt = newJwt });
    }
    

    /// <summary>Accepts a Sequence-issued JWT and issues platform-specific JWT. Wallet V3</summary>
    [Tags("Auth Alternative")]
    [HttpPost("wallet/login/sequence-jwt"), AllowAnonymous]
    public async Task<IActionResult> AuthenticateViaSequenceJwtV3([FromBody] SequenceJwtRequest dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Jwt))
            return ApiResponseHelper.ValidationError("Sequence JWT is required", _logger, "AuthenticateViaSequenceJwtV3", HttpContext, _errorLogService);

        var (jwt, status) = await _auth.AuthenticateWithSequenceJwtAsync(dto.Jwt);

        return status switch
        {
            GamersLabAuthorisationService.SequenceJwtAuthResult.Success =>
                Ok(new { jwt }),

            GamersLabAuthorisationService.SequenceJwtAuthResult.InvalidAddress =>
                ApiResponseHelper.AuthenticationError("The wallet address from the token is invalid", _logger, "AuthenticateViaSequenceJwtV3", HttpContext, _errorLogService),

            GamersLabAuthorisationService.SequenceJwtAuthResult.AddressNotFound =>
                ApiResponseHelper.NotFoundError("Player", "wallet address", _logger, "AuthenticateViaSequenceJwtV3", HttpContext, _errorLogService),

            GamersLabAuthorisationService.SequenceJwtAuthResult.Banned =>
                ApiResponseHelper.AuthorizationError("This wallet is banned from accessing the platform", _logger, "AuthenticateViaSequenceJwtV3", HttpContext, _errorLogService),

            GamersLabAuthorisationService.SequenceJwtAuthResult.JWTFailed =>
                new ObjectResult(new { error = "Internal server error", details = "An error occurred while generating your platform JWT" })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                },

            GamersLabAuthorisationService.SequenceJwtAuthResult.Expired =>
                ApiResponseHelper.AuthenticationError("The Sequence JWT has expired", _logger, "AuthenticateViaSequenceJwtV3", HttpContext, _errorLogService),

            GamersLabAuthorisationService.SequenceJwtAuthResult.InvalidSignature =>
                ApiResponseHelper.AuthenticationError("The Sequence JWT signature is invalid", _logger, "AuthenticateViaSequenceJwtV3", HttpContext, _errorLogService),

            _ =>
                ApiResponseHelper.AuthenticationError("Authentication failed", _logger, "AuthenticateViaSequenceJwtV3", HttpContext, _errorLogService)
        };
    }

    [Tags("Auth Alternative")]
    [HttpPost("wallet/login/steam"), AllowAnonymous]
    public async Task<IActionResult> AuthenticateViaSteam([FromBody] SteamRequest dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Ticket))
            return ApiResponseHelper.ValidationError("Steam ticket is required", _logger, "AuthenticateViaSteam", HttpContext, _errorLogService);

        if (string.IsNullOrWhiteSpace(dto.Username))
            return ApiResponseHelper.ValidationError("Username is required", _logger, "AuthenticateViaSteam", HttpContext, _errorLogService);

        string? jwt = await _auth.AuthenticateSteamTicketAsync(dto.Ticket, dto.Username);

        return string.IsNullOrWhiteSpace(jwt)
            ? ApiResponseHelper.AuthenticationError("Failed to authenticate with Steam", _logger, "AuthenticateViaSteam", HttpContext, _errorLogService)
            : Ok(new { jwt });
    }

    [Tags("Auth Alternative")]
    [HttpPost("wallet/login/serverside"), AllowAnonymous]
    public async Task<IActionResult> UnauthenticatedLogin([FromBody] UnauthenticatedRequest dto)
    {
        bool auth = await _auth.AuthenticateAccountCreationPassword(dto.GameVerificationPassword);
        if (!auth)
            return ApiResponseHelper.AuthenticationError("Invalid password", _logger, "UnauthenticatedLogin", HttpContext, _errorLogService);


        string ImmutablePlayerIdentifier = "ImmutablePlayerIdentifier";

        // Check if server side player access is allowed
        bool access = await _auth.AllowServerSidePlayerCreation();
        if (!access)
            return ApiResponseHelper.AuthorizationError("Server-side player creation is not allowed", _logger, "UnauthenticatedLogin", HttpContext, _errorLogService);

        // if both values are missing, then fail login
        if (string.IsNullOrWhiteSpace(dto.PlayerAddress) && string.IsNullOrWhiteSpace(dto.ImmutablePlayerIdentifier))
        {
            return ApiResponseHelper.ValidationError("Player address or immutable player identifier is required", _logger, "UnauthenticatedLogin", HttpContext, _errorLogService);
        }

        bool successLogin = false;
        var playerIndex = uint.MaxValue;

        //try immutable player identifier login first
        if (!string.IsNullOrWhiteSpace(dto.ImmutablePlayerIdentifier))
        {
            try
            {
                playerIndex =
                    await _storageService.GetPlayerByVerifiedId(ImmutablePlayerIdentifier,
                        dto.ImmutablePlayerIdentifier);
                if (playerIndex != uint.MaxValue)
                {
                    successLogin = true;
                }
            }
            catch (SmartContractRevertException ex) when (ex.Message.Contains("NotFound"))
            {
                successLogin = false;
            }
            catch (ArgumentException ex) when (ex.Message.Contains("NotFound"))
            {
                successLogin = false;
            }
        }

        //No successful login yet, try address login
        if (!successLogin && !string.IsNullOrWhiteSpace(dto.PlayerAddress))
        {
            // check can login
            var (ok, storageplayerIndex, message) =
                await _storageService.TryGetPlayerIndexByAddressAsync(dto.PlayerAddress);
            if (ok)
            {
                successLogin = true;
                playerIndex = storageplayerIndex;
            }
        }

        // login failed
        if (!successLogin)
            return ApiResponseHelper.NotFoundError("Player",
                dto.ImmutablePlayerIdentifier ?? dto.PlayerAddress ?? "unknown",
                _logger,
                "UnauthenticatedLogin",
                HttpContext,
                _errorLogService);

        string? jwt = await _auth.GenerateJwtFromPlayerIndexAsync(playerIndex);

        if (string.IsNullOrWhiteSpace(jwt))
        {
            return new ObjectResult(new
            {
                error = "Internal server error",
                details = "Failed to generate authentication token"
            })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }

        return Ok(new { jwt, playerIndex });
    }

    /// <summary>Login as administrator and issues platform-specific JWT.</summary>
    [Tags("Auth Alternative")]
    [HttpPost("wallet/login/admin"), AllowAnonymous]
    public async Task<IActionResult> LoginAdmin([FromBody] AdminLoginRequest dto)
    {
        var jwt = await _auth.AuthenticateAdminPassword(dto.Password);
        return string.IsNullOrWhiteSpace(jwt)
            ? Unauthorized("Bad password")
            : Ok(new { jwt });
    }


    /// <summary>Inspect current token and return wallet address or roles.</summary>
    [Tags("Auth Testing")]
    [HttpGet("ping/whoami"), Authorize]
    public IActionResult InspectJwt()
    {
        var hdr = Request.Headers.Authorization.ToString();
        if (!hdr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Unauthorized();

        var token = hdr["Bearer ".Length..];
        var wallet = _auth.GetWalletFromToken(token);

        if (wallet != null) return Ok(new { wallet, role = "user" });

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var roles = jwt.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToArray();
        return roles.Length > 0 ? Ok(new { roles }) : Unauthorized();
    }

    [Tags("Auth Testing")]
    [HttpPost("ping/anon"), AllowAnonymous]
    public IActionResult PingAnon() => Ok("pong");

    [Tags("Auth Testing")]
    [HttpPost("ping/user"), Authorize]
    public IActionResult PingUser() => Ok("pong");

    [Tags("Auth Testing")]
    [HttpPost("ping/admin"), Authorize(Roles = "admin")]
    public IActionResult PingAdmin() => Ok("pong");

    /* ════════════════════════  CLASSES  ════════════════════════ */

    public sealed class NonceRequest
    {
        [Required]
        [JsonPropertyName("address")]
        public string Address { get; init; } = string.Empty;

        [Required]
        [JsonPropertyName("password")]
        public string AppPassword { get; init; } = string.Empty;
    }

    public sealed class SequenceJwtRequest
    {
        [JsonPropertyName("jwt")]
        public string Jwt { get; init; } = string.Empty;
    }

    public sealed class SteamRequest
    {
        [JsonPropertyName("ticket")]
        public string Ticket { get; init; } = string.Empty;

        [JsonPropertyName("username")]
        public string Username { get; init; } = string.Empty;
    }

    public sealed class UnauthenticatedRequest
    {
        [JsonPropertyName("gameverificationpassword")]
        public string GameVerificationPassword { get; init; } = string.Empty;

        [JsonPropertyName("immutableplayeridentifier")]
        public string ImmutablePlayerIdentifier { get; set; } = string.Empty;

        [JsonPropertyName("playeraddress")]
        public string PlayerAddress { get; init; } = string.Empty;
    }

    public sealed class AdminLoginRequest
    {
        [Required]
        [JsonPropertyName("password")]
        public string Password { get; init; } = string.Empty;
    }

    public sealed class SignatureSubmission
    {
        [Required]
        [JsonPropertyName("address")]
        public string Address { get; init; } = string.Empty;

        [Required]
        [JsonPropertyName("signature")]
        public string Signature { get; init; } = string.Empty;
    }
}