using BattleRecordsRouter.Config;
using BattleRecordsRouter.Helper;
using BattleRecordsRouter.Models;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Numerics;
using Microsoft.AspNetCore.Http;

namespace BattleRecordsRouter.Services;

/// <summary>
/// Player-related reads &amp; writes on <c>OnChainDataStorage</c>.
/// </summary>
public partial class GamersLabStorageService
{
    /* ───────────────────────── Player CRUD ───────────────────────── */

    /// <summary>
    /// @notice Register a new player.
    /// @dev    Emits PlayerCreated(uint256,string,address,uint8).
    /// @return transactionHash  Mined tx hash
    /// @return playerIndex      Newly assigned on-chain index
    /// </summary>
    public async Task<(string transactionHash, uint playerIndex)> CreatePlayer(
        HttpContext httpContext,
        string playerId,
        PlayerType playerType,
        string playerAddress)
    {
        if (!GenericWeb3Helper.IsValidAddress(playerAddress, out var errAddr))
            throw new ArgumentException(errAddr, nameof(playerAddress));
        if (!InputValidationHelper.IsStringIsValid(playerId, out var errId))
            throw new ArgumentException(errId, nameof(playerId));
        if (!Enum.IsDefined(typeof(PlayerType), playerType) || playerType == PlayerType.Last)
            throw new ArgumentException($"Invalid player type: {playerType}", nameof(playerType));

        // send + wait (short lock only during send)
        var (txHash, receipt) = await ContractUtils.SafeWriteSendAndWaitAsync<CreatePlayerFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn =>
            {
                fn.PlayerId = playerId;
                fn.PlayerType = (byte)playerType;
                fn.PlayerAddress = playerAddress;
            },
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(CreatePlayer),
            loggingService: _loggingService,
            payload: new { playerId, playerType, playerAddress },
            receiptTimeout: TimeSpan.FromMinutes(2),
            throwOnFailedReceipt: false,
            receiptPollMs: 1200
        ).ConfigureAwait(false);

        // fail-fast if missing/failed receipt
        if (string.IsNullOrEmpty(txHash) || receipt is null || receipt.Status?.Value != 1)
        {
            _logger.LogWarning("CreatePlayer - Tx missing/failed. Tx={Tx} Status={Status}",
                txHash, receipt?.Status?.Value);
            return (string.Empty, 0u);
        }

        // decode PlayerCreated event from the same receipt
        var createdEvt = _web3.Eth.GetEvent<PlayerCreatedEventDTO>(_contractAddress)
            .DecodeAllEventsForEvent(receipt.Logs)
            .FirstOrDefault()?.Event;

        if (createdEvt is null)
            throw new InvalidOperationException("PlayerCreated event not found in receipt.");

        // PlayerIndex may be BigInteger or uint in the DTO; handle safely
        uint playerIndex;
        var idxObj = createdEvt.PlayerIndex;
        if (idxObj is uint u)
        {
            playerIndex = u;
        }
        else
        {
            var bi = (System.Numerics.BigInteger)idxObj;
            if (bi < 0 || bi > uint.MaxValue)
                throw new OverflowException($"PlayerIndex {bi} out of range for uint.");
            playerIndex = (uint)bi;
        }

        return (txHash, playerIndex);
    }


    /// <summary>
    /// @notice Update a player’s score.
    /// @dev    Emits PlayerScoreUpdated(uint256,int256) (not decoded here).
    /// @return transactionHash  Tx hash only
    /// </summary>
    public Task<string> UpdatePlayerScore(HttpContext httpContext, uint playerIndex, int newScore)
    {
        // send-only: return tx hash, no wait / no decode
        return ContractUtils.SafeWriteSendAsync<UpdatePlayerScoreFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn =>
            {
                fn.PlayerIndex = playerIndex;
                fn.NewScore = newScore;
            },
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(UpdatePlayerScore),
            loggingService: _loggingService,
            payload: new { playerIndex, newScore }
        );
    }


