using BattleRecordsRouter.Config;
using BattleRecordsRouter.Helper;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace BattleRecordsRouter.Services;

/// <summary>
/// Admin-only operations for the on-chain storage contract.
/// </summary>
public partial class GamersLabStorageService
{
/* ─────────────────────────── Pausing ─────────────────────────── */

    /// <summary>
    /// @notice Pause all state-changing functions in <c>OnChainDataStorage</c>.
    /// @dev Emits Paused(address). Returns tx hash only.
    /// </summary>
    public Task<string> Pause(HttpContext httpContext) =>
        ContractUtils.SafeWriteSendAsync<PauseFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn => { }, // no extra props
            httpContext: httpContext,
            logger: _logger,
            operationName: nameof(Pause),
            loggingService: _loggingService
        );

    /// <summary>
    /// @notice Lift the global pause.
    /// @dev Emits Unpaused(address). Returns tx hash only.
    /// </summary>
    public Task<string> Unpause(HttpContext httpContext) =>
        ContractUtils.SafeWriteSendAsync<UnpauseFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn => { }, // no extra props
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(Unpause),
            loggingService: _loggingService
        );


    /// <summary>
    /// @notice Read the paused flag from the contract (read-only).
    /// </summary>
    public Task<bool> Paused() =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                var c = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var fn = c.GetFunction("paused");
                return await fn.CallAsync<bool>();
            },
            defaultValue: false,
            _logger,
            nameof(Paused),
            loggingService: _loggingService
        );

/* ───────────────────── Player bans ───────────────────── */

    /// <summary>
    /// @notice Permanently ban a player from recording new matches.
    /// @dev Emits PlayerBanned(uint256). Returns tx hash only.
    /// </summary>
    public Task<string> BanPlayer(HttpContext httpContext, uint playerIndex) =>
        ContractUtils.SafeWriteSendAsync<BanPlayerFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn => fn.PlayerIndex = playerIndex,
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(BanPlayer),
            loggingService: _loggingService,
            payload: new { playerIndex }
        );

    /// <summary>
    /// @notice Remove a previous ban.
    /// @dev Emits PlayerUnbanned(uint256). Returns tx hash only.
    /// </summary>
    public Task<string> UnbanPlayer(HttpContext httpContext, uint playerIndex) =>
        ContractUtils.SafeWriteSendAsync<UnbanPlayerFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn => fn.PlayerIndex = playerIndex,
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(UnbanPlayer),
            loggingService: _loggingService,
            payload: new { playerIndex }
        );

/* ───────────────────── Moderator helpers ───────────────────── */

    /// <summary>@notice Grant <c>MODERATOR_ROLE</c> to an EOA. Returns tx hash only.</summary>
    public Task<string> GrantModerator(HttpContext httpContext, string account)
    {
        if (!InputValidationHelper.IsStringIsValid(account, out var errorMessageAccount))
            throw new ArgumentException(errorMessageAccount, nameof(account));

        return ContractUtils.SafeWriteSendAsync<GrantModeratorFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn => fn.Account = account,
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(GrantModerator),
            loggingService: _loggingService,
            payload: new { account }
        );
    }

    /// <summary>@notice Revoke <c>MODERATOR_ROLE</c> from an EOA. Returns tx hash only.</summary>
    public Task<string> RevokeModerator(HttpContext httpContext, string account)
    {
        if (!InputValidationHelper.IsStringIsValid(account, out var errorMessageAccount))
            throw new ArgumentException(errorMessageAccount, nameof(account));

        return ContractUtils.SafeWriteSendAsync<RevokeModeratorFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn => fn.Account = account,
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(RevokeModerator),
            loggingService: _loggingService,
            payload: new { account }
        );
    }

