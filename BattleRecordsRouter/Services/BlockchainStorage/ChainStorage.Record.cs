using System.Numerics;
using BattleRecordsRouter.Config;
using BattleRecordsRouter.Helper;
using BattleRecordsRouter.Models;
using BattleRecordsRouter.Repositories;
using BattleRecordsRouter.Services.Database;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Microsoft.AspNetCore.Http;

namespace BattleRecordsRouter.Services;

public partial class GamersLabStorageService
{

    /* ────────────────────────────── WRITE ────────────────────────────── */

    /// <summary>
    /// Set a record for a player in a match session.
    /// @return transactionHash (tx hash only; no wait / no event decode)
    /// </summary>
    public async Task<string> SetRecord(
        HttpContext httpContext,
        uint matchSessionId,
        uint playerIndex,
        int score,
        string key,
        string value,
        uint[] otherPlayers,
        uint startTime)
    {
        if (!InputValidationHelper.IsTimestampValid(startTime, out var terr))
            throw new ArgumentException(terr, nameof(startTime));
        if (!InputValidationHelper.IsStringIsValid(key, out var kerr))
            throw new ArgumentException(kerr, nameof(key));
        if (!InputValidationHelper.IsStringIsValid(value, out var verr))
            throw new ArgumentException(verr, nameof(value));

        // --- BETA: dedicated minter for SetRecord / BatchSetRecords ---
        // TODO: move to Key Vault / configuration after validation

        var batchAccount = new Nethereum.Web3.Accounts.Account(_config.ModeratorAccount.PrivateKey, _config.ChainId);
        var batchWeb3 = new Nethereum.Web3.Web3(batchAccount, _web3.Client);

        // Prefer EIP-1559 pricing (not legacy)
        if (batchWeb3.TransactionManager is Nethereum.Web3.Accounts.AccountSignerTransactionManager astm)
            astm.UseLegacyAsDefault = false;

        // Use shared nonce service (tracks nonces in memory across all requests)
        batchWeb3.TransactionManager.Account.NonceService = _sharedNonceService;

        _logger.LogInformation(
            "SetRecord sending from {Sender} to {Contract}",
            batchWeb3.TransactionManager?.Account?.Address,
            _contractAddress
        );

        // --- Explicit 1559 fees + conservative gas (skip estimator) ---
        // Explorer shows ~2,000,000 gas used with ~3,000,000 limit → use 3,000,000 as conservative cap
        var gasLimit = new Nethereum.Hex.HexTypes.HexBigInteger(3_000_000);

        // Base fee proxy via gasPrice; pick a small fixed tip (tunable)
        var baseFee = await batchWeb3.Eth.GasPrice.SendRequestAsync().ConfigureAwait(false);
        var tip = new Nethereum.Hex.HexTypes.HexBigInteger(1_500_000_000); // 1.5 gwei tip
        var maxFee = new Nethereum.Hex.HexTypes.HexBigInteger(baseFee.Value * 2 + tip.Value);

        // SEND ONLY — return tx hash, no wait / no decode
        return await ContractUtils.SafeWriteSendAsync<SetRecordFunction>(
            web3: batchWeb3,
            contractAddress: _contractAddress,
            init: fn =>
            {
                // payload
                fn.MatchId = matchSessionId;
                fn.PlayerIndex = playerIndex;
                fn.Score = score;
                fn.Key = key;
                fn.Value = value;
                fn.OtherPlayers = new List<uint>(otherPlayers);
                fn.StartTime = startTime;

                // ensure correct msg.sender for any internal calls (and for safety)
                fn.FromAddress = batchAccount.Address;

                // bypass estimation
                fn.Gas = gasLimit;
                fn.GasPrice = null; // ensure 1559, not legacy
                fn.MaxPriorityFeePerGas = tip;
                fn.MaxFeePerGas = maxFee;
            },
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(SetRecord),
            loggingService: _loggingService,
            walletAddress: batchAccount.Address,
            payload: new { matchSessionId, playerIndex, score, key, value, otherPlayers, startTime }
        ).ConfigureAwait(false);
    }