/* ─────────────────── Verified addresses & IDs ─────────────────── */

    public Task<string> AddVerifiedAddress(HttpContext httpContext, uint playerIndex, string verifiedAddress)
    {
        if (!GenericWeb3Helper.IsValidAddress(verifiedAddress, out var errAddr))
            throw new ArgumentException(errAddr, nameof(verifiedAddress));

        return ContractUtils.SafeWriteSendAsync<AddVerifiedAddressFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn =>
            {
                fn.PlayerIndex = playerIndex;
                fn.VerifiedAddress = verifiedAddress;
            },
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(AddVerifiedAddress),
            loggingService: _loggingService,
            payload: new { playerIndex, verifiedAddress }
        );
    }


    public Task<string> AddPlayerIdentifier(HttpContext httpContext, uint playerIndex, string identifierType, string identifierValue)
    {
        if (!InputValidationHelper.IsStringIsValid(identifierType, out var errT))
            throw new ArgumentException(errT, nameof(identifierType));
        if (!InputValidationHelper.IsStringIsValid(identifierValue, out var errV))
            throw new ArgumentException(errV, nameof(identifierValue));

        return ContractUtils.SafeWriteSendAsync<AddPlayerIdentifierFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn =>
            {
                fn.PlayerIndex = playerIndex;
                fn.IdentifierType = identifierType;
                fn.IdentifierValue = identifierValue;
            },
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(AddPlayerIdentifier),
            loggingService: _loggingService,
            payload: new { playerIndex, identifierType, identifierValue }
        );
    }

    public Task<string> SetPlayerMetadata(HttpContext httpContext, uint playerIndex, string key, string value)
    {
        if (!InputValidationHelper.IsStringIsValid(key, out var kErr))
            throw new ArgumentException(kErr, nameof(key));
        if (!InputValidationHelper.IsStringIsValid(value, out var vErr))
            throw new ArgumentException(vErr, nameof(value));

        return ContractUtils.SafeWriteSendAsync<SetPlayerMetadataFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn =>
            {
                fn.PlayerIndex = playerIndex;
                fn.Key = key;
                fn.Value = value;
            },
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(SetPlayerMetadata),
            loggingService: _loggingService,
            payload: new { playerIndex, key, value }
        );
    }

    public Task<string> UpdatePlayerUniqueId(HttpContext httpContext, uint playerIndex, string newPlayerId)
    {
        if (!InputValidationHelper.IsStringIsValid(newPlayerId, out var errId))
            throw new ArgumentException(errId, nameof(newPlayerId));

        return ContractUtils.SafeWriteSendAsync<UpdatePlayerUniqueIdFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn =>
            {
                fn.PlayerIndex = playerIndex;
                fn.NewPlayerId = newPlayerId;
            },
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(UpdatePlayerUniqueId),
            loggingService: _loggingService,
            payload: new { playerIndex, newPlayerId }
        );
    }