/* ───────────────────── Generic role helpers ───────────────────── */

    /// <summary>@notice Grant an arbitrary <c>bytes32</c> role (by role accessor). Returns tx hash only.</summary>
    public Task<string> GrantRole(HttpContext httpContext, string role, string account)
    {
        if (!InputValidationHelper.IsStringIsValid(role, out var errorMessageRole))
            throw new ArgumentException(errorMessageRole, nameof(role));
        if (!InputValidationHelper.IsStringIsValid(account, out var errorMessageAccount))
            throw new ArgumentException(errorMessageAccount, nameof(account));

        return ContractUtils.SafeWriteSendAsync<GrantRoleFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn =>
            {
                fn.Role = role;
                fn.Account = account;
            },
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(GrantRole),
            loggingService: _loggingService,
            payload: new { role, account }
        );
    }

    /// <summary>@notice Revoke an arbitrary <c>bytes32</c> role (by role accessor). Returns tx hash only.</summary>
    public Task<string> RevokeRole(HttpContext httpContext, string role, string account)
    {
        if (!InputValidationHelper.IsStringIsValid(role, out var errorMessageRole))
            throw new ArgumentException(errorMessageRole, nameof(role));
        if (!InputValidationHelper.IsStringIsValid(account, out var errorMessageAccount))
            throw new ArgumentException(errorMessageAccount, nameof(account));

        return ContractUtils.SafeWriteSendAsync<RevokeRoleFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn =>
            {
                fn.Role = role;
                fn.Account = account;
            },
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(RevokeRole),
            loggingService: _loggingService,
            payload: new { role, account }
        );
    }


    /// <summary>
    /// @notice Query whether an address currently holds a role (read-only).
    /// </summary>
    public Task<bool> HasRole(string role, string account)
    {
        if (!InputValidationHelper.IsStringIsValid(role, out var errorMessageRole))
            throw new ArgumentException(errorMessageRole, nameof(role));
        if (!InputValidationHelper.IsStringIsValid(account, out var errorMessageAccount))
            throw new ArgumentException(errorMessageAccount, nameof(account));

        return ContractUtils.SafeReadAsync(
            async ct =>
            {
                var c = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);

                // AccessControl pattern: role accessor (e.g., MODERATOR_ROLE()) → bytes32
                var roleFn = c.GetFunction(role);
                var roleId = await roleFn.CallAsync<byte[]>();

                var fn = c.GetFunction("hasRole");
                return await fn.CallAsync<bool>(roleId, account);
            },
            defaultValue: false,
            _logger,
            nameof(HasRole),
            loggingService: _loggingService
        );
    }

    /* ───────────────────── Banned-player views ───────────────────── */

    /// <summary>@notice Return the list of all banned player indices.</summary>
    public Task<List<uint>> GetAllBannedPlayers() =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                var c = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var fn = c.GetFunction("getAllBannedPlayers");
                return await fn.CallAsync<List<uint>>();
            },
            defaultValue: new List<uint>(),
            _logger,
            nameof(GetAllBannedPlayers),
            loggingService: _loggingService
        );

    /// <summary>@notice Fetch a banned player index by list position.</summary>
    public Task<uint> GetPlayerBannedByIndex(uint index) =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                var c = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var fn = c.GetFunction("getPlayerBannedByIndex");
                return await fn.CallAsync<uint>(index);
            },
            defaultValue: uint.MaxValue,
            _logger,
            nameof(GetPlayerBannedByIndex),
            loggingService: _loggingService
        );

    /// <summary>@notice True if the player is currently banned.</summary>
    public Task<bool> IsPlayerBanned(uint playerIndex) =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                var c = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var fn = c.GetFunction("isPlayerBanned");
                return await fn.CallAsync<bool>(playerIndex);
            },
            defaultValue: false,
            _logger,
            nameof(IsPlayerBanned),
            loggingService: _loggingService
        );
}

/* ───────────────────── FunctionMessage definitions ───────────────────── */

[Function("pause")]
public sealed class PauseFunction : FunctionMessage
{
}

[Function("unpause")]
public sealed class UnpauseFunction : FunctionMessage
{
}

[Function("banPlayer")]
public sealed class BanPlayerFunction : FunctionMessage
{
    [Parameter("uint256", "playerIndex", 1)]
    public uint PlayerIndex { get; set; }
}

[Function("unbanPlayer")]
public sealed class UnbanPlayerFunction : FunctionMessage
{
    [Parameter("uint256", "playerIndex", 1)]
    public uint PlayerIndex { get; set; }
}

[Function("grantModerator")]
public sealed class GrantModeratorFunction : FunctionMessage
{
    [Parameter("address", "account", 1)]
    public string Account { get; set; } = string.Empty;
}

[Function("revokeModerator")]
public sealed class RevokeModeratorFunction : FunctionMessage
{
    [Parameter("address", "account", 1)]
    public string Account { get; set; } = string.Empty;
}

[Function("grantRole")]
public sealed class GrantRoleFunction : FunctionMessage
{
    [Parameter("bytes32", "role", 1)]
    public string Role { get; set; } = string.Empty;

    [Parameter("address", "account", 2)]
    public string Account { get; set; } = string.Empty;
}

[Function("revokeRole")]
public sealed class RevokeRoleFunction : FunctionMessage
{
    [Parameter("bytes32", "role", 1)]
    public string Role { get; set; } = string.Empty;

    [Parameter("address", "account", 2)]
    public string Account { get; set; } = string.Empty;
}