    public async Task<string> BatchSetRecords(HttpContext httpContext, RecordInput[] inputs)
    {
        // --- Validate inputs
        foreach (var i in inputs)
        {
            if (!InputValidationHelper.IsTimestampValid(i.StartTime, out var terr))
                throw new ArgumentException(terr, nameof(i.StartTime));
            if (!InputValidationHelper.IsStringIsValid(i.Key, out var kerr))
                throw new ArgumentException(kerr, nameof(i.Key));
            if (!InputValidationHelper.IsStringIsValid(i.Value, out var verr))
                throw new ArgumentException(verr, nameof(i.Value));
        }

        // --- BETA: dedicated minter for SetRecord/BatchSetRecords (move to Key Vault later)
        var batchAccount = new Nethereum.Web3.Accounts.Account(_config.ModeratorAccount.PrivateKey, _config.ChainId);
        var batchWeb3 = new Nethereum.Web3.Web3(batchAccount, _web3.Client);

        // Prefer EIP-1559 (not legacy)
        var tm = batchWeb3.TransactionManager;
        if (tm is Nethereum.Web3.Accounts.AccountSignerTransactionManager astm)
            astm.UseLegacyAsDefault = false;

        // Use shared nonce service (tracks nonces in memory across all requests)
        batchWeb3.TransactionManager.Account.NonceService = _sharedNonceService;

        // --- Your conservative gas heuristic
        const int fixedOverhead = 10_000_000;
        const int perRecordGas = 5_000_000;
        const int maxGas = 100_000_000;

        // --- PRECOMPUTE FEES to avoid Nethereum's TimePreferenceFeeSuggestionStrategy
        // baseFee proxy via eth_gasPrice; tip fixed to keep it simple & stable under load
        var gasPrice = await batchWeb3.Eth.GasPrice.SendRequestAsync().ConfigureAwait(false);
        var tip = new Nethereum.Hex.HexTypes.HexBigInteger(1_500_000_000); // 1.5 gwei tip (tune as needed)
        var maxFee = new Nethereum.Hex.HexTypes.HexBigInteger(gasPrice.Value * 2 + tip.Value);

        _logger.LogInformation(
            "BatchSetRecords sending from {Sender} to {Contract}",
            batchWeb3.TransactionManager?.Account?.Address,
            _contractAddress
        );

        return await ContractUtils.SafeWriteSendAsync<BatchSetRecordsFunction>(
            web3: batchWeb3,
            contractAddress: _contractAddress,
            init: fn =>
            {
                // Build the payload
                fn.Inputs = inputs.Select(i => new RecordInputDTO
                {
                    MatchId = (System.Numerics.BigInteger)i.MatchSessionId,
                    PlayerIndex = (System.Numerics.BigInteger)i.PlayerIndex,
                    Score = (System.Numerics.BigInteger)i.Score,
                    Key = i.Key,
                    Value = i.Value,
                    OtherPlayers = (i.OtherPlayers ?? Array.Empty<uint>())
                        .Select(op => (System.Numerics.BigInteger)op)
                        .ToList(),
                    StartTime = (System.Numerics.BigInteger)i.StartTime
                }).ToArray();

                // Gas limit
                var baseGas = fixedOverhead + (perRecordGas * fn.Inputs.Length);
                var totalGas = (int)Math.Min((baseGas * 1.25), maxGas);
                fn.Gas = new Nethereum.Hex.HexTypes.HexBigInteger(totalGas);

                // EIP-1559 fees (explicit) — this **disables** the auto fee suggestion path
                fn.GasPrice = null; // ensure we are NOT in legacy pricing
                fn.MaxPriorityFeePerGas = tip;
                fn.MaxFeePerGas = maxFee;

                _logger.LogInformation("Submitting batchSetRecords with {Count} records (gas {Gas})",
                    fn.Inputs.Length, totalGas);
            },
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(BatchSetRecords),
            loggingService: _loggingService,
            payload: new { recordCount = inputs.Length }
        ).ConfigureAwait(false);
    }


    /* ───────────────────────────── READS ─────────────────────────────── */

    /// <summary>@notice Fetch a record by global index.</summary>
    public Task<RecordInfo> GetRecordsByIndex(uint index) =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                var c = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var fn = c.GetFunction("getRecordsByIndex");
                var dto = await fn.CallDeserializingToObjectAsync<RecordDTO>(index);