/* ───────────────────────── Helpers / views (READS) ───────────────────────── */

    public Task<uint> GetPlayerIndex(string playerId)
    {
        if (!InputValidationHelper.IsStringIsValid(playerId, out var err))
            throw new ArgumentException(err, nameof(playerId));

        return ContractUtils.SafeReadAsync(
            async ct =>
            {
                var c = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var fn = c.GetFunction("getPlayerIndex");
                return await fn.CallAsync<uint>(playerId);
            },
            defaultValue: 0u,
            _logger,
            nameof(GetPlayerIndex),
            loggingService: _loggingService
        );
    }

    public Task<string> GetPlayerUniqueId(uint playerIndex) =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                var c = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var fn = c.GetFunction("getPlayerUniqueId");
                return await fn.CallAsync<string>(playerIndex);
            },
            defaultValue: string.Empty,
            _logger,
            nameof(GetPlayerUniqueId),
            loggingService: _loggingService
        );


    public async Task<(bool ok, uint playerIndex, string message)>
        TryGetPlayerIndexByAddressAsync(string address)
    {
        // 1) basic format validation ------------------------------------------
        if (!GenericWeb3Helper.IsValidAddress(address, out var err))
            return (false, uint.MaxValue, err);

        try
        {
            /* call the same view fn we already use elsewhere */
            var contract = BlockchainTransactionHelper.GetContract(
                _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);

            var fn = contract.GetFunction("getPlayerIndexByAddress");
            uint idx = await fn.CallAsync<uint>(address);

            // Check if the returned index is valid
            if (idx == 0)
                return (false, uint.MaxValue, "wallet not registered on-chain");

            return (true, idx, string.Empty);
        }
        catch (Nethereum.Contracts.SmartContractCustomErrorRevertException ex)
        {
            _logger.LogWarning(ex,
                "[ChainStorage] Contract revert in getPlayerIndexByAddress for {addr}", address);

            return (false, uint.MaxValue, $"Smart contract error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[ChainStorage] getPlayerIndexByAddress failed for {addr}", address);

            return (false, uint.MaxValue, ex.Message);
        }
    }

    /// <inheritdoc cref="TryGetPlayerIndexByAddressAsync(string)"/>
    /// <remarks>
    /// Legacy API updated to return uint.MaxValue instead of throwing on failure.
    /// </remarks>
    public async Task<uint> GetPlayerIndexByAddress(string address)
    {
        var (ok, idx, msg) = await TryGetPlayerIndexByAddressAsync(address);

        if (!ok)
        {
            return uint.MaxValue;
        }

        return idx;
    }


    public Task<uint> GetPlayerCount() =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                var c = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var fn = c.GetFunction("getPlayerCount");
                return await fn.CallAsync<uint>();
            },
            defaultValue: 0u,
            _logger,
            nameof(GetPlayerCount),
            loggingService: _loggingService
        );

    /// <summary>@notice Return full metadata for a player.</summary>
    public Task<(string transactionHash, PlayerResponse player)> GetPlayerByIndex(uint index) =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                _logger.LogDebug("[GetPlayerByIndex] Starting for index #{Index}", index);

                var contract = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var fn = contract.GetFunction("getPlayerByIndex");

                _logger.LogDebug("[GetPlayerByIndex] Calling contract function for index #{Index}", index);

                var dto = await fn.CallDeserializingToObjectAsync<PlayerDTO>(index);

                _logger.LogDebug(
                    "[GetPlayerByIndex] Successfully called contract for index #{Index}, Score: {Score}",
                    index, dto.Score);

                var identifiers = new Dictionary<string, string>();
                for (int i = 0; i < dto.IdentifierKeys.Count && i < dto.IdentifierValues.Count; i++)
                    identifiers[dto.IdentifierKeys[i]] = dto.IdentifierValues[i];

                return (string.Empty, new PlayerResponse
                {
                    PlayerId = dto.PlayerId,
                    Address = dto.Address,
                    PlayerType = (PlayerType)dto.PlayerType,
                    Score = dto.Score,
                    Identifiers = identifiers,
                    VerifiedAddresses = dto.VerifiedAddresses
                });
            },
            defaultValue: (string.Empty, null),
            _logger,
            nameof(GetPlayerByIndex),
            loggingService: _loggingService
        );

