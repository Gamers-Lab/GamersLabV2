using BattleRecordsRouter.Config;
using BattleRecordsRouter.Helper;
using BattleRecordsRouter.Models;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Microsoft.AspNetCore.Http;

namespace BattleRecordsRouter.Services;

/// <summary>
/// Login-session helpers for the <c>OnChainDataStorage</c> contract.
/// </summary>
public partial class GamersLabStorageService
{
    /* ────────────────────────── Session management ────────────────────────── */

    /// <summary>
    /// Gets diagnostic information about the blockchain connection and contract.
    /// </summary>
    /// <returns>A dictionary with diagnostic information.</returns>
    public async Task<Dictionary<string, string>> GetDiagnosticInfo()
    {
        var result = new Dictionary<string, string>();

        try
        {
            // Check network connection
            var web3 = _web3;
            var clientVersion = await web3.Client.SendRequestAsync<string>("web3_clientVersion", null);

            result["clientVersion"] = clientVersion;

            // Get current block number
            var blockNumber = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            result["blockNumber"] = blockNumber.ToString();

            // Check contract address
            result["contractAddress"] = _contractAddress;

            // Check if contract exists at the address
            var code = await web3.Eth.GetCode.SendRequestAsync(_contractAddress);
            result["contractCodeExists"] = code != "0x" ? "Yes" : "No";
            result["contractCodeLength"] = (code.Length / 2 - 1).ToString();

            // Use the working getPlayerCount function
            var contract = BlockchainTransactionHelper.GetContract(
                web3, OnChainDataStorageABI.ContractAbi, _contractAddress);

            try
            {
                var fn = contract.GetFunction("getPlayerCount");
                var count = await fn.CallAsync<uint>();
                result["getPlayerCount"] = count.ToString();
            }
            catch (Exception ex)
            {
                result["getPlayerCount_error"] = ex.Message;
            }

            // Try to get player at index 0 if any players exist
            try
            {
                var playerCountFn = contract.GetFunction("getPlayerCount");
                var playerCount = await playerCountFn.CallAsync<uint>();

                if (playerCount > 0)
                {
                    var fn = contract.GetFunction("getPlayerByIndex");
                    var dto = await fn.CallDeserializingToObjectAsync<PlayerDTO>(0u);
                    result["player0_address"] = dto.Address;
                    result["player0_id"] = dto.PlayerId;
                }
                else
                {
                    result["player0_info"] = "No players registered";
                }
            }
            catch (Exception ex)
            {
                result["getPlayerByIndex_error"] = ex.Message;
            }

            // Check contract owner
            try
            {
                var fn = contract.GetFunction("owner");
                var owner = await fn.CallAsync<string>();
                result["contractOwner"] = owner;
            }
            catch (Exception ex)
            {
                result["owner_error"] = ex.Message;
            }

            // Test other working functions
            try
            {
                var fn = contract.GetFunction("getLoginSessionCount");
                var count = await fn.CallAsync<ulong>();
                result["getLoginSessionCount"] = count.ToString();
            }
            catch (Exception ex)
            {
                result["getLoginSessionCount_error"] = ex.Message;
            }

            try
            {
                var fn = contract.GetFunction("getMatchSessionCount");
                var count = await fn.CallAsync<uint>();
                result["getMatchSessionCount"] = count.ToString();
            }
            catch (Exception ex)
            {
                result["getMatchSessionCount_error"] = ex.Message;
            }
        }
        catch (Exception ex)
        {
            result["error"] = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// @notice Create a new login session.
    /// @dev    Emits LoginSessionCreated(uint256,uint256,uint256,uint8,uint256).
    /// @return (transactionHash, sessionId)
    /// </summary>
    public async Task<(string transactionHash, ulong sessionId)> LoginSessionCreate(
        HttpContext httpContext,
        uint playerIndex,
        ulong applicationId,
        uint startTime,
        Device device)
    {
        if (!InputValidationHelper.IsTimestampValid(startTime, out var tErr))
            throw new ArgumentException(tErr, nameof(startTime));

        // Send (short lock only during send) + wait for receipt
        var (txHash, receipt) = await ContractUtils.SafeWriteSendAndWaitAsync<LoginSessionCreateFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn =>
            {
                fn.PlayerIndex = playerIndex;
                fn.ApplicationId = applicationId;
                fn.StartTime = startTime;
                fn.Device = (byte)device;
            },
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(LoginSessionCreate),
            loggingService: _loggingService,
            payload: new { playerIndex, applicationId, startTime, device },
            receiptTimeout: TimeSpan.FromMinutes(2),
            throwOnFailedReceipt: false,
            receiptPollMs: 1200
        ).ConfigureAwait(false);

        if (string.IsNullOrEmpty(txHash) || receipt is null || receipt.Status?.Value != 1)
        {
            _logger.LogWarning("LoginSessionCreate - Tx missing/failed. Tx={Tx} Status={Status}",
                txHash, receipt?.Status?.Value);
            return (string.Empty, 0ul);
        }

        // Decode LoginSessionCreated event from the same receipt (outside the lock)
        var created = _web3.Eth.GetEvent<LoginSessionCreatedEventDTO>(_contractAddress)
            .DecodeAllEventsForEvent(receipt.Logs)
            .FirstOrDefault()?.Event;

        if (created is null)
            throw new InvalidOperationException("LoginSessionCreated event not found in receipt.");

        // SessionId likely BigInteger; cast safely to ulong
        var sidBig = created.SessionId;
        if (sidBig > (System.Numerics.BigInteger)ulong.MaxValue)
            throw new OverflowException($"SessionId {sidBig} exceeds ulong.MaxValue");

        var sessionId = (ulong)sidBig;
        return (txHash, sessionId);
    }


    /// <summary>
    /// @notice End an existing session.
    /// @dev    Emits LoginSessionEnded(uint256,uint256) (not decoded here).
    /// @return transactionHash (tx hash only)
    /// </summary>
    public Task<string> LoginSessionEnd(HttpContext httpContext, ulong sessionId, uint endTime)
    {
        if (!InputValidationHelper.IsTimestampValid(endTime, out var tErr))
            throw new ArgumentException(tErr, nameof(endTime));

        return ContractUtils.SafeWriteSendAsync<LoginSessionEndFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn =>
            {
                fn.SessionId = sessionId;
                fn.EndTime = endTime;
            },
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(LoginSessionEnd),
            loggingService: _loggingService,
            payload: new { sessionId, endTime } // for logging/audit
        );
    }


