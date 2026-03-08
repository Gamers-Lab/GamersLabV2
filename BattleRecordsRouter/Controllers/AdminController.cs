using BattleRecordsRouter.Controllers.Settings;
using BattleRecordsRouter.Helper.Blockchain.Response;
using BattleRecordsRouter.Models;
using BattleRecordsRouter.Services;
using Microsoft.AspNetCore.Mvc;
using BattleRecordsRouter.Siwe.Authorisation;
using Microsoft.AspNetCore.Authorization;
using Nethereum.Siwe;

namespace BattleRecordsRouter.Controllers;

[ApiController]
[OnlyInEnvironment("EnableAdminEndpoints")]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly IGamersLabStorageService _storageService;
    private readonly ILogger<BlockchainStorageController> _logger;
  //  private readonly SiweMessageService _siweMessageService;
    private readonly IGamersLabAuthorisationService _gamersLabAuthorisationService;

    public AdminController(
        IGamersLabStorageService storageService,
        ILogger<BlockchainStorageController> logger,
      //  SiweMessageService siweMessageService,
        IGamersLabAuthorisationService gamersLabAuthorisationService)
    {
        _storageService = storageService;
        _logger = logger;
      //  _siweMessageService = siweMessageService;
        _gamersLabAuthorisationService = gamersLabAuthorisationService;
    }

    #region Admin Functions

    /// <summary>
    /// Pauses all contract operations (admin only) 🔑
    /// </summary>
    /// <returns>Transaction hash if successful.</returns>
    [HttpPost("admin/pause"), Authorize(Roles = "admin")]
    [Tags("Admin Management")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> PauseContract()
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.Pause(HttpContext);
            return ApiResponseHelper.TransactionOrError(txHash, _logger, "PauseContract");
        }, _logger, "PauseContract");
    }

    /// <summary>
    /// Unpauses all contract operations (admin only) 🔑
    /// </summary>
    /// <returns>Transaction hash if successful.</returns>
    [HttpPost("admin/unpause"), Authorize(Roles = "admin")]
    [Tags("Admin Management")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnpauseContract()
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.Unpause(HttpContext);
            return ApiResponseHelper.TransactionOrError(txHash, _logger, "UnpauseContract");
        }, _logger, "UnpauseContract");
    }

    /// <summary>
    /// Checks if the contract is currently paused (admin only) 🔑
    /// </summary>
    /// <returns>True if paused, false otherwise.</returns>
    [HttpGet("admin/paused"), AllowAnonymous]
    [Tags("Admin Management")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public async Task<IActionResult> IsPaused()
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var result = await _storageService.Paused();
            return ApiResponseHelper.ViewOrError(result, _logger, "IsPaused");
        }, _logger, "IsPaused");
    }

    /// <summary>
    /// Bans a player from participating (admin only) 🔑
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - Player must exist.
    /// - Player must not already be banned.
    /// </remarks>
    /// <param name="playerIndex">Index of the player to ban.</param>
    /// <returns>Transaction hash if successful.</returns>
    [HttpPost("admin/player/{playerIndex}/ban"), Authorize(Roles = "admin")]
    [Tags("Admin Management")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> BanPlayer(uint playerIndex)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.BanPlayer(HttpContext, playerIndex);
            return ApiResponseHelper.TransactionOrError(txHash, _logger, "BanPlayer");
        }, _logger, "BanPlayer");
    }

    /// <summary>
    /// Unbans a previously banned player (admin only) 🔑
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - Player must currently be banned.
    /// </remarks>
    /// <param name="playerIndex">Index of the player to unban.</param>
    /// <returns>Transaction hash if successful.</returns>
    [HttpPost("admin/player/{playerIndex}/unban"), Authorize(Roles = "admin")]
    [Tags("Admin Management")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnbanPlayer(uint playerIndex)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.UnbanPlayer(HttpContext, playerIndex);
            return ApiResponseHelper.TransactionOrError(txHash, _logger, "UnbanPlayer");
        }, _logger, "UnbanPlayer");
    }

    /// <summary>
    /// Gets the banned player index at a specific array position.
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - Index must be valid within the banned players array.
    /// </remarks>
    /// <param name="index">Array position in the banned list.</param>
    /// <returns>The player index that is banned.</returns>
    [HttpGet("admin/player/banned/{index}"), AllowAnonymous]
    [Tags("Admin Management")]
    [ProducesResponseType(typeof(uint), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlayerBannedByIndex(uint index)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var result = await _storageService.GetPlayerBannedByIndex(index);
            return ApiResponseHelper.ViewOrError(result, _logger, "GetPlayerBannedByIndex");
        }, _logger, "GetPlayerBannedByIndex");
    }

    /// <summary>
    /// Retrieves the full list of banned player indices.
    /// </summary>
    /// <returns>List of banned player indices.</returns>
    [HttpGet("admin/player/banned"), AllowAnonymous]
    [Tags("Admin Management")]
    [ProducesResponseType(typeof(List<uint>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllBannedPlayers()
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var result = await _storageService.GetAllBannedPlayers();
            return ApiResponseHelper.ViewOrError(result, _logger, "GetAllBannedPlayers");
        }, _logger, "GetAllBannedPlayers");
    }

    /// <summary>
    /// Checks if a player is currently banned.
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - Player index must exist.
    /// </remarks>
    /// <param name="playerIndex">The player index to check.</param>
    /// <returns>True if player is banned; false otherwise.</returns>
    [HttpGet("admin/player/{playerIndex}/is-banned"), AllowAnonymous]
    [Tags("Admin Management")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public async Task<IActionResult> IsPlayerBanned(uint playerIndex)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var result = await _storageService.IsPlayerBanned(playerIndex);
            return ApiResponseHelper.ViewOrError(result, _logger, "IsPlayerBanned");
        }, _logger, "IsPlayerBanned");
    }

    /// <summary>
    /// Grants moderator role to an account (admin only) 🔑
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - Account must be a valid Ethereum address.
    /// </remarks>
    /// <param name="account">Ethereum address to grant MODERATOR_ROLE to.</param>
    /// <returns>Transaction hash if successful.</returns>
    [HttpPost("admin/moderator/{account}/grant"), Authorize(Roles = "admin")]
    [Tags("Admin Management")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GrantModerator(string account)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.GrantModerator(HttpContext, account);
            return ApiResponseHelper.TransactionOrError(txHash, _logger, "GrantModerator");
        }, _logger, "GrantModerator");
    }

    /// <summary>
    /// Revokes moderator role from an account (admin only) 🔑
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - Account must currently have MODERATOR_ROLE.
    /// </remarks>
    /// <param name="account">Ethereum address to revoke MODERATOR_ROLE from.</param>
    /// <returns>Transaction hash if successful.</returns>
    [HttpPost("admin/moderator/{account}/revoke"), Authorize(Roles = "admin")]
    [Tags("Admin Management")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeModerator(string account)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.RevokeModerator(HttpContext, account);
            return ApiResponseHelper.TransactionOrError(txHash, _logger, "RevokeModerator");
        }, _logger, "RevokeModerator");
    }

    /// <summary>
    /// Checks if an account has a specific on-chain role.
    /// </summary>
    /// <remarks>
    /// - Role string must be valid.
    /// - Account must be a valid Ethereum address.
    /// </remarks>
    /// <param name="role">The role to check (e.g., MODERATOR_ROLE).</param>
    /// <param name="account">Ethereum address to check.</param>
    /// <returns>True if account has the role, otherwise false.</returns>
    [HttpGet("admin/role/{role}/{account}"), AllowAnonymous]
    [Tags("Admin Management")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public async Task<IActionResult> HasRole(string role, string account)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var result = await _storageService.HasRole(role, account);
            return ApiResponseHelper.ViewOrError(result, _logger, "HasRole");
        }, _logger, "HasRole");
    }

    /// <summary>
    /// Grants a custom role to an account (admin only) 🔑
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - Role must be a valid bytes32 identifier.
    /// </remarks>
    /// <param name="role">The role identifier to grant.</param>
    /// <param name="account">Ethereum address to grant the role to.</param>
    /// <returns>Transaction hash if successful.</returns>
    [HttpPost("admin/role/{role}/{account}/grant"), Authorize(Roles = "admin")]
    [Tags("Admin Management")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GrantRole(string role, string account)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.GrantRole(HttpContext, role, account);
            return ApiResponseHelper.TransactionOrError(txHash, _logger, "GrantRole");
        }, _logger, "GrantRole");
    }

    /// <summary>
    /// Revokes a custom role from an account (admin only) 🔑
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - Role must be a valid bytes32 identifier.
    /// </remarks>
    /// <param name="role">The role identifier to revoke.</param>
    /// <param name="account">Ethereum address to revoke the role from.</param>
    /// <returns>Transaction hash if successful.</returns>
    [HttpPost("admin/role/{role}/{account}/revoke"), Authorize(Roles = "admin")]
    [Tags("Admin Management")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeRole(string role, string account)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.RevokeRole(HttpContext, role, account);
            return ApiResponseHelper.TransactionOrError(txHash, _logger, "RevokeRole");
        }, _logger, "RevokeRole");
    }

    #endregion

    #region Application Management

    /// <summary>
    /// Sets the name of the application (admin only) 🔑
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - Name must be a nonempty string (validated on-chain).
    /// - Name must be less than 512 characters.
    /// </remarks>
    /// <param name="request">
    /// Contains:
    /// - Name: New application name.
    /// </param>
    /// <returns>A transaction hash if successful; otherwise a 500 error.</returns>
    [HttpPost("application/name"), Authorize(Roles = "admin")]
    [Tags("Application Management")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetApplication([FromBody] SetApplicationRequest request)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.SetApplication(HttpContext, request.Name);
            return ApiResponseHelper.TransactionOrError(txHash, _logger, "SetApplication");
        }, _logger, "SetApplication");
    }

    /// <summary>
    /// Gets the current application details.
    /// </summary>
    /// <remarks>
    /// - Returns the current application name and owner address.
    /// - No special input or rules.
    /// </remarks>
    /// <returns>The application details (name and owner address).</returns>
    [HttpGet("application/name"), AllowAnonymous]
    [Tags("Application Management")]
    [ProducesResponseType(typeof(ApplicationResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetApplication()
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var (_, application) = await _storageService.GetApplication();
            return ApiResponseHelper.ViewOrError(application, _logger, "GetApplication");
        }, _logger, "GetApplication");
    }

    /// <summary>
    /// Creates a new application record (version, company, contracts) (admin only) 🔑
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - Version and Company must be nonempty strings.
    /// - Version and Company must each be less than 512 characters.
    /// - ContractAddresses must not exceed 99 addresses.
    /// - Each contract address must be nonzero.
    /// </remarks>
    /// <param name="request">
    /// Contains:
    /// - ApplicationVersion: Application version string.
    /// - CompanyName: Company name string.
    /// - ContractAddresses: List of associated contract addresses.
    /// </param>
    /// <returns>A transaction hash and the created record ID.</returns>
    [HttpPost("application/record"), Authorize(Roles = "admin")]
    [Tags("Application Management")]
    [ProducesResponseType(typeof(TransactionWithIdResponse<ulong>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateApplicationRecord([FromBody] CreateApplicationRecordRequest request)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var (txHash, recordId) = await _storageService.CreateApplicationRecord(
                HttpContext,
                request.ApplicationVersion,
                request.CompanyName,
                request.ContractAddresses);

            var response = new TransactionWithIdResponse<ulong>
            {
                TransactionHash = txHash,
                Id = recordId
            };

            return ApiResponseHelper.TransactionOrError(txHash, response, _logger, "CreateApplicationRecord");
        }, _logger, "CreateApplicationRecord");
    }

    /// <summary>
    /// Sets metadata for an existing application record (admin only) 🔑
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - RecordId must refer to an existing application record.
    /// - Key and Value must be nonempty strings.
    /// - Key and Value must each be less than 512 characters.
    /// - Cannot exceed 99 metadata keys or 99 values per key.
    /// - Cannot duplicate existing value under a key.
    /// </remarks>
    /// <param name="request">
    /// Contains:
    /// - RecordId: The ID of the application record.
    /// - Key: Metadata key.
    /// - Value: Metadata value.
    /// </param>
    /// <returns>A transaction hash and the record ID updated.</returns>
    [HttpPost("application/record/metadata"), Authorize(Roles = "admin")]
    [Tags("Application Management")]
    [ProducesResponseType(typeof(TransactionWithIdResponse<ulong>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetApplicationRecordMetadata(
        [FromBody] SetApplicationRecordMetadataRequest request)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.SetApplicationRecordMetadata(
                HttpContext,
                request.RecordId,
                request.Key,
                request.Value);

            return ApiResponseHelper.TransactionOrError(txHash, _logger, "SetApplicationRecordMetadata");
        }, _logger, "SetApplicationRecordMetadata");
    }

    /// <summary>
    /// Updates the list of smart contract addresses associated (admin only) 🔑
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - RecordId must refer to an existing application record.
    /// - ContractAddresses must not exceed 99 addresses.
    /// - ContractAddresses list must not be empty.
    /// - All contract addresses must be nonzero.
    /// </remarks>
    /// <param name="request">
    /// Contains:
    /// - RecordId: ID of the application record.
    /// - ContractAddresses: Updated list of contract addresses.
    /// </param>
    /// <returns>A transaction hash and the updated record ID.</returns>
    [HttpPost("application/record/addresses"), Authorize(Roles = "admin")]
    [Tags("Application Management")]
    [ProducesResponseType(typeof(TransactionWithIdResponse<ulong>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateApplicationRecordAddresses(
        [FromBody] UpdateApplicationRecordAddressesRequest request)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var txHash = await _storageService.UpdateApplicationRecordAddresses(
                HttpContext,
                request.RecordId,
                request.ContractAddresses);

            return ApiResponseHelper.TransactionOrError(txHash, _logger, "UpdateApplicationRecordAddresses");
        }, _logger, "UpdateApplicationRecordAddresses");
    }

    /// <summary>
    /// Gets the ID of the most recently created application record.
    /// </summary>
    /// <returns>The latest application record ID.</returns>
    [HttpGet("application/record/latest"), AllowAnonymous]
    [Tags("Application Management")]
    [ProducesResponseType(typeof(ulong), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLatestApplicationRecord()
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            ulong? recordId = await _storageService.GetLatestApplicationRecord();

            return ApiResponseHelper.ViewOrError((recordId == ulong.MaxValue) ? null : recordId,
                _logger,
                "GetLatestApplicationRecord"
            );
        }, _logger, "GetLatestApplicationRecord");
    }

    /// <summary>
    /// Gets an application record by its record ID.
    /// </summary>
    /// <remarks>
    /// Rules:
    /// - RecordId must refer to an existing application record.
    /// </remarks>
    /// <param name="recordId">
    /// The record ID of the application record to retrieve.
    /// </param>
    /// <returns>Full application record details including version, company, contracts, and metadata.</returns>
    [HttpGet("application/record/{recordId:long}"), AllowAnonymous]
    [Tags("Application Management")]
    [ProducesResponseType(typeof(ApplicationRecordResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetApplicationRecord(ulong recordId)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            (_, var record) = await _storageService.GetApplicationRecord(recordId);

            return ApiResponseHelper.ViewOrError(record, _logger, "GetApplicationRecord");
        }, _logger, "GetApplicationRecord");
    }

    #endregion
}