/* ─────────────────── Verified-ID helper ─────────────────── */

    public Task<uint> GetPlayerByVerifiedId(string identifierType, string identifierValue)
    {
        if (!InputValidationHelper.IsStringIsValid(identifierType, out var errT))
            throw new ArgumentException(errT, nameof(identifierType));
        if (!InputValidationHelper.IsStringIsValid(identifierValue, out var errV))
            throw new ArgumentException(errV, nameof(identifierValue));

        return ContractUtils.SafeReadAsync(
            async ct =>
            {
                var contract = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);

                var fn = contract.GetFunction("getPlayerByVerifiedId");
                return await fn.CallAsync<uint>(identifierType, identifierValue);
            },
            defaultValue: uint.MaxValue,
            _logger,
            nameof(GetPlayerByVerifiedId),
            loggingService: _loggingService
        );
    }


    /* ───────────────────── Solidity Event DTOs ───────────────────── */

    [Event("PlayerCreated")]
    private class PlayerCreatedEventDTO : IEventDTO
    {
        [Parameter("uint256", "playerIndex", 1, true)]
        public uint PlayerIndex { get; set; }

        [Parameter("address", "playerAddress", 2, true)]
        public string PlayerAddress { get; set; } = string.Empty;

        [Parameter("string", "playerID", 3, false)]
        public string PlayerId { get; set; } = string.Empty;

        [Parameter("uint8", "playerType", 4, false)]
        public byte PlayerType { get; set; }
    }


    /* ───────────────────── DTO returned by getPlayerByIndex ───────────────────── */

    [FunctionOutput]
    private class PlayerDTO
    {
        [Parameter("string", "pid", 1)]
        public string PlayerId { get; set; } = string.Empty;

        [Parameter("address", "addr", 2)]
        public string Address { get; set; } = string.Empty;

        [Parameter("uint8", "ptype", 3)]
        public byte PlayerType { get; set; }

        [Parameter("int256", "score", 4)]
        public BigInteger Score { get; set; }

        [Parameter("string[]", "idKeys", 5)]
        public List<string> IdentifierKeys { get; set; } = new();

        [Parameter("string[]", "idVals", 6)]
        public List<string> IdentifierValues { get; set; } = new();

        [Parameter("address[]", "verifiedAddrs", 7)]
        public List<string> VerifiedAddresses { get; set; } = new();
    }
}

/* ───────────────────── FunctionMessage defs ───────────────────── */

[Function("createPlayer")]
public sealed class CreatePlayerFunction : FunctionMessage
{
    [Parameter("string", "playerID", 1)]
    public string PlayerId { get; set; } = string.Empty;

    [Parameter("uint8", "playerType", 2)]
    public byte PlayerType { get; set; }

    [Parameter("address", "playerAddress", 3)]
    public string PlayerAddress { get; set; } = string.Empty;
}

[Function("updatePlayerScore")]
public sealed class UpdatePlayerScoreFunction : FunctionMessage
{
    [Parameter("uint256", "playerIndex", 1)]
    public uint PlayerIndex { get; set; }

    [Parameter("int256", "newScore", 2)]
    public int NewScore { get; set; }
}

[Function("addVerifiedAddress")]
public sealed class AddVerifiedAddressFunction : FunctionMessage
{
    [Parameter("uint256", "playerIndex", 1)]
    public uint PlayerIndex { get; set; }

    [Parameter("address", "verifiedAddress", 2)]
    public string VerifiedAddress { get; set; } = string.Empty;
}

[Function("addPlayerIdentifier")]
public sealed class AddPlayerIdentifierFunction : FunctionMessage
{
    [Parameter("uint256", "playerIndex", 1)]
    public uint PlayerIndex { get; set; }

    [Parameter("string", "identifierType", 2)]
    public string IdentifierType { get; set; } = string.Empty;

    [Parameter("string", "identifierValue", 3)]
    public string IdentifierValue { get; set; } = string.Empty;
}

[Function("setPlayerMetadata")]
public sealed class SetPlayerMetadataFunction : FunctionMessage
{
    [Parameter("uint256", "idx", 1)]
    public uint PlayerIndex { get; set; }

    [Parameter("string", "k", 2)]
    public string Key { get; set; } = string.Empty;

    [Parameter("string", "v", 3)]
    public string Value { get; set; } = string.Empty;
}

[Function("updatePlayerUniqueId")]
public sealed class UpdatePlayerUniqueIdFunction : FunctionMessage
{
    [Parameter("uint256", "idx", 1)]
    public uint PlayerIndex { get; set; }

    [Parameter("string", "newPid", 2)]
    public string NewPlayerId { get; set; } = string.Empty;
}