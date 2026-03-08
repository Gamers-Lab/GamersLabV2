﻿﻿using System.Numerics;
using BattleRecordsRouter.Config;
using Microsoft.Extensions.Options;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

namespace BattleRecordsRouter.Services;

public class GenericGenericBlockchainService : IGenericBlockchainService
{
    private readonly Web3 _web3;
    private readonly BlockchainConfig _config;
    private readonly ILogger<GenericGenericBlockchainService> _logger;

    public GenericGenericBlockchainService(IOptions<BlockchainConfig> config, ILogger<GenericGenericBlockchainService> logger)
    {
        _config = config.Value;
        _logger = logger;

        // Initialize Web3 with the node URL
        _web3 = string.IsNullOrEmpty(_config.DefaultAccount)
            ? new Web3(_config.NodeUrl)
            : new Web3(new Account(_config.DefaultAccount), _config.NodeUrl);

        _logger.LogInformation("Blockchain service initialized with node URL: {NodeUrl}", _config.NodeUrl);
    }

    public async Task<string> GetAccountBalance(string address)
    {
        try
        {
            var balance = await _web3.Eth.GetBalance.SendRequestAsync(address);
            return Web3.Convert.FromWei(balance).ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting balance for address {Address}", address);
            throw;
        }
    }

    public async Task<string> CallContractFunction(string contractAddress, string abi, string functionName, params object[] functionInput)
    {
        try
        {
            var contract = _web3.Eth.GetContract(abi, contractAddress);
            var function = contract.GetFunction(functionName);
            var result = await function.CallAsync<object>(functionInput);
            return result?.ToString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling contract function {FunctionName} at address {ContractAddress}",
                functionName, contractAddress);
            throw;
        }
    }

    public async Task<string> SendTransactionToContract(string contractAddress, string abi, string functionName, params object[] functionInput)
    {
        try
        {
            var contract = _web3.Eth.GetContract(abi, contractAddress);
            var function = contract.GetFunction(functionName);

            var transactionInput = new TransactionInput
            {
                From = _web3.TransactionManager.Account.Address,
                Gas = new HexBigInteger(_config.DefaultGasLimit),
                GasPrice = new HexBigInteger(_config.DefaultGasPrice)
            };

            var transactionHash = await function.SendTransactionAsync(transactionInput, functionInput);
            return transactionHash;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending transaction to contract function {FunctionName} at address {ContractAddress}",
                functionName, contractAddress);
            throw;
        }
    }

    public async Task<BigInteger> GetBlockNumber()
    {
        try
        {
            return await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting block number");
            throw;
        }
    }

    public async Task<Dictionary<string, object>> GetContractData(string contractAddress, string abi)
    {
        try
        {
            var contract = _web3.Eth.GetContract(abi, contractAddress);
            var result = new Dictionary<string, object>();

            // Get all functions that are view/pure (read-only)
            var functions = contract.ContractBuilder.ContractABI.Functions
                .Where(f => f.Constant)
                .ToList();

            foreach (var function in functions)
            {
                if (function.InputParameters.Length == 0)
                {
                    try
                    {
                        var contractFunction = contract.GetFunction(function.Name);
                        var output = await contractFunction.CallDecodingToDefaultAsync();
                        result.Add(function.Name, output);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error calling function {FunctionName}", function.Name);
                        result.Add(function.Name, $"Error: {ex.Message}");
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting contract data for address {ContractAddress}", contractAddress);
            throw;
        }
    }

    public async Task<ulong> GetContractTransactionCount(string contractAddress)
    {
        try
        {
            // Get current block number
            var currentBlock = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            
            // Get transaction count for the contract address
            // This gets the nonce (number of transactions sent FROM this address)
            var nonce = await _web3.Eth.Transactions.GetTransactionCount.SendRequestAsync(contractAddress);
            
            // For a more accurate count of transactions TO the contract, we'd need to scan blocks
            // But nonce gives us transactions FROM the contract (which is still useful)
            return (ulong)nonce.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transaction count for contract {ContractAddress}", contractAddress);
            return 0;
        }
    }
}
