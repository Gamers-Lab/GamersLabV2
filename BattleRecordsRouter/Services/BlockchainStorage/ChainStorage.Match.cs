using System.Numerics;
using BattleRecordsRouter.Config;
using BattleRecordsRouter.Helper;
using BattleRecordsRouter.Models;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Microsoft.AspNetCore.Http;

namespace BattleRecordsRouter.Services;

/// <summary>
/// Match-session helpers for <c>OnChainDataStorage</c>.
/// </summary>
public partial class GamersLabStorageService
{
    /* ───────────────────────── Start / End ───────────────────────── */

    /// <summary>
    /// @notice Create a new match session.
    /// @dev    Emits MatchSessionStarted(uint256, …) and PlayerAddedToMatchSession(...).
    /// </summary>
    public async Task<(string transactionHash, uint matchSessionId, uint[] playDataIds)> StartMatchSession(
        HttpContext httpContext,
        ulong applicationId,
        uint startTime,
        bool multiplayer,
        uint[] playerIds,
        string level)
    {
        if (!InputValidationHelper.IsTimestampValid(startTime, out var tErr))
            throw new ArgumentException(tErr, nameof(startTime));
        if (!InputValidationHelper.IsStringIsValid(level, out var lErr))
            throw new ArgumentException(lErr, nameof(level));

        _logger.LogDebug(
            "StartMatchSession - appId: {AppId}, multiplayer: {Multiplayer}, players: {Players}, level: {Level}",
            applicationId, multiplayer, string.Join(",", playerIds), level);

        // Send (short lock only during send) + wait for receipt
        var (txHash, receipt) = await ContractUtils.SafeWriteSendAndWaitAsync<StartMatchSessionFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn =>
            {
                fn.ApplicationId = applicationId;
                fn.StartTime = startTime;
                fn.Multiplayer = multiplayer;
                fn.PlayerIds = playerIds.ToList();
                fn.Level = level;
            },
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(StartMatchSession),
            loggingService: _loggingService,
            payload: new { applicationId, startTime, multiplayer, playerIds, level },
            receiptTimeout: TimeSpan.FromMinutes(2),
            throwOnFailedReceipt: false,
            receiptPollMs: 1200
        ).ConfigureAwait(false);

        if (string.IsNullOrEmpty(txHash) || receipt is null || receipt.Status?.Value != 1)
        {
            _logger.LogWarning("StartMatchSession - Tx missing/failed. Tx={Tx} Status={Status}",
                txHash, receipt?.Status?.Value);
            return (string.Empty, 0u, Array.Empty<uint>());
        }

        // Decode the primary MatchSessionStarted event (outside the lock)
        var startedEvents = _web3.Eth.GetEvent<MatchSessionStartedEventDTO>(_contractAddress)
            .DecodeAllEventsForEvent(receipt.Logs);
        var mainEvent = startedEvents.FirstOrDefault()?.Event;

        var matchIdBig = mainEvent?.MatchSessionId ?? System.Numerics.BigInteger.Zero;
        if (matchIdBig > uint.MaxValue)
            throw new OverflowException($"MatchSessionId {matchIdBig} exceeds uint.MaxValue");

        var matchId = (uint)matchIdBig;

        // Decode PlayerAddedToMatchSession for playDataIds (still outside the lock)
        var playerAddedEvents = _web3.Eth.GetEvent<PlayerAddedToMatchSessionEventDTO>(_contractAddress)
            .DecodeAllEventsForEvent(receipt.Logs);

        var playDataIds = playerAddedEvents
            .Select(e => e.Event.PlayDataId is uint u ? u : (uint)(System.Numerics.BigInteger)e.Event.PlayDataId)
            .ToArray();

        _logger.LogDebug("StartMatchSession - TX: {Tx}, MatchSessionId: {MatchId}, PlayDataIds: {Ids}",
            txHash, matchId, string.Join(",", playDataIds));

