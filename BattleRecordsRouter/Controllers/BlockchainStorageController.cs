using BattleRecordsRouter.Helper;
using BattleRecordsRouter.Helper.Blockchain.Response;
using BattleRecordsRouter.Models;
using BattleRecordsRouter.Repositories;
using BattleRecordsRouter.Services;
using Microsoft.AspNetCore.Mvc;
using BattleRecordsRouter.Siwe.Authorisation;
using Microsoft.AspNetCore.Authorization;
using RecordInfo = BattleRecordsRouter.Models.RecordInfo;

namespace BattleRecordsRouter.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BlockchainStorageController : ControllerBase
{
    private readonly IGamersLabStorageService _storageService;

    private readonly ILogger<BlockchainStorageController> _logger;

    // private readonly SiweMessageService _siweMessageService;
    private readonly IGamersLabAuthorisationService _auth;

    private readonly IErrorLogDBServices _errorLogService;

    private readonly IPlayerCredentialDBServices _playerCredentialService;

    public BlockchainStorageController(
        IGamersLabStorageService storageService,
        ILogger<BlockchainStorageController> logger,
        IGamersLabAuthorisationService auth,
        IErrorLogDBServices errorLogService,
        IPlayerCredentialDBServices playerCredentialService)
    {
        _storageService = storageService;
        _logger = logger;
        _auth = auth;
        _errorLogService = errorLogService;
        _playerCredentialService = playerCredentialService;
    }

    #region Record Management

