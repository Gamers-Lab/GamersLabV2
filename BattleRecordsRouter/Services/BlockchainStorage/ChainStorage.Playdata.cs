using BattleRecordsRouter.Config;
using BattleRecordsRouter.Helper;
using BattleRecordsRouter.Models;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Numerics;
using Microsoft.AspNetCore.Http;

namespace BattleRecordsRouter.Services;

/// <summary>
/// Per-player play-data helpers for <c>OnChainDataStorage</c>.
/// </summary>
public partial class GamersLabStorageService
{
    /* ──────────────────────── Create / update ──────────────────────── */

    /// <summary>
    /// @notice Record when a player joins a match session.
    /// @dev    Emits PlayerAddedToMatchSession(uint256,uint256,uint256).
    /// @return transactionHash  (tx hash only)
    /// </summary>
    public Task<string> AddPlayerToMatchSession(HttpContext httpContext, uint matchSessionId, uint playerIndex, uint startTime)
    {
        if (!InputValidationHelper.IsTimestampValid(startTime, out var tErr))
            throw new ArgumentException(tErr, nameof(startTime));

        return ContractUtils.SafeWriteSendAsync<AddPlayerToMatchSessionFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn =>
            {
                fn.MatchSessionId = matchSessionId;
                fn.PlayerIndex = playerIndex;
                fn.StartTime = startTime;
            },
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(AddPlayerToMatchSession),
            loggingService: _loggingService,
            payload: new { matchSessionId, playerIndex, startTime }
        );
    }

    /// <summary>
    /// @notice Finalise a player’s data after the match ends.
    /// @dev    Emits PlayerDataUpdated(uint256,…).
    /// @return transactionHash  (tx hash only)
    /// </summary>
    public Task<string> UpdatePlayDataForMatchSession(
        HttpContext httpContext,
        uint matchSessionId,
        uint playerIndex,
        uint endTime,
        int score,
        byte winLoss,
        int matchPosition)
    {
        if (!InputValidationHelper.IsTimestampValid(endTime, out var tErr))
            throw new ArgumentException(tErr, nameof(endTime));

        return ContractUtils.SafeWriteSendAsync<UpdatePlayDataForMatchSessionFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn =>
            {
                fn.MatchSessionId = matchSessionId;
                fn.PlayerIndex = playerIndex;
                fn.EndTime = endTime;
                fn.Score = score;
                fn.WinLoss = winLoss;
                fn.MatchPosition = matchPosition;
            },
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(UpdatePlayDataForMatchSession),
            loggingService: _loggingService,
            payload: new { matchSessionId, playerIndex, endTime, score, winLoss, matchPosition }
        );
    }
    
    /* ───────────────────────── Reads ───────────────────────── */

    public Task<uint> GetPlayDataCount() =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                var c = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var fn = c.GetFunction("getPlayDataCount");
                return await fn.CallAsync<uint>();
            },
            defaultValue: 0u,
            _logger,
            nameof(GetPlayDataCount),
            loggingService: _loggingService
        );

    /// <summary>@notice Fetch play-data by list index.</summary>
    public Task<PlayDataInfo> GetPlayDataByIndex(uint index) =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                var c = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var fn = c.GetFunction("getPlayDataByIndex");

                var dto = await fn.CallDeserializingToObjectAsync<PlayDataDTO>(index);

                return new PlayDataInfo
                {
                    BaseData = new BaseData
                    {
                        Id = (ulong)dto.BaseData.Id,
                        StartTime = (uint)dto.BaseData.StartTime,
                        EndTime = (uint)dto.BaseData.EndTime,
                        ApplicationId = (ulong)dto.BaseData.ApplicationId
                    },
                    // Keep metadata empty here as before (adjust if DTO includes metadata)
                    Metadata = new MetadataInfo { Keys = new List<string>(), Values = new List<List<string>>() },
                    PlayerIndex = (uint)dto.PlayerIndex,
                    Score = (int)dto.Score,
                    WinLoss = (WinLoss)(byte)dto.WinLoss,
                    MatchPosition = (int)dto.MatchPosition,
                    RecordIds = dto.RecordIds.Select(id => (ulong)id).ToList()
                };
            },
            defaultValue: new PlayDataInfo(),
            _logger,
            nameof(GetPlayDataByIndex),
            loggingService: _loggingService
        );


    /* ───────────────────── Solidity event DTOs ───────────────────── */

    [Event("PlayerAddedToMatchSession")]
    private class PlayerAddedToMatchSessionEventDTO : IEventDTO
    {
        [Parameter("uint256", "matchSessionId", 1, true)]
        public uint MatchSessionId { get; set; }

        [Parameter("uint256", "playerIndex", 2, true)]
        public uint PlayerIndex { get; set; }

        [Parameter("uint256", "playerDataId", 3)]
        public uint PlayDataId { get; set; }
    }

    /* ───────────────────── Function output DTO ───────────────────── */

    [FunctionOutput]
    public class PlayDataDTO : IFunctionOutputDTO
    {
        [Parameter("tuple", "base_", 1)]
        public BaseDataDTO BaseData { get; set; } = new();

        [Parameter("uint256", "playerIdx", 2)]
        public BigInteger PlayerIndex { get; set; }

        [Parameter("int256", "score", 3)]
        public BigInteger Score { get; set; }

        [Parameter("uint8", "wl", 4)]
        public BigInteger WinLoss { get; set; }

        [Parameter("int256", "pos", 5)]
        public BigInteger MatchPosition { get; set; }

        [Parameter("uint256[]", "recIds", 6)]
        public List<BigInteger> RecordIds { get; set; } = new();
    }
}

/* ───────────────────── FunctionMessage definitions ───────────────────── */

[Function("addPlayerToMatchSession")]
public sealed class AddPlayerToMatchSessionFunction : FunctionMessage
{
    [Parameter("uint256", "matchSessionId", 1)]
    public uint MatchSessionId { get; set; }

    [Parameter("uint256", "playerIndex", 2)]
    public uint PlayerIndex { get; set; }

    [Parameter("uint256", "startTime", 3)]
    public uint StartTime { get; set; }
}

[Function("updatePlayDataForMatchSession")]
public sealed class UpdatePlayDataForMatchSessionFunction : FunctionMessage
{
    [Parameter("uint256", "mid", 1)]
    public uint MatchSessionId { get; set; }

    [Parameter("uint256", "pIdx", 2)]
    public uint PlayerIndex { get; set; }

    [Parameter("uint256", "end", 3)]
    public uint EndTime { get; set; }

    [Parameter("int256", "score", 4)]
    public int Score { get; set; }

    [Parameter("uint8", "wl", 5)]
    public byte WinLoss { get; set; }

    [Parameter("int256", "pos", 6)]
    public int MatchPosition { get; set; }
}

[Function("addPlayDataMetadata")]
public sealed class AddPlayDataMetadataFunction : FunctionMessage
{
    [Parameter("uint256", "playDataId", 1)]
    public uint PlayDataId { get; set; }

    [Parameter("string", "key", 2)]
    public string Key { get; set; } = string.Empty;

    [Parameter("string", "value", 3)]
    public string Value { get; set; } = string.Empty;
}