    /// <summary>
    /// @notice Attach a key/value pair to a login session.
    /// @dev    Emits LoginSessionMetadataAdded(uint256,string,string) (not decoded).
    /// @return transactionHash (tx hash only)
    /// </summary>
    public Task<string> SetLoginSessionMetadata(HttpContext httpContext, ulong sessionId, string key, string value)
    {
        if (!InputValidationHelper.IsStringIsValid(key, out var kErr))
            throw new ArgumentException(kErr, nameof(key));
        if (!InputValidationHelper.IsStringIsValid(value, out var vErr))
            throw new ArgumentException(vErr, nameof(value));

        return ContractUtils.SafeWriteSendAsync<SetLoginSessionMetadataFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn =>
            {
                fn.SessionId = sessionId;
                fn.Key = key;
                fn.Value = value;
            },
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(SetLoginSessionMetadata),
            loggingService: _loggingService,
            payload: new { sessionId, key, value }
        );
    }


/* ────────────────────────── Read helpers ────────────────────────── */

    /// <summary>@return total login-session records ever created.</summary>
    public Task<ulong> GetLoginSessionCount() =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                var c = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var fn = c.GetFunction("getLoginSessionCount");
                return await fn.CallAsync<ulong>();
            },
            defaultValue: 0ul,
            _logger,
            nameof(GetLoginSessionCount),
            loggingService: _loggingService
        );

    /// <summary>@return player indices currently logged in.</summary>
    public Task<ulong[]> GetAllActiveLoginPlayers() =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                var c = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var fn = c.GetFunction("getAllActiveLoginPlayers");
                var list = await fn.CallAsync<List<ulong>>();
                return list?.ToArray() ?? Array.Empty<ulong>();
            },
            defaultValue: Array.Empty<ulong>(),
            _logger,
            nameof(GetAllActiveLoginPlayers),
            loggingService: _loggingService
        );

    /// <summary>@return IDs of all live login sessions.</summary>
    public Task<ulong[]> GetAllActiveLoginSessionIds() =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                var c = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var fn = c.GetFunction("getAllActiveLoginSessionIds");
                var list = await fn.CallAsync<List<ulong>>();
                return list?.ToArray() ?? Array.Empty<ulong>();
            },
            defaultValue: Array.Empty<ulong>(),
            _logger,
            nameof(GetAllActiveLoginSessionIds),
            loggingService: _loggingService
        );

    /// <summary>@notice Fetch a login session by ID.</summary>
    public Task<LoginSessionInfo> GetLoginSessionById(ulong sessionId) =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                var c = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var fn = c.GetFunction("getLoginSessionById");
                var dto = await fn.CallDeserializingToObjectAsync<LoginSessionDTO>(sessionId);

                return new LoginSessionInfo
                {
                    BaseData = new BaseData
                    {
                        Id = dto.BaseData.Id,
                        StartTime = dto.BaseData.StartTime,
                        EndTime = dto.BaseData.EndTime,
                        ApplicationId = dto.BaseData.ApplicationId
                    },
                    Metadata = new MetadataInfo { Keys = dto.Metadata.Keys, Values = dto.Metadata.Values },
                    Device = (Device)dto.Device,
                    PlayerIndex = dto.PlayerIndex,
                    MatchIds = dto.MatchIds
                };
            },
            defaultValue: new LoginSessionInfo(),
            _logger,
            nameof(GetLoginSessionById),
            loggingService: _loggingService
        );


    /* ────────────────────────── Solidity DTOs / events ────────────────────────── */

    #region DTOs & Events

    [FunctionOutput]
    public class LoginSessionDTO : IFunctionOutputDTO
    {
        [Parameter("tuple", "base_", 1)]
        public BaseDataDTO BaseData { get; set; } = new();

        [Parameter("tuple", "meta_", 2)]
        public MetadataDTO Metadata { get; set; } = new();

        [Parameter("uint8", "device_", 3)]
        public byte Device { get; set; }

        [Parameter("uint256", "playerIdx", 4)]
        public uint PlayerIndex { get; set; }

        [Parameter("uint256[]", "matchIds_", 5)]
        public List<ulong> MatchIds { get; set; } = new();
    }

    [Event("LoginSessionCreated")]
    private class LoginSessionCreatedEventDTO : IEventDTO
    {
        [Parameter("uint256", "sessionId", 1, true)]
        public ulong SessionId { get; set; }

        [Parameter("uint256", "playerIndex", 2, true)]
        public uint PlayerIndex { get; set; }

        [Parameter("uint256", "startTime", 3, true)]
        public uint StartTime { get; set; }

        [Parameter("uint8", "device", 4)]
        public byte Device { get; set; }

        [Parameter("uint256", "applicationId", 5)]
        public ulong ApplicationId { get; set; }
    }

    #endregion

    public async Task<Dictionary<string, object>> GetSystemDiagnostics()
    {
        var diagnostics = new Dictionary<string, object>();

        try
        {
            // Get player count
            var playerCount = await GetPlayerCount();
            diagnostics["total_players"] = playerCount;

            // Get match session count
            var matchCount = await GetActiveMatchSessionsLength();
            diagnostics["total_match_sessions"] = matchCount;

            // Get play data count
            var playDataCount = await GetPlayDataCount();
            diagnostics["total_play_data_records"] = playDataCount;

            // Get login session count
            var loginSessionCount = await GetLoginSessionCount();
            diagnostics["total_login_sessions"] = loginSessionCount;

            // Get current block number
            var blockNumber = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            diagnostics["current_block_number"] = blockNumber.Value;

            // Get contract balance
            var balance = await _web3.Eth.GetBalance.SendRequestAsync(_contractAddress);
            diagnostics["contract_balance_wei"] = balance.Value;
            diagnostics["contract_balance_eth"] = Nethereum.Util.UnitConversion.Convert.FromWei(balance.Value);

            // Contract address info
            diagnostics["contract_address"] = _contractAddress;
            diagnostics["network_id"] = await _web3.Net.Version.SendRequestAsync();

            // Timestamp
            diagnostics["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            diagnostics["timestamp_iso"] = DateTime.UtcNow.ToString("O");

            _logger.LogInformation("System diagnostics retrieved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving system diagnostics");
            diagnostics["error"] = ex.Message;
        }

        return diagnostics;
    }
}

/* ────────────────────────── FunctionMessage types ────────────────────────── */

[Function("loginSessionCreate")]
public sealed class LoginSessionCreateFunction : FunctionMessage
{
    [Parameter("uint256", "idx", 1)]
    public uint PlayerIndex { get; set; }

    [Parameter("uint256", "appId", 2)]
    public ulong ApplicationId { get; set; }

    [Parameter("uint256", "start", 3)]
    public uint StartTime { get; set; }

    [Parameter("uint8", "devc", 4)]
    public byte Device { get; set; }
}

[Function("loginSessionEnd")]
public sealed class LoginSessionEndFunction : FunctionMessage
{
    [Parameter("uint256", "sid", 1)]
    public ulong SessionId { get; set; }

    [Parameter("uint256", "end", 2)]
    public uint EndTime { get; set; }
}

[Function("setLoginSessionMetadata")]
public sealed class SetLoginSessionMetadataFunction : FunctionMessage
{
    [Parameter("uint256", "sid", 1)]
    public ulong SessionId { get; set; }

    [Parameter("string", "key", 2)]
    public string Key { get; set; } = string.Empty;

    [Parameter("string", "value", 3)]
    public string Value { get; set; } = string.Empty;
}