    /// <summary>
    /// Submits a new gameplay record (e.g., achievement, score, stat) for a player 🔒
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - If matchSessionId is 0, it will be set to a non-match/level record.
    /// - Key and Value must be nonempty strings and each under 512 characters.
    /// - Cannot exceed system-wide maximums for records per player.
    /// - OtherPlayers array is optional but must contain valid player indices.
    /// </remarks>
    /// <param name="request">
    /// Includes:
    /// - MatchSessionId: ID of match session the player belongs to.
    /// - Score: Numeric score achieved.
    /// - Key: Record category (e.g., "HighScore", "FastestTime").
    /// - Value: Record value string.
    /// </param>
    /// <returns>Transaction hash if successful.</returns>
    [HttpPost("records/match/add"), Authorize]
    [Tags("Record Management")]
    [ProducesResponseType(typeof(TransactionWithIdResponse<ulong>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetRecordMatch([FromBody] SetRecordRequest request)
    {
        return await SetRecord(request, false);
    }

    /// <summary>
    /// Submits a new gameplay record for a player that is not associated with a match session (e.g., menu, level) 🔒
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - This endpoint always sets matchSessionId to 0 (non-match/level record).
    /// - Key and Value must be nonempty strings and each under 512 characters.
    /// - Cannot exceed system-wide maximums for records per player.
    /// - OtherPlayers array is optional but must contain valid player indices.
    /// </remarks>
    /// <param name="request">
    /// Includes:
    /// - Score: Numeric score achieved.
    /// - Key: Record category (e.g., "HighScore", "FastestTime").
    /// - Value: Record value string.
    /// </param>
    /// <returns>Transaction hash if successful.</returns>
    [HttpPost("records/menu/add"), Authorize]
    [Tags("Record Management")]
    [ProducesResponseType(typeof(TransactionWithIdResponse<ulong>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetRecordMenu([FromBody] SetRecordRequest request)
    {
        return await SetRecord(request, true);
    }

    /// <summary>
    /// Private helper method to handle record creation for both match and menu contexts
    /// </summary>
    private async Task<IActionResult> SetRecord(SetRecordRequest request, bool isMenuRecord)
    {
        // Extract required claims from JWT
        if (!JwtClaimsHelper.TryExtractRequiredClaims(HttpContext, _logger, out uint _,
                out ulong applicationId, out IActionResult errorResult))
        {
            return errorResult;
        }

        // Resolve player index with admin override support
        if (!JwtClaimsHelper.TryResolvePlayerIndex(HttpContext, _logger, request.PlayerIndex,
                out uint playerIndexValue, out errorResult))
        {
            return errorResult;
        }

        var startTime = request.StartTime ?? GetNowUnixTimestamp();
        var otherPlayers = request.OtherPlayers ?? [];

        // For menu records, always use matchSessionId = 0
        uint matchSessionId = isMenuRecord ? 0 : request.MatchSessionId;

        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.SetRecord(
                HttpContext,
                matchSessionId,
                playerIndexValue,
                request.Score,
                request.Key,
                request.Value,
                otherPlayers,
                startTime);

            string operation = isMenuRecord ? "SetRecordMenu" : "SetRecordMatch";
            return ApiResponseHelper.TransactionOrError(txHash, _logger, operation);
        }, _logger, isMenuRecord ? "SetRecordMenu" : "SetRecordMatch", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Batch submits records for a player that is in a match session 🔒
    /// </summary>
    /// <remarks>
    /// **Player Index Security:**
    /// - **Non-Admin Users**: PlayerIndex is automatically set from JWT token. Any provided PlayerIndex values in the request are ignored.
    /// - **Admin Users**: Must explicitly provide PlayerIndex for each record in the batch. Missing PlayerIndex will result in a 400 validation error.
    ///
    /// This allows admins to submit batch records for multiple players while ensuring non-admin users can only submit records for themselves.
    /// </remarks>
    [HttpPost("records/match/batch"), Authorize]
    [Tags("Record Management")]
    [ProducesResponseType(typeof(BatchRecordResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> BatchSetRecordsMatch([FromBody] BatchSetRecordsRequest request)
    {
        _logger.LogInformation("BatchSetRecordsMatch called");
        _logger.LogInformation("Request: {Request}", request);
        return await BatchSetRecords(request, false);
    }

    /// <summary>
    /// Batch submits records for a player that is not associated with a match session (e.g., menu, level) 🔒
    /// </summary>
    /// <remarks>
    /// **Player Index Security:**
    /// - **Non-Admin Users**: PlayerIndex is automatically set from JWT token. Any provided PlayerIndex values in the request are ignored.
    /// - **Admin Users**: Must explicitly provide PlayerIndex for each record in the batch. Missing PlayerIndex will result in a 400 validation error.
    ///
    /// This allows admins to submit batch records for multiple players while ensuring non-admin users can only submit records for themselves.
    /// </remarks>
    [HttpPost("records/menu/batch"), Authorize]
    [Tags("Record Management")]
    [ProducesResponseType(typeof(BatchRecordResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> BatchSetRecordsMenu([FromBody] BatchSetRecordsRequest request)
    {
        return await BatchSetRecords(request, true);
    }

    /// <summary>
    /// Private helper method to handle batch record creation for both match and menu contexts
    /// </summary>
    private async Task<IActionResult> BatchSetRecords([FromBody] BatchSetRecordsRequest request, bool isMenuRecords)
    {
        if (!JwtClaimsHelper.TryExtractRequiredClaims(HttpContext, _logger, out uint jwtPlayerIndex,
                out ulong applicationId, out IActionResult errorResult))
        {
            return errorResult;
        }

        // ✅ Materialize enumerable to a list
        var records = request.Records?.ToList() ?? new List<SetRecordRequest>();

        if (records.Count == 0)
        {
            _logger.LogWarning("BatchSetRecords received an empty record list");
            return ApiResponseHelper.ValidationError("No records provided for batch submission", _logger,
                isMenuRecords ? "BatchSetRecordsMenu" : "BatchSetRecordsMatch", HttpContext, _errorLogService);
        }

        var currentTime = GetNowUnixTimestamp();
        string operation = isMenuRecords ? "BatchSetRecordsMenu" : "BatchSetRecordsMatch";
        bool isAdmin = JwtClaimsHelper.IsAdmin(HttpContext);

        // Security: Validate player index for each record
        // Non-admin: Always use JWT player index (ignore any provided value)
        // Admin: Must explicitly provide player index for each record
        if (isAdmin)
        {
            // Admin must provide playerIndex for each record
            var missingPlayerIndex = records.Where(r => !r.PlayerIndex.HasValue).ToList();
            if (missingPlayerIndex.Any())
            {
                _logger.LogWarning(
                    "Admin user must provide playerIndex for all records in batch. {Count} records missing playerIndex",
                    missingPlayerIndex.Count);

                return ApiResponseHelper.ValidationError(
                    "Administrators must explicitly provide playerIndex for each record in the batch",
                    _logger,
                    operation,
                    HttpContext,
                    _errorLogService);
            }

            _logger.LogInformation(
                "Admin batch submission: Processing {Count} records with explicit player indices",
                records.Count);
        }
        else
        {
            // Non-admin: Force all records to use JWT player index (silently override any provided values)
            _logger.LogInformation(
                "Non-admin batch submission: All {Count} records will use JWT playerIndex={JwtIndex}",
                records.Count,
                jwtPlayerIndex);
        }

        _logger.LogInformation("Received {Count} records in batch for {Operation}", records.Count, operation);
        for (int i = 0; i < records.Count; i++)
        {
            var r = records[i];
            // For menu records, always use matchSessionId = 0
            uint matchSessionId = isMenuRecords ? 0 : r.MatchSessionId;
            // Non-admin: always use JWT playerIndex; Admin: use provided playerIndex
            uint effectivePlayerIndex = isAdmin ? r.PlayerIndex!.Value : jwtPlayerIndex;

            _logger.LogInformation(
                "[Record {Index}] matchSessionId={MatchSessionId}, playerIndex={PlayerIndex}, score={Score}, key={Key}, value={Value}, startTime={StartTime}, otherPlayers=[{OtherPlayers}]",
                i,
                matchSessionId,
                effectivePlayerIndex,
                r.Score,
                r.Key,
                r.Value,
                r.StartTime ?? currentTime,
                string.Join(",", r.OtherPlayers ?? Array.Empty<uint>()));
        }

        var inputs = records.Select(r => new RecordInput
        {
            MatchSessionId = isMenuRecords ? 0 : r.MatchSessionId,
            // Non-admin: force JWT playerIndex; Admin: use provided playerIndex (already validated as non-null)
            PlayerIndex = isAdmin ? r.PlayerIndex!.Value : jwtPlayerIndex,
            Score = r.Score,
            Key = r.Key,
            Value = r.Value,
            OtherPlayers = r.OtherPlayers ?? Array.Empty<uint>(),
            StartTime = r.StartTime ?? currentTime
        }).ToArray();

        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.BatchSetRecords(HttpContext, inputs);

            return ApiResponseHelper.TransactionOrError(txHash, _logger, operation);
        }, _logger, operation, HttpContext, _errorLogService);
    }

    /// <summary>
    /// Retrieves a gameplay record by its unique record index 👁️
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - RecordId must exist.
    /// </remarks>
    /// <param name="recordId">The record's array index (RecordId).</param>
    /// <returns>Structured record entry containing key, value, score, and related player information.</returns>
    [HttpGet("records/{recordId}"), AllowAnonymous]
    [Tags("Record Management")]
    [ProducesResponseType(typeof(RecordInfo), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecord(uint recordId)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var result = await _storageService.GetRecordsByIndex(recordId);
            return ApiResponseHelper.ViewOrError(result, _logger, "GetRecordByIndex");
        }, _logger, "GetRecordByIndex", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Retrieves all gameplay record IDs created by a specific player 👁️
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - PlayerIndex must exist.
    /// </remarks>
    /// <param name="playerIndex">Index of the player whose records are requested.</param>
    /// <returns>Array of record IDs belonging to the player.</returns>
    [HttpGet("records/player/{playerIndex}"), AllowAnonymous]
    [Tags("Record Management")]
    [ProducesResponseType(typeof(uint[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecordsByPlayerIndex(uint playerIndex)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var result = await _storageService.GetRecordIdsByPlayer(playerIndex);
            return ApiResponseHelper.ViewOrError(result, _logger, "GetRecordsByPlayerIndex");
        }, _logger, "GetRecordsByPlayerIndex", HttpContext, _errorLogService);
    }

    #endregion

    #region Player Management

    /// <summary>
    /// Creates a new player (human or AI controlled) 🔒
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - PlayerId (string) must be nonempty and unique.
    /// - PlayerType must be a valid enum (Human, NPC).
    /// - PlayerAddress must be nonzero.
    /// - PlayerId must be less than 512 characters.
    /// - Caller must have MODERATOR_ROLE on-chain.
    /// </remarks>
    /// <param name="request">
    /// Includes:
    /// - PlayerId: Unique player identifier (string).
    /// - PlayerAddress: Ethereum address linked to player.
    /// </param>
    /// <returns>Transaction hash and created player index.</returns>
    [HttpPost("players/add"), Authorize]
    [Tags("Player Management")]
    [ProducesResponseType(typeof(CreatePlayerResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreatePlayer([FromBody] CreatePlayerRequest request)
    {
        var playerType = PlayerType.Human;

        return await ApiResponseHelper.HandleSafe(async () =>
        {
            try
            {
                var (txHash, playerIndex) = await _storageService.CreatePlayer(
                    HttpContext,
                    request.PlayerId,
                    playerType,
                    request.PlayerAddress);

                var response = new CreatePlayerResponse
                {
                    TransactionHash = txHash,
                    PlayerIndex = playerIndex
                };

                return ApiResponseHelper.TransactionOrError(txHash, response, _logger, "CreatePlayer");
            }
            catch (SmartContractRevertException ex) when (ex.Message.Contains("AlreadyExists"))
            {
                _logger.LogInformation("Player already exists: PlayerId={PlayerId}, Address={Address}",
                    request.PlayerId, request.PlayerAddress);
                return Conflict("Player already exists");
            }
        }, _logger, "CreatePlayer", HttpContext, _errorLogService);
    }

    [HttpPost("players/add/anon"), AllowAnonymous]
    [Tags("Player Management")]
    [ProducesResponseType(typeof(CreatePlayerResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreatePlayerAnon([FromBody] CreatePlayerAnonymousRequest request)
    {
        bool auth = await _auth.AuthenticateAccountCreationPassword(request.SignInPassword);

        if (!auth)
            return ApiResponseHelper.AuthenticationError("Invalid password", _logger, "CreatePlayerAnon", HttpContext, _errorLogService);

        var playerType = PlayerType.Human;

        return await ApiResponseHelper.HandleSafe(async () =>
        {
            try
            {
                var (txHash, playerIndex) = await _storageService.CreatePlayer(
                    HttpContext,
                    request.PlayerId,
                    playerType,
                    request.PlayerAddress);

                var response = new CreatePlayerResponse
                {
                    TransactionHash = txHash,
                    PlayerIndex = playerIndex
                };

                return ApiResponseHelper.TransactionOrError(txHash, response, _logger, "CreatePlayer");
            }
            catch (SmartContractRevertException ex) when (ex.Message.Contains("AlreadyExists"))
            {
                _logger.LogInformation("Player already exists: PlayerId={PlayerId}, Address={Address}",
                    request.PlayerId, request.PlayerAddress);
                return Conflict("Player already exists");
            }
        }, _logger, "CreatePlayer", HttpContext, _errorLogService);
    }

    [HttpPost("players/add/serverside"), AllowAnonymous]
    [Tags("Player Management")]
    [ProducesResponseType(typeof(CreatePlayerResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreatePlayerServerSide([FromBody] CreatePlayerServerSideRequest request)
    {
        string ImmutablePlayerIdentifier = "ImmutablePlayerIdentifier";

        bool access = await _auth.AllowServerSidePlayerCreation();

        if (!access)
            return ApiResponseHelper.AuthorizationError("Server-side player creation is not allowed", _logger, "CreatePlayerServerSide", HttpContext, _errorLogService);

        // 1. Check for valid password
        bool auth = await _auth.AuthenticateAccountCreationPassword(request.GameVerificationPassword);
        if (!auth)
            return ApiResponseHelper.AuthenticationError("Invalid password", _logger, "CreatePlayerServerSide", HttpContext, _errorLogService);

        var playerType = PlayerType.Human;

        // 2. check if player with same immutable player identifier already exists
        try
        {
            var playerIndex =
                await _storageService.GetPlayerByVerifiedId(ImmutablePlayerIdentifier,
                    request.ImmutablePlayerIdentifier);
            if (playerIndex != uint.MaxValue)
                return ApiResponseHelper.ConflictError("Player with this identifier already exists", _logger, "CreatePlayerServerSide", HttpContext, _errorLogService);
        }
        catch (SmartContractRevertException ex) when (ex.Message.Contains("NotFound"))
        {
            // This is expected - the player doesn't exist yet, which is what we want
            _logger.LogDebug("Player identifier not found, which is expected for new player creation");
        }
        catch (ArgumentException ex) when (ex.Message.Contains("NotFound"))
        {
            // This is expected - the player doesn't exist yet, which is what we want
            _logger.LogDebug("Player identifier not found, which is expected for new player creation");
        }

        // check if username exists
        try
        {
            var userIndex = await _storageService.GetPlayerIndex(request.PlayerUsername);
            if (userIndex != uint.MaxValue && userIndex != 0)
                return ApiResponseHelper.ConflictError("Player with this username already exists", _logger, "CreatePlayerServerSide", HttpContext, _errorLogService);
        }
        catch
        {
            // This is expected - the player doesn't exist yet, which is what we want
            _logger.LogDebug("Player username not found, which is expected for new player creation");
        }

        // 3. check if address is provided and if it's already in use
        var address = request.PlayerAddress;
        var key = string.Empty;

        // if address is provided, check if it's already in use
        if (!string.IsNullOrEmpty(address))
        {
            var success = await _storageService.TryGetPlayerIndexByAddressAsync(request.PlayerAddress);
            if (success.ok)
                return ApiResponseHelper.ConflictError("Address already in use", _logger, "CreatePlayerServerSide", HttpContext, _errorLogService);
        }
        // if no address is provided, generate a new one
        else
        {
            var (account, privateKey) = GenericWeb3Helper.GenerateNewAccount();
            address = account;
            key = privateKey;
        }

        // we now have a unique address and a unique player identifier

        // 4. if we generated a new address, store the private key
        if (!string.IsNullOrEmpty(key))
        {
            // Store the generated address and private key in the database
            try
            {
                await _playerCredentialService.CreateAsync(
                    address,
                    key,
                    request.ImmutablePlayerIdentifier);

                _logger.LogInformation("Stored new player credentials for {Identifier}",
                    request.ImmutablePlayerIdentifier);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store player credentials for {Identifier}",
                    request.ImmutablePlayerIdentifier);
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to store player credentials");
            }
        }

        return await ApiResponseHelper.HandleSafe(async () =>
            {
                // First create the player
                var (txHash, playerIndex) = await _storageService.CreatePlayer(
                    HttpContext,
                    request.PlayerUsername,
                    playerType,
                    address);

                // Then add the immutable identifier
                var txHash2 = await _storageService.AddPlayerIdentifier(
                    HttpContext,
                    playerIndex,
                    ImmutablePlayerIdentifier,
                    request.ImmutablePlayerIdentifier);

                // Only return success if both operations succeeded
                if (string.IsNullOrEmpty(txHash) || string.IsNullOrEmpty(txHash2))
                {
                    _logger.LogError(
                        "Failed to complete player creation process. CreatePlayer txHash: {TxHash}, AddPlayerIdentifier txHash: {TxHash2}",
                        txHash, txHash2);
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }

                var response = new CreatePlayerResponse
                {
                    TransactionHash = txHash,
                    PlayerIndex = playerIndex
                };

                return ApiResponseHelper.TransactionOrError(txHash, response, _logger, "CreatePlayerServerSide");
            }, _logger, "CreatePlayerServerSide", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Adds a verified wallet address to a player 🔒
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - Address must be nonzero and unique for player.
    /// - Maximum 99 verified addresses allowed per player.
    /// </remarks>
    /// <param name="request">
    /// Includes:
    /// - VerifiedAddress: New address to add.
    /// </param>
    /// <returns>Transaction hash and the added verified address.</returns>
    [HttpPost("players/add/address"), Authorize]
    [Tags("Player Management")]
    [ProducesResponseType(typeof(AddVerifiedAddressResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddVerifiedAddress([FromBody] AddVerifiedAddressRequest request)
    {
        // Extract required claims from JWT
        if (!JwtClaimsHelper.TryExtractRequiredClaims(HttpContext, _logger, out uint _,
                out ulong applicationId, out IActionResult errorResult))
        {
            return errorResult;
        }

        // Resolve player index with admin override support
        if (!JwtClaimsHelper.TryResolvePlayerIndex(HttpContext, _logger, request.PlayerIndex,
                out uint playerIndexValue, out errorResult))
        {
            return errorResult;
        }

        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.AddVerifiedAddress(
                HttpContext,
                playerIndexValue,
                request.VerifiedAddress);

            return ApiResponseHelper.TransactionOrError(txHash, _logger, "AddVerifiedAddress");
        }, _logger, "AddVerifiedAddress", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Updates a player's unique ID 🔒
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - PlayerIndex must refer to an existing player.
    /// - NewPlayerId must be nonempty and less than 512 characters.
    /// - NewPlayerId must not already be in use by another player.
    /// - Caller must have MODERATOR_ROLE on-chain.
    /// </remarks>
    /// <param name="request">
    /// Includes:
    /// - PlayerIndex: Index of the player to update.
    /// - NewPlayerId: New unique identifier for the player.
    /// </param>
    /// <returns>Transaction hash if successful.</returns>
    [HttpPost("players/update/id"), Authorize]
    [Tags("Player Management")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePlayerUniqueId([FromBody] UpdatePlayerUniqueIdRequest request)
    {
        // Extract required claims from JWT
        if (!JwtClaimsHelper.TryExtractRequiredClaims(HttpContext, _logger, out uint _,
                out ulong applicationId, out IActionResult errorResult))
        {
            return errorResult;
        }

        // Resolve player index with admin override support
        if (!JwtClaimsHelper.TryResolvePlayerIndex(HttpContext, _logger, request.PlayerIndex,
                out uint playerIndexValue, out errorResult))
        {
            return errorResult;
        }

        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.UpdatePlayerUniqueId(
                HttpContext,
                playerIndexValue,
                request.NewPlayerId);

            var response = new TransactionResponse { TransactionHash = txHash };
            return ApiResponseHelper.TransactionOrError(txHash, response, _logger, "UpdatePlayerUniqueId");
        }, _logger, "UpdatePlayerUniqueId", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Adds a custom verified identifier (e.g. Discord, Steam) to an existing player 🔒
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - IdentifierType and IdentifierValue must be nonempty strings.
    /// - PlayerIndex must refer to an existing player.
    /// - Maximum 99 verified identifiers allowed.
    /// - Adding the same IdentifierType overwrites the previous value.
    /// </remarks>
    /// <param name="request">
    /// Includes:
    /// - IdentifierType: Key type (e.g., "Discord").
    /// - IdentifierValue: Key value.
    /// </param>
    /// <returns>Transaction hash if successful.</returns>
    [HttpPost("players/add/identifier"), Authorize]
    [Tags("Player Management")]
    [ProducesResponseType(typeof(AddPlayerIdentifierResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddPlayerIdentifier([FromBody] AddPlayerIdentifierRequest request)
    {
        // Extract required claims from JWT
        if (!JwtClaimsHelper.TryExtractRequiredClaims(HttpContext, _logger, out uint _,
                out ulong applicationId, out IActionResult errorResult))
        {
            return errorResult;
        }

        // Resolve player index with admin override support
        if (!JwtClaimsHelper.TryResolvePlayerIndex(HttpContext, _logger, request.PlayerIndex,
                out uint playerIndexValue, out errorResult))
        {
            return errorResult;
        }

        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.AddPlayerIdentifier(
                HttpContext,
                playerIndexValue,
                request.IdentifierType,
                request.IdentifierValue);

            var response = new AddPlayerIdentifierResponse { TransactionHash = txHash };
            return ApiResponseHelper.TransactionOrError(txHash, response, _logger, "AddPlayerIdentifier");
        }, _logger, "AddPlayerIdentifier", HttpContext, _errorLogService);
    }

    [HttpPost("players/add/metadata"), Authorize]
    [Tags("Player Management")]
    [ProducesResponseType(typeof(PlayerMetadataRequest), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetPlayerMetadata([FromBody] PlayerMetadataRequest request)
    {
        // Extract required claims from JWT
        if (!JwtClaimsHelper.TryExtractRequiredClaims(HttpContext, _logger, out uint _,
                out ulong applicationId, out IActionResult errorResult))
        {
            return errorResult;
        }

        // Resolve player index with admin override support
        if (!JwtClaimsHelper.TryResolvePlayerIndex(HttpContext, _logger, request.PlayerIndex,
                out uint playerIndexValue, out errorResult))
        {
            return errorResult;
        }

        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.SetPlayerMetadata(
                HttpContext,
                playerIndexValue,
                request.Key,
                request.Value);

            return ApiResponseHelper.TransactionOrError(txHash, new TransactionResponse
            {
                TransactionHash = txHash,
            }, _logger, "SetPlayerMetadata");
        }, _logger, "SetPlayerMetadata", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Sets (overwrites) a player's total score 🔒
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - NewScore is an integer (can be negative or positive).
    /// </remarks>
    /// <param name="request">
    /// Includes:
    /// - NewScore: New score to set.
    /// </param>
    /// <returns>Transaction hash and updated player score.</returns>
    [HttpPost("players/set/score"), Authorize]
    [Tags("Player Management")]
    [ProducesResponseType(typeof(UpdatePlayerScoreResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePlayerScore([FromBody] UpdatePlayerScoreRequest request)
    {
        // Extract required claims from JWT
        if (!JwtClaimsHelper.TryExtractRequiredClaims(HttpContext, _logger, out uint _,
                out ulong applicationId, out IActionResult errorResult))
        {
            return errorResult;
        }

        // Resolve player index with admin override support
        if (!JwtClaimsHelper.TryResolvePlayerIndex(HttpContext, _logger, request.PlayerIndex,
                out uint playerIndexValue, out errorResult))
        {
            return errorResult;
        }

        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.UpdatePlayerScore(
                HttpContext,
                playerIndexValue,
                request.NewScore);

            return ApiResponseHelper.TransactionOrError(txHash, _logger, "UpdatePlayerScore");
        }, _logger, "UpdatePlayerScore", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Gets a player's index by their unique player ID 👁️
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - PlayerId must exist.
    /// </remarks>
    /// <param name="playerId">Unique string player ID.</param>
    /// <returns>Numeric player index.</returns>
    [HttpGet("players/lookup/index/{playerId}"), AllowAnonymous]
    [Tags("Player Management")]
    [ProducesResponseType(typeof(GetPlayerIndexResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlayerIndex(string playerId)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            try
            {
                var result = await _storageService.GetPlayerIndex(playerId);
                return ApiResponseHelper.ViewOrError(new GetPlayerIndexResponse { PlayerIndex = result }, _logger,
                    "GetPlayerIndex");
            }
            catch (SmartContractRevertException ex) when (ex.Message.Contains("NotFound"))
            {
                _logger.LogInformation("Player not found for playerId {PlayerId}", playerId);
                return ApiResponseHelper.NotFoundError("Player", playerId, _logger, "GetPlayerIndex", HttpContext, _errorLogService);
            }
        }, _logger, "GetPlayerIndex", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Gets a player's unique ID by their player index 👁️
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - PlayerIndex must exist.
    /// </remarks>
    /// <param name="playerIndex">Index of the player.</param>
    /// <returns>String unique ID associated with the player.</returns>
    [HttpGet("players/lookup/id/{playerIndex}"), AllowAnonymous]
    [Tags("Player Management")]
    [ProducesResponseType(typeof(GetPlayerUniqueIdResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlayerUniqueId(uint playerIndex)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            try
            {
                var result = await _storageService.GetPlayerUniqueId(playerIndex);
                return ApiResponseHelper.ViewOrError(new GetPlayerUniqueIdResponse { PlayerId = result }, _logger,
                    "GetPlayerUniqueId");
            }
            catch (SmartContractRevertException ex) when (ex.Message.Contains("IndexOutOfBounds"))
            {
                _logger.LogInformation("Player not found for playerIndex {PlayerIndex}", playerIndex);
                return ApiResponseHelper.NotFoundError("Player", playerIndex.ToString(), _logger, "GetPlayerUniqueId", HttpContext, _errorLogService);
            }
        }, _logger, "GetPlayerUniqueId", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Resolves a player index using a previously-registered verified identifier (e.g., SteamID) 👁️
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - The provided identifierType and identifierValue must exactly match a value
    /// - Returns NotFound if no player owns that identifier.
    /// </remarks>
    /// <param name="identifierType">The ID key (e.g. \"SteamID\").</param>
    /// <param name="identifierValue">The associated value (e.g. \"SuperCop\").</param>
    /// <returns>The player index associated with this identifier.</returns>
    [HttpGet("players/lookup/identifier/{identifierType}/{identifierValue}"), AllowAnonymous]
    [Tags("Player Management")]
    [ProducesResponseType(typeof(GetPlayerIndexResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlayerByVerifiedId(string identifierType, string identifierValue)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            try
            {
                uint index = await _storageService.GetPlayerByVerifiedId(identifierType, identifierValue);

                if (index == uint.MaxValue)
                {
                    _logger.LogInformation("Player not found for identifier {Type}:{Value}", identifierType,
                        identifierValue);
                    return NotFound(new
                        { message = $"No player found with identifier {identifierType}:{identifierValue}" });
                }

                var response = new GetPlayerIndexResponse { PlayerIndex = index };
                return ApiResponseHelper.ViewOrError(response, _logger, "GetPlayerByVerifiedId");
            }
            catch (ArgumentException ex) when (ex.Message.Contains("NotFound"))
            {
                _logger.LogInformation("Player not found for identifier {Type}:{Value}", identifierType,
                    identifierValue);
                return NotFound(new
                    { message = $"No player found with identifier {identifierType}:{identifierValue}" });
            }
        }, _logger, "GetPlayerByVerifiedId", HttpContext, _errorLogService);
    }


    /// <summary>
    /// Gets a player's index by their linked wallet address 👁️
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - Address must belong to a registered player.
    /// </remarks>
    /// <param name="address">The wallet address associated with a player.</param>
    /// <returns>Player index if found.</returns>
    [HttpGet("players/lookup/address/{address}"), AllowAnonymous]
    [Tags("Player Management")]
    [ProducesResponseType(typeof(GetPlayerIndexResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlayerByAddress(string address)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var (ok, index, errorMessage) = await _storageService.TryGetPlayerIndexByAddressAsync(address);

            if (!ok)
            {
                if (errorMessage.Contains("not registered") || errorMessage.Contains("Smart contract error"))
                {
                    _logger.LogInformation("Player not found for address {Address}: {Message}", address, errorMessage);
                    return ApiResponseHelper.NotFoundError("Player", address, _logger, "GetPlayerIndexByAddress", HttpContext, _errorLogService);
                }

                if (errorMessage.Contains("Invalid Ethereum address format"))
                {
                    _logger.LogInformation("Invalid address format for {Address}: {Message}", address, errorMessage);
                    return ApiResponseHelper.ValidationError("Invalid Ethereum address format", _logger, "GetPlayerIndexByAddress", HttpContext, _errorLogService);
                }

                _logger.LogError("Error looking up player by address {Address}: {Message}", address, errorMessage);
                return new ObjectResult(new
                {
                    error = "Internal server error",
                    details = "Error looking up player by address"
                })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }

            return Ok(new GetPlayerIndexResponse { PlayerIndex = index });
        }, _logger, "GetPlayerByAddress", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Gets full player details (profile) by their player index 👁️
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - PlayerIndex must exist.
    /// </remarks>
    /// <param name="playerIndex">Player index number.</param>
    /// <returns>Player details including IDs, address, score, verified identifiers, etc.</returns>
    [HttpGet("players/{playerIndex}"), AllowAnonymous]
    [Tags("Player Management")]
    [ProducesResponseType(typeof(PlayerApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlayerByIndex(uint playerIndex)
    {
        _logger.LogInformation("[API] GetPlayerByIndex called for index #{Index}", playerIndex);

        return await ApiResponseHelper.HandleSafe(async () =>
        {
            try
            {
                _logger.LogDebug("[API] Calling storage service GetPlayerByIndex for #{Index}", playerIndex);
                (string? txHash, PlayerResponse? player) = await _storageService.GetPlayerByIndex(playerIndex);

                _logger.LogDebug("[API] GetPlayerByIndex result - TxHash: {TxHash}, Player: {@Player}",
                    txHash ?? "null", player);

                if (player == null)
                {
                    _logger.LogWarning("[API] Player is null for index #{Index}", playerIndex);
                    return ApiResponseHelper.NotFoundError("Player", playerIndex.ToString(), _logger, "GetPlayerByIndex", HttpContext, _errorLogService);
                }

                if (string.IsNullOrEmpty(player.PlayerId) && string.IsNullOrEmpty(player.Address))
                {
                    _logger.LogWarning("[API] Player has empty ID and address for index #{Index}", playerIndex);
                    return ApiResponseHelper.NotFoundError("Player", playerIndex.ToString(), _logger, "GetPlayerByIndex", HttpContext, _errorLogService);
                }

                var apiResponse = new PlayerApiResponse
                {
                    PlayerId = player.PlayerId,
                    Address = player.Address,
                    PlayerType = player.PlayerType,
                    Score = player.Score.Sign < 0 || player.Score > ulong.MaxValue ? 0UL : (ulong)player.Score,
                    Identifiers = player.Identifiers,
                    VerifiedAddresses = player.VerifiedAddresses,
                    Metadata = player.Metadata
                };

                return ApiResponseHelper.ViewOrError(apiResponse, _logger, "GetPlayerByIndex");
            }
            catch (SmartContractRevertException ex) when (ex.Message.Contains("IndexOutOfBounds"))
            {
                _logger.LogInformation("[API] Player index #{Index} not found (IndexOutOfBounds)", playerIndex);
                return ApiResponseHelper.NotFoundError("Player", playerIndex.ToString(), _logger, "GetPlayerByIndex", HttpContext, _errorLogService);
            }
        }, _logger, "GetPlayerByIndex", HttpContext, _errorLogService);
    }


    /// <summary>
    /// Gets the total number of registered players 👁️
    /// </summary>
    /// <remarks>
    /// - No special rules.
    /// </remarks>
    /// <returns>Unsigned integer count of players.</returns>
    [HttpGet("players/count"), AllowAnonymous]
    [Tags("Player Management")]
    [ProducesResponseType(typeof(GetPlayerCountResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlayerCount()
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var count = await _storageService.GetPlayerCount();
            return ApiResponseHelper.ViewOrError(new GetPlayerCountResponse { Count = count }, _logger,
                "GetPlayerCount");
        }, _logger, "GetPlayerCount", HttpContext, _errorLogService);
    }

    #endregion

    #region Session Management

    /// <summary>
    /// Starts a new login session for a player 🔒
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - Device must be a valid enum (iOS, Android, WebGL, PC, Console, Other).
    /// </remarks>
    /// <param name="request">
    /// Includes:
    /// - Device: Device type (enum or integer).
    /// - StartTime: Start time of login (Unix timestamp).
    /// - PlayerIndex (optional): Index of the player. If null, it will be extracted from the JWT.
    /// - ApplicationId (optional): Application identifier. If null, it will be extracted from the JWT.
    /// </param>
    /// <returns>
    /// Returns the transaction hash and the newly created login session ID.
    /// </returns>
    [HttpPost("sessions/login/start"), Authorize]
    [Tags("Session Management")]
    [ProducesResponseType(typeof(CreateLoginSessionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateLoginSession([FromBody] LoginSessionRequest request)
    {
        // Extract from JWT
        if (!JwtClaimsHelper.TryExtractRequiredClaims(HttpContext, _logger, out uint _,
                out ulong jwtApplicationId, out IActionResult errorResult))
        {
            return errorResult;
        }

        // Resolve player index with admin override support
        if (!JwtClaimsHelper.TryResolvePlayerIndex(HttpContext, _logger, request.PlayerIndex,
                out uint playerIndex, out errorResult))
        {
            return errorResult;
        }

        var applicationId = request.ApplicationId ?? jwtApplicationId;
        var startTime = request.StartTime ?? GetNowUnixTimestamp();

        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var (txHash, sessionId) = await _storageService.LoginSessionCreate(
                HttpContext,
                playerIndex,
                applicationId,
                startTime,
                request.Device);

            return ApiResponseHelper.TransactionOrError(
                txHash,
                new CreateLoginSessionResponse { TransactionHash = txHash, SessionId = sessionId },
                _logger,
                "CreateLoginSession");
        }, _logger, "CreateLoginSession", HttpContext, _errorLogService);
    }

    private uint GetNowUnixTimestamp()
    {
        return (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }


    /// <summary>
    /// Ends a login session 🔒
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - SessionId must exist.
    /// </remarks>
    /// <param name="request">
    /// Includes:
    /// - SessionId: ID of the login session to end.
    /// </param>
    /// <returns>Transaction hash if successful.</returns>
    [HttpPost("sessions/login/end"), Authorize]
    [Tags("Session Management")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> EndLoginSession([FromBody] LoginSessionEndRequest request)
    {
        var time = request.EndTime ?? GetNowUnixTimestamp();

        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.LoginSessionEnd(HttpContext, request.SessionId, time);
            return ApiResponseHelper.TransactionOrError(txHash, _logger, "EndLoginSession");
        }, _logger, "EndLoginSession", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Adds a metadata key/value pair to a login session 🔒
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - SessionId must exist.
    /// - Key and Value must be nonempty and each less than 512 characters.
    /// - Cannot exceed 99 keys or 99 values per key.
    /// - Cannot duplicate an existing value under a key.
    /// </remarks>
    /// <param name="request">
    /// Includes:
    /// - SessionId: ID of the login session.
    /// - Key: Metadata key string.
    /// - Value: Metadata value string.
    /// </param>
    /// <returns>Transaction hash if successful.</returns>
    [HttpPost("sessions/login/add/metadata"), Authorize]
    [Tags("Session Management")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetLoginSessionMetadata([FromBody] LoginSessionMetadataRequest request)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.SetLoginSessionMetadata(
                HttpContext,
                request.SessionId,
                request.Key,
                request.Value);

            return ApiResponseHelper.TransactionOrError(txHash, _logger, "SetLoginSessionMetadata");
        }, _logger, "SetLoginSessionMetadata", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Gets detailed information about a specific login session PM 👁️
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - SessionId must exist.
    /// </remarks>
    /// <param name="sessionId">The ID of the login session to retrieve.</param>
    /// <returns>Login session info including base data, metadata, device type, and player index.</returns>
    [HttpGet("sessions/login/{sessionId:long}"), AllowAnonymous]
    [Tags("Session Management")]
    [ProducesResponseType(typeof(LoginSessionInfo), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLoginSessionById(ulong sessionId)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var result = await _storageService.GetLoginSessionById(sessionId);
            return ApiResponseHelper.ViewOrError(result, _logger, "GetLoginSessionById");
        }, _logger, "GetLoginSessionById", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Gets the total number of login sessions stored 👁️
    /// </summary>
    /// <returns>Unsigned integer representing total login sessions.</returns>
    [HttpGet("sessions/count"), AllowAnonymous]
    [Tags("Session Management")]
    [ProducesResponseType(typeof(uint), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLoginSessionCount()
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var result = await _storageService.GetLoginSessionCount();
            return ApiResponseHelper.ViewOrError(result, _logger, "GetLoginSessionCount");
        }, _logger, "GetLoginSessionCount", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Returns all player indices that currently have active login sessions 👁️
    /// </summary>
    /// <returns>List of active player indices.</returns>
    [HttpGet("sessions/playersactive"), AllowAnonymous]
    [Tags("Session Management")]
    [ProducesResponseType(typeof(ulong[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllActiveLoginPlayers()
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var result = await _storageService.GetAllActiveLoginPlayers();
            return ApiResponseHelper.ViewOrError(result, _logger, "GetAllActiveLoginPlayers");
        }, _logger, "GetAllActiveLoginPlayers", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Returns all currently active login session IDs 👁️
    /// </summary>
    /// <returns>List of active login session IDs.</returns>
    [HttpGet("sessions/sessionsactive"), AllowAnonymous]
    [Tags("Session Management")]
    [ProducesResponseType(typeof(ulong[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllActiveLoginSessions()
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var result = await _storageService.GetAllActiveLoginSessionIds();
            return ApiResponseHelper.ViewOrError(result, _logger, "GetAllActiveLoginSessions");
        }, _logger, "GetAllActiveLoginSessions", HttpContext, _errorLogService);
    }

    #endregion

    #region Match Session Management

    /// <summary>
    /// Starts a new match session multiplayer 🔒
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - At least 1 player must be provided.
    /// - No duplicate player indices allowed.
    /// - Level string must not be empty and must be less than 512 characters.
    /// - PlayerIds must refer to valid players.
    /// </remarks>
    /// <param name="request">
    /// Contains:
    /// - StartTime: Start time of match (unix timestamp).
    /// - PlayerIds: List of player indices joining.
    /// - Level: Level or map name.
    /// </param>
    /// <returns>Transaction hash and newly created match session ID.</returns>
    [HttpPost("match/sessions/start/multiplayer"), Authorize]
    [Tags("Match Session Management")]
    [ProducesResponseType(typeof(CreateMatchSessionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> StartMatchSessionMultiplayer([FromBody] StartMatchSessionRequest request)
    {
        // Extract required claims from JWT
        if (!JwtClaimsHelper.TryExtractRequiredClaims(HttpContext, _logger, out uint playerIndex,
                out ulong applicationId, out IActionResult errorResult))
        {
            return errorResult;
        }

        uint[] playerIndexValue = request.PlayerIds ?? [playerIndex];
        var applicationIdValue = request.ApplicationId ?? applicationId;
        var startTime = request.StartTime ?? GetNowUnixTimestamp();

        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var (txHash, matchSessionId, playDataIds) = await _storageService.StartMatchSession(
                HttpContext,
                applicationIdValue,
                startTime,
                true,
                playerIndexValue,
                request.Level);

            var response = new CreateMatchSessionResponse
            {
                TransactionHash = txHash,
                MatchSessionId = matchSessionId,
                PlayDataIds = playDataIds
            };

            return ApiResponseHelper.TransactionOrError(txHash, response, _logger, "StartMatchSessionMultiplayer");
        }, _logger, "StartMatchSessionMultiplayer", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Starts a new match session single-player 🔒
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - Level string must not be empty and must be less than 512 characters.
    /// </remarks>
    /// <param name="request">
    /// Contains:
    /// - Level: Level or map name.
    /// </param>
    /// <returns>Transaction hash and newly created match session ID.</returns>
    [HttpPost("match/sessions/start/singleplayer"), Authorize]
    [Tags("Match Session Management")]
    [ProducesResponseType(typeof(CreateMatchSessionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> StartMatchSessionSinglePlayer([FromBody] StartMatchSessionRequest request)
    {
        // Extract required claims from JWT
        if (!JwtClaimsHelper.TryExtractRequiredClaims(HttpContext, _logger, out uint playerIndex,
                out ulong applicationId, out IActionResult errorResult))
        {
            return errorResult;
        }

        // ──────── added: log the raw claims and request values ────────
        _logger.LogDebug(
            "StartMatchSessionSinglePlayer - Claims: playerIndex={PlayerIdx}, applicationIdClaim={AppIdClaim}; " +
            "Request overrides: applicationId={ReqAppId}, playerIds={ReqPlayers}",
            playerIndex,
            applicationId,
            request.ApplicationId?.ToString() ?? "null",
            request.PlayerIds is null ? "null" : string.Join(",", request.PlayerIds));
        // ───────────────────────────────────────────────────────────────

        uint[] playerIndexValue = request.PlayerIds ?? [playerIndex];
        var applicationIdValue = request.ApplicationId ?? applicationId;
        var startTime = request.StartTime ?? GetNowUnixTimestamp();

        // ──────── added: log the final values that will be sent ────────
        _logger.LogDebug(
            "StartMatchSessionSinglePlayer - Using applicationId={AppId}, startTime={Start}, multiplayer={Mp}, " +
            "playerIds=[{Players}], level=\"{Level}\"",
            applicationIdValue,
            startTime,
            false,
            string.Join(",", playerIndexValue),
            request.Level);
        // ───────────────────────────────────────────────────────────────

        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var (txHash, matchSessionId, playDataIds) = await _storageService.StartMatchSession(
                HttpContext,
                applicationIdValue,
                startTime,
                false,
                playerIndexValue,
                request.Level);

            var response = new CreateMatchSessionResponse
            {
                TransactionHash = txHash,
                MatchSessionId = matchSessionId,
                PlayDataIds = playDataIds
            };

            return ApiResponseHelper.TransactionOrError(txHash, response, _logger, "StartMatchSessionSinglePlayer");
        }, _logger, "StartMatchSessionSinglePlayer", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Ends a single match session 🔒
    /// </summary>
    [HttpPost("match/sessions/singleplayer/end"), Authorize]
    [Tags("Match Session Management")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> EndMatchSessionSinglePlayer(
        [FromBody] EndMatchSessionSingleplayer requestMultiplayer)
    {
        var endTime = requestMultiplayer.EndTime ?? GetNowUnixTimestamp();

        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.EndMatchSession(
                HttpContext,
                requestMultiplayer.MatchSessionId,
                endTime);
            return ApiResponseHelper.TransactionOrError(txHash, _logger, "EndMatchSessionSinglePlayer");
        }, _logger, "EndMatchSessionSinglePlayer", HttpContext, _errorLogService);
    }


    /// <summary>
    /// Ends a multiplayer match session 🔒
    /// </summary>
    [HttpPost("match/sessions/multiplayer/end"), Authorize]
    [Tags("Match Session Management")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> EndMatchSessionMultiplayer(
        [FromBody] EndMatchSessionRequestMultiplayer requestMultiplayer)
    {
        var endTime = requestMultiplayer.EndTime ?? GetNowUnixTimestamp();

        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.EndMatchSession(
                HttpContext,
                requestMultiplayer.MatchSessionId,
                endTime);
            return ApiResponseHelper.TransactionOrError(txHash, _logger, "EndMatchSessionMultiplayer");
        }, _logger, "EndMatchSessionMultiplayer", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Adds a metadata key/value pair to a match session 🔒
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - MatchSessionId must exist.
    /// - Match session must still be active (not ended).
    /// - Key and Value must be nonempty and less than 512 characters.
    /// - Cannot exceed 99 keys or 99 values per key.
    /// - Cannot duplicate an existing value for a key.
    /// - Caller must have MODERATOR_ROLE on-chain.
    /// </remarks>
    /// <param name="request">
    /// Contains:
    /// - MatchSessionId: Match session ID.
    /// - Key: Metadata key string.
    /// - Value: Metadata value string.
    /// </param>
    /// <returns>Transaction hash if successful.</returns>
    [HttpPost("match/sessions/add/metadata"), Authorize]
    [Tags("Match Session Management")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetMatchSessionMetadata([FromBody] MatchSessionMetadataRequest request)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.SetMatchSessionMetadata(
                HttpContext,
                request.MatchSessionId,
                request.Key,
                request.Value);

            return ApiResponseHelper.TransactionOrError(txHash, _logger, "SetMatchSessionMetadata");
        }, _logger, "SetMatchSessionMetadata", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Gets all active match session IDs for a given player 👁️
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - PlayerIndex must refer to an existing player.
    /// </remarks>
    /// <param name="playerIndex">The index of the player to query.</param>
    /// <returns>List of match session IDs where the player is active.</returns>
    [HttpGet("match/sessions/active/{playerIndex}"), AllowAnonymous]
    [Tags("Match Session Management")]
    [ProducesResponseType(typeof(ActiveMatchSessionsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveMatchSessions(uint playerIndex)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var result = await _storageService.GetActiveMatchSessions(playerIndex);
            return ApiResponseHelper.ViewOrError(new ActiveMatchSessionsResponse { SessionIds = result }, _logger,
                "GetActiveMatchSessions");
        }, _logger, "GetActiveMatchSessions", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Gets the total number of match sessions in the system 👁️
    /// </summary>
    /// <remarks>
    /// - No special rules.
    /// </remarks>
    /// <returns>Count of match sessions created (unsigned integer).</returns>
    [HttpGet("match/sessions/count"), AllowAnonymous]
    [Tags("Match Session Management")]
    [ProducesResponseType(typeof(ActiveMatchSessionsLengthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveMatchSessionsLength()
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var result = await _storageService.GetActiveMatchSessionsLength();
            return ApiResponseHelper.ViewOrError(new ActiveMatchSessionsLengthResponse { Length = result }, _logger,
                "GetActiveMatchSessionsLength");
        }, _logger, "GetActiveMatchSessionsLength", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Gets all active match session IDs across all players 👁️
    /// </summary>
    /// <remarks>
    /// - No special rules.
    /// </remarks>
    /// <returns>List of all currently active match session IDs.</returns>
    [HttpGet("match/sessions/all"), AllowAnonymous]
    [Tags("Match Session Management")]
    [ProducesResponseType(typeof(ActiveMatchSessionsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllActiveMatchSessions()
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var result = await _storageService.GetAllActiveMatchSessions();
            return ApiResponseHelper.ViewOrError(new ActiveMatchSessionsResponse { SessionIds = result }, _logger,
                "GetAllActiveMatchSessions");
        }, _logger, "GetAllActiveMatchSessions", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Gets a match session using its index in the sessions array 👁️
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - Index must exist within the match sessions array.
    /// </remarks>
    /// <param name="index">Array index of the match session.</param>
    /// <returns>Match session information including base data, players, metadata, and level name.</returns>
    [HttpGet("match/sessions/index/{index}"), AllowAnonymous]
    [Tags("Match Session Management")]
    [ProducesResponseType(typeof(MatchSessionInfo), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMatchSessionByIndex(uint index)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var session = await _storageService.GetMatchSessionByIndex(index);
            return ApiResponseHelper.ViewOrError(session, _logger, "GetMatchSessionByIndex");
        }, _logger, "GetMatchSessionByIndex", HttpContext, _errorLogService);
    }

    #endregion

    #region Play Data Management

    /// <summary>
    /// Adds an additional player to an existing match session 🔒
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - The match session must exist.
    /// - The match session must still be active (not ended).
    /// - The match session must not be full (maximum players reached).
    /// </remarks>
    /// <param name="request">
    /// Includes:
    /// - MatchSessionId: ID of the match session.
    /// </param>
    /// <returns>A transaction hash and newly created PlayDataId.</returns>
    [HttpPost("match/multiplayer-player/add"), Authorize]
    [Tags("Play Data Management")]
    [ProducesResponseType(typeof(PlayDataResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddPlayerToMatchSession([FromBody] AddPlayerToMatchSessionRequest request)
    {
        // Extract required claims from JWT
        if (!JwtClaimsHelper.TryExtractRequiredClaims(HttpContext, _logger, out uint _,
                out ulong applicationId, out IActionResult errorResult))
        {
            return errorResult;
        }

        // Resolve player index with admin override support
        if (!JwtClaimsHelper.TryResolvePlayerIndex(HttpContext, _logger, request.PlayerIndex,
                out uint playerIndexValue, out errorResult))
        {
            return errorResult;
        }

        var starTime = request.StartTime ?? GetNowUnixTimestamp();

        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.AddPlayerToMatchSession(
                HttpContext,
                request.MatchSessionId,
                playerIndexValue,
                starTime);

            return ApiResponseHelper.TransactionOrError(txHash, _logger, "AddPlayerToMatchSession");
        }, _logger, "AddPlayerToMatchSession", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Ends play data for a player in a  match session and records final stats 🔒
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - The play data record must exist.
    /// - Score, Win/Loss, and Match Position will be updated.
    /// - Caller must have MODERATOR_ROLE on-chain.
    /// </remarks>
    /// <param name="request">
    /// Includes:
    /// - MatchSessionId: ID of the match session.
    /// - PlayerIndex: Index of the player.
    /// - EndTime: When the player finished the match.
    /// - Score: Final score for the player.
    /// - WinLoss: Result (Win/Loss/Forfeit).
    /// - MatchPosition: Placement in match.
    /// </param>
    /// <returns>A transaction hash and updated PlayDataId.</returns>
    [HttpPost("match/player/end"), Authorize]
    [Tags("Play Data Management")]
    [ProducesResponseType(typeof(PlayDataResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePlayDataForMatchSession([FromBody] UpdatePlayDataRequest request)
    {
        // Extract required claims from JWT
        if (!JwtClaimsHelper.TryExtractRequiredClaims(HttpContext, _logger, out uint _,
                out ulong applicationId, out IActionResult errorResult))
        {
            return errorResult;
        }

        // Resolve player index with admin override support
        if (!JwtClaimsHelper.TryResolvePlayerIndex(HttpContext, _logger, request.PlayerIndex,
                out uint playerIndexValue, out errorResult))
        {
            return errorResult;
        }

        var endTime = request.EndTime ?? GetNowUnixTimestamp();

        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.UpdatePlayDataForMatchSession(
                HttpContext,
                request.MatchSessionId,
                playerIndexValue,
                endTime,
                request.Score,
                request.WinLoss,
                request.MatchPosition);

            return ApiResponseHelper.TransactionOrError(txHash, _logger, "UpdatePlayDataForMatchSession");
        }, _logger, "UpdatePlayDataForMatchSession", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Retrieves detailed information about a specific play data record 👁️
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - PlayDataIndex must exist.
    /// </remarks>
    /// <param name="playDataIndex">
    /// Index of the play data record to fetch.
    /// </param>
    /// <returns>Detailed PlayDataInfo including player stats, metadata, and session information.</returns>
    [HttpGet("match/playdata/index/{playDataIndex}"), AllowAnonymous]
    [Tags("Play Data Management")]
    [ProducesResponseType(typeof(PlayDataInfo), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlayDataByIndex(uint playDataIndex)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var result = await _storageService.GetPlayDataByIndex(playDataIndex);
            return ApiResponseHelper.ViewOrError(result, _logger, "GetPlayDataByIndex");
        }, _logger, "GetPlayDataByIndex", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Gets the total number of play data records currently stored 👁️
    /// </summary>
    /// <remarks>
    /// - No special rules.
    /// - Returns the total play data count as an uint.
    /// </remarks>
    /// <returns>Unsigned integer representing the total play data count.</returns>
    [HttpGet("match/playdata/count"), AllowAnonymous]
    [Tags("Play Data Management")]
    [ProducesResponseType(typeof(uint), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlayDataCount()
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var result = await _storageService.GetPlayDataCount();
            return ApiResponseHelper.ViewOrError(result, _logger, "GetPlayDataCount");
        }, _logger, "GetPlayDataCount", HttpContext, _errorLogService);
    }

    /// <summary>
    /// Finds the play data ID for a player in a match session 👁️
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - Match session ID must exist.
    /// - Player must be a participant in the match session.
    /// </remarks>
    /// <param name="matchSessionId">The match session ID.</param>
    /// <param name="playerIndex">The player index.</param>
    /// <returns>The play data ID for the player in the match.</returns>
    [HttpGet("match/playdata/find/{matchSessionId}/{playerIndex}"), AllowAnonymous]
    [Tags("Play Data Management")]
    [ProducesResponseType(typeof(PlayDataIdResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> FindPlayDataId(uint matchSessionId, uint playerIndex)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var playDataId = await _storageService.FindPlayDataId(matchSessionId, playerIndex);
            return ApiResponseHelper.ViewOrError(new PlayDataIdResponse { PlayDataId = playDataId }, _logger,
                "FindPlayDataId");
        }, _logger, "FindPlayDataId", HttpContext, _errorLogService);
    }

    #endregion

    /// <summary>
    /// Gets the current Unix timestamp.
    /// </summary>
    /// <returns>Current Unix timestamp as uint.</returns>
    [HttpGet("diagnostics/time"), AllowAnonymous]
    [ProducesResponseType(typeof(uint), StatusCodes.Status200OK)]
    public IActionResult GetCurrentTime()
    {
        return Ok(GetNowUnixTimestamp());
    }
}