        return (txHash, matchId, playDataIds);
    }


    /// <summary>
    /// @notice Close a session and record its end-time.
    /// @dev    Emits MatchSessionEnded(uint256,uint256) on-chain (not decoded here).
    /// </summary>
    public Task<string> EndMatchSession(HttpContext httpContext, uint matchSessionId, uint endTime)
    {
        if (!InputValidationHelper.IsTimestampValid(endTime, out var tErr))
            throw new ArgumentException(tErr, nameof(endTime));

        return ContractUtils.SafeWriteSendAsync<EndMatchSessionFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn =>
            {
                fn.MatchSessionId = matchSessionId;
                fn.EndTime = endTime;
            },
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(EndMatchSession),
            loggingService: _loggingService,
            payload: new { matchSessionId, endTime }
        );
    }


/* ───────────────────────── Metadata ───────────────────────── */

    public Task<string> SetMatchSessionMetadata(HttpContext httpContext, uint matchSessionId, string key, string value)
    {
        if (!InputValidationHelper.IsStringIsValid(key, out var kErr))
            throw new ArgumentException(kErr, nameof(key));
        if (!InputValidationHelper.IsStringIsValid(value, out var vErr))
            throw new ArgumentException(vErr, nameof(value));

        return ContractUtils.SafeWriteSendAsync<SetMatchSessionMetadataFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn =>
            {
                fn.MatchSessionId = matchSessionId;
                fn.Key = key;
                fn.Value = value;
            },
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(SetMatchSessionMetadata),
            loggingService: _loggingService,
            payload: new { matchSessionId, key, value }
        );
    }


    /* ───────────────────────── Reads ───────────────────────── */

    public Task<uint> GetActiveMatchSessionsLength() =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                var c = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var fn = c.GetFunction("getMatchSessionCount");
                return await fn.CallAsync<uint>();
            },
            defaultValue: 0u,
            _logger,
            nameof(GetActiveMatchSessionsLength),
            loggingService: _loggingService
        );

    /// <summary>@notice Return all currently active session IDs.</summary>
    public Task<uint[]> GetAllActiveMatchSessions() =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                var c = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var fn = c.GetFunction("getAllActiveMatchSessionIds");
                var list = await fn.CallAsync<List<uint>>();
                return list?.ToArray() ?? Array.Empty<uint>();
            },
            defaultValue: Array.Empty<uint>(),
            _logger,
            nameof(GetAllActiveMatchSessions),
            loggingService: _loggingService
        );

    /// <summary>
    /// Get all active match sessions for a specific player
    /// </summary>
    /// <param name="playerIndex">The player index to query</param>
    /// <returns>Array of match session IDs where the player is active</returns>
    public Task<uint[]> GetActiveMatchSessions(uint playerIndex) =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                var allActiveSessions = await GetAllActiveMatchSessions();
                if (allActiveSessions.Length == 0)
                    return Array.Empty<uint>();

                var playerSessions = new List<uint>();
                foreach (var sessionId in allActiveSessions)
                {
                    try
                    {
                        var sessionInfo = await GetMatchSessionByIndex(sessionId);
                        if (sessionInfo.PlayerIds.Contains(playerIndex))
                            playerSessions.Add(sessionId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Error checking match session {SessionId} for player {PlayerIndex}",
                            sessionId, playerIndex);
                    }
                }

                return playerSessions.ToArray();
            },
            defaultValue: Array.Empty<uint>(),
            _logger,
            nameof(GetActiveMatchSessions),
            loggingService: _loggingService
        );

    /// <summary>
    /// Finds the play data ID for a player in a match session
    /// </summary>
    public Task<uint> FindPlayDataId(uint matchSessionId, uint playerIndex) =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                var c = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var fn = c.GetFunction("findPlayDataId");
                return await fn.CallAsync<uint>(matchSessionId, playerIndex);
            },
            defaultValue: 0u,
            _logger,
            nameof(FindPlayDataId),
            loggingService: _loggingService
        );

    public Task<MatchSessionInfo> GetMatchSessionByIndex(uint index) =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                var c = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var fn = c.GetFunction("getMatchSessionByIndex");
                var dto = await fn.CallDeserializingToObjectAsync<MatchSessionDTO>(index);
                return ToDomain(dto);
            },
            defaultValue: new MatchSessionInfo(),
            _logger,
            nameof(GetMatchSessionByIndex),
            loggingService: _loggingService
        );

    /* ───────────────────────── DTO helpers ───────────────────────── */

    private static MatchSessionInfo ToDomain(MatchSessionDTO dto) => new()
    {
        BaseData = new BaseData
        {
            Id = dto.BaseData.Id,
            StartTime = dto.BaseData.StartTime,
            EndTime = dto.BaseData.EndTime,
            ApplicationId = dto.BaseData.ApplicationId
        },
        Metadata = new MetadataInfo { Keys = dto.Metadata.Keys, Values = dto.Metadata.Values },
        Multiplayer = dto.Multiplayer,
        Level = dto.Level,
        PlayerIds = dto.PlayerIds,
        PlayData = dto.PlayData
    };

    /* ───────────────────────── Solidity DTOs ───────────────────────── */

    [FunctionOutput]
    public class MatchSessionDTO : IFunctionOutputDTO
    {
        [Parameter("tuple", "base_", 1)]
        public BaseDataDTO BaseData { get; set; } = new();

        [Parameter("tuple", "meta_", 2)]
        public MetadataDTO Metadata { get; set; } = new();

        [Parameter("bool", "multiplayer", 3)]
        public bool Multiplayer { get; set; }

        [Parameter("uint256[]", "playerIds", 4)]
        public List<uint> PlayerIds { get; set; } = new();

        [Parameter("uint256[]", "playDataIds", 5)]
        public List<uint> PlayData { get; set; } = new();

        [Parameter("string", "level", 6)]
        public string Level { get; set; } = string.Empty;
    }

    /* ───────────────────────── Event DTOs ───────────────────────── */

    [Event("MatchSessionStarted")]
    private class MatchSessionStartedEventDTO : IEventDTO
    {
        [Parameter("uint256", "matchSessionId", 1, true)]
        public BigInteger MatchSessionId { get; set; }

        [Parameter("uint256", "applicationId", 2, false)] // ✅ now correctly not indexed
        public BigInteger ApplicationId { get; set; }

        [Parameter("uint256", "startTime", 3, true)]
        public BigInteger StartTime { get; set; }

        [Parameter("bool", "multiplayer", 4, false)]
        public bool Multiplayer { get; set; }

        [Parameter("uint256[]", "playerIndices", 5, false)]
        public List<BigInteger> PlayerIndices { get; set; } = new();

        [Parameter("string", "level", 6, false)]
        public string Level { get; set; } = string.Empty;
    }
}