                return new RecordInfo
                {
                    RecordId = (uint)dto.Id,
                    Key = dto.Key,
                    Value = dto.Value,
                    Score = dto.Score,
                    StartTime = (uint)dto.StartTime,
                    PlayerIndex = (uint)dto.PlayerIndex,
                    OtherPlayers = dto.OtherPlayers?.Select(p => (uint)p).ToArray() ?? Array.Empty<uint>()
                };
            },
            defaultValue: new RecordInfo
            {
                RecordId = 0,
                Key = string.Empty,
                Value = string.Empty,
                Score = 0,
                StartTime = 0,
                PlayerIndex = 0,
                OtherPlayers = Array.Empty<uint>()
            },
            _logger,
            nameof(GetRecordsByIndex)
        );

    /// <summary>@notice List all record IDs created for a player.</summary>
    public Task<uint[]> GetRecordIdsByPlayer(uint playerIndex) =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                var c = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var fn = c.GetFunction("getRecordIdsByPlayer");

                // Avoid uint[] ABI issues by reading as List<ulong> then down-casting.
                var result = await fn.CallAsync<List<ulong>>(playerIndex);
                return result.Select(x => (uint)x).ToArray();
            },
            defaultValue: Array.Empty<uint>(),
            _logger,
            nameof(GetRecordIdsByPlayer)
        );


    /* ────────── Solidity DTOs & FunctionMessages (unchanged) ────────── */

    [Function("setRecord")]
    private sealed class SetRecordFunction : FunctionMessage
    {
        [Parameter("uint256", "matchId", 1)]
        public uint MatchId { get; set; }

        [Parameter("uint256", "playerIndex", 2)]
        public uint PlayerIndex { get; set; }

        [Parameter("int256", "score", 3)]
        public int Score { get; set; }

        [Parameter("string", "key", 4)]
        public string Key { get; set; } = string.Empty;

        [Parameter("string", "value", 5)]
        public string Value { get; set; } = string.Empty;

        [Parameter("uint256[]", "otherPlayers", 6)]
        public List<uint> OtherPlayers { get; set; } = new();

        [Parameter("uint256", "startTime", 7)]
        public uint StartTime { get; set; }
    }

    [Function("batchSetRecords")]
    public sealed class BatchSetRecordsFunction : FunctionMessage
    {
        [Parameter("tuple[]", "inputs", 1)]
        public RecordInputDTO[] Inputs { get; set; } = Array.Empty<RecordInputDTO>();
    }

    [Struct("RecordInput")]
    public sealed class RecordInputDTO
    {
        [Parameter("uint256", "matchId", 1)]
        public BigInteger MatchId { get; set; }

        [Parameter("uint256", "playerIndex", 2)]
        public BigInteger PlayerIndex { get; set; }

        [Parameter("int256", "score", 3)]
        public BigInteger Score { get; set; }

        [Parameter("string", "key", 4)]
        public string Key { get; set; } = string.Empty;

        [Parameter("string", "value", 5)]
        public string Value { get; set; } = string.Empty;

        [Parameter("uint256[]", "otherPlayers", 6)]
        public List<BigInteger> OtherPlayers { get; set; } = new();

        [Parameter("uint256", "startTime", 7)]
        public BigInteger StartTime { get; set; }
    }

    [Event("RecordSet")]
    private sealed class RecordSetEventDTO : IEventDTO
    {
        [Parameter("uint256", "recordId", 1, true)]
        public BigInteger RecordId { get; set; }

        [Parameter("uint256", "playerIndex", 2, true)]
        public BigInteger PlayerIndex { get; set; }

        [Parameter("uint256", "playDataId", 3, true)]
        public BigInteger PlayDataId { get; set; }

        [Parameter("int256", "score", 4, false)]
        public int Score { get; set; }

        [Parameter("string", "key", 5, false)]
        public string Key { get; set; } = string.Empty;

        [Parameter("string", "value", 6, false)]
        public string Value { get; set; } = string.Empty;

        [Parameter("uint256[]", "otherPlayers", 7, false)]
        public List<BigInteger> OtherPlayers { get; set; } = new();

        [Parameter("uint256", "startTime", 8, false)]
        public BigInteger StartTime { get; set; }
    }

    [FunctionOutput]
    private sealed class RecordDTO
    {
        [Parameter("uint256", "id", 1)]
        public ulong Id { get; set; }

        [Parameter("string", "key", 2)]
        public string Key { get; set; } = string.Empty;

        [Parameter("string", "value", 3)]
        public string Value { get; set; } = string.Empty;

        [Parameter("int256", "score", 4)]
        public int Score { get; set; }

        [Parameter("uint256", "start", 5)]
        public ulong StartTime { get; set; }

        [Parameter("uint256", "playerIdx", 6)]
        public ulong PlayerIndex { get; set; }

        [Parameter("uint256[]", "others", 7)]
        public List<ulong> OtherPlayers { get; set; } = new();
    }
}

/* ───────── Controller-facing input model (unchanged) ───────── */

public class RecordInput
{
    public uint MatchSessionId { get; set; }
    public uint PlayerIndex { get; set; }
    public int Score { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public uint[] OtherPlayers { get; set; } = Array.Empty<uint>();
    public uint StartTime { get; set; }
}