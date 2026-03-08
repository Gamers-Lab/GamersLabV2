using Nethereum.ABI.FunctionEncoding.Attributes;
using BattleRecordsRouter.Config;
using BattleRecordsRouter.Helper;
using BattleRecordsRouter.Models;
using Nethereum.Contracts;

namespace BattleRecordsRouter.Services;

/// <summary>
/// ChainStorage partial class - Application related functions.
/// </summary>
public partial class GamersLabStorageService
{
    /// <summary>
    /// Sets the application name on-chain.
    /// @return transactionHash (tx hash only)
    /// </summary>
    public Task<string> SetApplication(HttpContext httpContext, string name)
    {
        if (!InputValidationHelper.IsStringIsValid(name, out var errorMessage))
            throw new ArgumentException(errorMessage, nameof(name));

        return ContractUtils.SafeWriteSendAsync<SetApplicationFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn => fn.Name = name,
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(SetApplication),
            loggingService: _loggingService,
            payload: new { name }
        );
    }


    /// <summary>
    /// Creates a new application record with version, company, and contracts.
    /// @return (transactionHash, recordId)
    /// </summary>
    public async Task<(string transactionHash, ulong recordId)> CreateApplicationRecord(
        HttpContext httpContext,
        string applicationVersion,
        string companyName,
        string[] contractAddresses)
    {
        if (!InputValidationHelper.IsStringIsValid(applicationVersion, out var errorMessage))
            throw new ArgumentException(errorMessage, nameof(applicationVersion));

        if (!InputValidationHelper.IsStringIsValid(companyName, out var companyNameError))
            throw new ArgumentException(companyNameError, nameof(companyName));

        foreach (var addr in contractAddresses)
            if (!GenericWeb3Helper.IsValidAddress(addr, out var addrErr))
                throw new ArgumentException(addrErr, nameof(contractAddresses));

        // Send + wait (short lock only for SEND; receipt + decode occur after lock)
        var (txHash, receipt) = await ContractUtils.SafeWriteSendAndWaitAsync<CreateApplicationRecordFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn =>
            {
                fn.ApplicationVersion = applicationVersion;
                fn.CompanyName = companyName;
                fn.ContractAddresses = contractAddresses.ToList();
            },
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(CreateApplicationRecord),
            loggingService: _loggingService,
            payload: new { applicationVersion, companyName, contractAddresses },
            receiptTimeout: TimeSpan.FromMinutes(2),
            throwOnFailedReceipt: false,
            receiptPollMs: 1200
        ).ConfigureAwait(false);

        // Keep original fallback semantics if anything fails
        if (string.IsNullOrEmpty(txHash) || receipt is null || receipt.Status?.Value != 1)
        {
            _logger.LogWarning("CreateApplicationRecord - Tx missing/failed. Tx={Tx} Status={Status}",
                txHash, receipt?.Status?.Value);
            return (string.Empty, ulong.MaxValue);
        }

        // Decode ApplicationRecordCreated event from the same receipt (OUTSIDE the lock)
        var created = _web3.Eth.GetEvent<ApplicationRecordCreatedEventDTO>(_contractAddress)
            .DecodeAllEventsForEvent(receipt.Logs)
            .FirstOrDefault()?.Event;

        if (created is null)
        {
            _logger.LogWarning("CreateApplicationRecord - Event not found in receipt {Tx}", txHash);
            return (txHash, ulong.MaxValue);
        }

        // RecordId might be ulong or BigInteger in your DTO — handle both
        ulong recordId;
        var ridObj = created.RecordId;
        if (ridObj is ulong ridUlong)
        {
            recordId = ridUlong;
        }

        return (txHash, recordId);
    }


    /// <summary>
    /// Sets a key/value metadata for an existing application record.
    /// @return transactionHash (tx hash only)
    /// </summary>
    public Task<string> SetApplicationRecordMetadata(
        HttpContext httpContext,
        ulong recordId,
        string key,
        string value)
    {
        if (!InputValidationHelper.IsStringIsValid(key, out var errorMessageKey))
            throw new ArgumentException(errorMessageKey, nameof(key));
        if (!InputValidationHelper.IsStringIsValid(value, out var errorMessageValue))
            throw new ArgumentException(errorMessageValue, nameof(value));

        return ContractUtils.SafeWriteSendAsync<SetApplicationRecordMetadataFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn =>
            {
                fn.RecordId = recordId;
                fn.Key = key;
                fn.Value = value;
            },
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(SetApplicationRecordMetadata),
            loggingService: _loggingService,
            payload: new { recordId, key, value }
        );
    }


    /// <summary>
    /// Updates the list of contract addresses associated with an application record.
    /// @return transactionHash (tx hash only)
    /// </summary>
    public Task<string> UpdateApplicationRecordAddresses(
        HttpContext httpContext,
        ulong recordId,
        string[] contractAddresses)
    {
        foreach (var address in contractAddresses)
            if (!GenericWeb3Helper.IsValidAddress(address, out var errorMessage))
                throw new ArgumentException(errorMessage, nameof(contractAddresses));

        return ContractUtils.SafeWriteSendAsync<UpdateApplicationRecordAddressesFunction>(
            web3: _web3,
            contractAddress: _contractAddress,
            init: fn =>
            {
                fn.RecordId = recordId;
                fn.ContractAddresses = contractAddresses.ToList();
            },
            logger: _logger,
            httpContext: httpContext,
            operationName: nameof(UpdateApplicationRecordAddresses),
            loggingService: _loggingService,
            payload: new { recordId, contractAddresses }
        );
    }


    /// <summary>Returns the latest application record ID.</summary>
    public async Task<ulong?> GetLatestApplicationRecord()
    {
        // SafeReadAsync returns ulong; implicit cast to ulong? via 'await'
        return await ContractUtils.SafeReadAsync(
            async ct =>
            {
                var contract = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var function = contract.GetFunction("getLatestApplicationRecord");
                return await function.CallAsync<ulong>();
            },
            defaultValue: ulong.MaxValue,
            _logger,
            nameof(GetLatestApplicationRecord),
            loggingService: _loggingService
        );
    }

    /// <summary>Fetches the detailed application record by its ID.</summary>
    public Task<(string transactionHash, ApplicationRecordResponse record)>
        GetApplicationRecord(ulong recordId) =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                var contract = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var fn = contract.GetFunction("getApplicationRecordByIndex");
                var dto = await fn.CallDeserializingToObjectAsync<ApplicationRecordDTO>(recordId);

                var meta = dto.Keys
                    .Select((k, i) => new
                    {
                        Key = k,
                        Values = i < dto.Values.Count ? dto.Values[i] : new List<string>()
                    })
                    .ToDictionary(x => x.Key, x => x.Values);

                return (string.Empty, new ApplicationRecordResponse
                {
                    ApplicationVersion = dto.ApplicationVersion,
                    CompanyName = dto.CompanyName,
                    ContractAddresses = dto.ContractAddresses,
                    Metadata = meta
                });
            },
            defaultValue: (string.Empty, new ApplicationRecordResponse()),
            _logger,
            nameof(GetApplicationRecord),
            loggingService: _loggingService
        );


    /// <summary>Returns the current application name and owner.</summary>
    public Task<(string transactionHash, ApplicationResponse application)> GetApplication() =>
        ContractUtils.SafeReadAsync(
            async ct =>
            {
                var contract = BlockchainTransactionHelper.GetContract(
                    _web3, OnChainDataStorageABI.ContractAbi, _contractAddress);
                var function = contract.GetFunction("application");
                var result = await function.CallDeserializingToObjectAsync<ApplicationDTO>();

                return (string.Empty, new ApplicationResponse
                {
                    Name = result.Name,
                    Owner = result.Owner
                });
            },
            defaultValue: (string.Empty, new ApplicationResponse()),
            _logger,
            nameof(GetApplication),
            loggingService: _loggingService
        );


    /* ─────────────── Events ─────────────── */

    [Event("ApplicationRecordCreated")]
    private class ApplicationRecordCreatedEventDTO : IEventDTO
    {
        [Parameter("uint256", "recordId", 1, true)]
        public ulong RecordId { get; set; }

        [Parameter("string", "applicationVersion", 2, false)]
        public string? ApplicationVersion { get; set; }

        [Parameter("string", "companyName", 3, false)]
        public string? CompanyName { get; set; }

        [Parameter("address[]", "contractAddresses", 4, false)]
        public List<string>? ContractAddresses { get; set; }
    }

    [FunctionOutput]
    public class ApplicationRecordDTO : IFunctionOutputDTO
    {
        [Parameter("string", "ver", 1)]
        public string ApplicationVersion { get; set; } = string.Empty;

        [Parameter("string", "company", 2)]
        public string CompanyName { get; set; } = string.Empty;

        [Parameter("address[]", "contracts_", 3)]
        public List<string> ContractAddresses { get; set; } = new();

        [Parameter("string[]", "keys", 4)]
        public List<string> Keys { get; set; } = new();

        [Parameter("string[][]", "vals", 5)]
        public List<List<string>> Values { get; set; } = new();
    }

    [FunctionOutput]
    private class ApplicationDTO
    {
        [Parameter("string", "name", 1)]
        public string Name { get; set; } = string.Empty;

        [Parameter("address", "owner", 2)]
        public string Owner { get; set; } = string.Empty;
    }

    [Function("setApplication")]
    public sealed class SetApplicationFunction : FunctionMessage
    {
        [Parameter("string", "_name", 1)]
        public string Name { get; set; } = string.Empty;
    }

    [Function("createApplicationRecord")]
    public sealed class CreateApplicationRecordFunction : FunctionMessage
    {
        [Parameter("string", "_ver", 1)]
        public string ApplicationVersion { get; set; } = string.Empty;

        [Parameter("string", "_company", 2)]
        public string CompanyName { get; set; } = string.Empty;

        [Parameter("address[]", "_contracts", 3)]
        public List<string> ContractAddresses { get; set; } = new();
    }

    [Function("setApplicationRecordMetadata")]
    public sealed class SetApplicationRecordMetadataFunction : FunctionMessage
    {
        [Parameter("uint256", "_id", 1)]
        public ulong RecordId { get; set; }

        [Parameter("string", "_key", 2)]
        public string Key { get; set; } = string.Empty;

        [Parameter("string", "_val", 3)]
        public string Value { get; set; } = string.Empty;
    }

    [Function("updateApplicationRecordAddresses")]
    public sealed class UpdateApplicationRecordAddressesFunction : FunctionMessage
    {
        [Parameter("uint256", "_id", 1)]
        public ulong RecordId { get; set; }

        [Parameter("address[]", "_contracts", 2)]
        public List<string> ContractAddresses { get; set; } = new();
    }
}