/* ───────────────────────── FunctionMessage defs ───────────────────────── */

[Function("startMatchSession")]
public sealed class StartMatchSessionFunction : FunctionMessage
{
    [Parameter("uint256", "appId", 1)]
    public ulong ApplicationId { get; set; }

    [Parameter("uint256", "start", 2)]
    public uint StartTime { get; set; }

    [Parameter("bool", "multiplayer", 3)]
    public bool Multiplayer { get; set; }

    [Parameter("uint256[]", "pIdx", 4)]
    public List<uint> PlayerIds { get; set; } = new();

    [Parameter("string", "level", 5)]
    public string Level { get; set; } = string.Empty;
}

[Function("endMatchSession")]
public sealed class EndMatchSessionFunction : FunctionMessage
{
    [Parameter("uint256", "matchSessionId", 1)]
    public uint MatchSessionId { get; set; }

    [Parameter("uint256", "endTime", 2)]
    public uint EndTime { get; set; }
}

[Function("setMatchSessionMetadata")]
public sealed class SetMatchSessionMetadataFunction : FunctionMessage
{
    [Parameter("uint256", "matchSessionId", 1)]
    public uint MatchSessionId { get; set; }

    [Parameter("string", "key", 2)]
    public string Key { get; set; } = string.Empty;

    [Parameter("string", "value", 3)]
    public string Value { get; set; } = string.Empty;
}