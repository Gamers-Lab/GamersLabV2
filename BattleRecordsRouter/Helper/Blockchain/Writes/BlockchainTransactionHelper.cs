using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;

/// <summary>
/// A utility class that encapsulates transaction and event operations for smart contracts,
/// including sending transactions, decoding logs, and handling receipts.
/// </summary>
public class BlockchainTransactionHelper
{
    private readonly Web3 _web3;
    private readonly ILogger<BlockchainTransactionHelper> _logger;
    private readonly string _contractAddress;
    private readonly HexBigInteger _gasLimit;
    private readonly HexBigInteger _gasPrice;

    public BlockchainTransactionHelper(
        Web3 web3,
        string contractAddress,
        HexBigInteger gasLimit,
        HexBigInteger gasPrice,
        ILogger<BlockchainTransactionHelper> logger)
    {
        _web3 = web3;
        _contractAddress = contractAddress;
        _gasLimit = gasLimit;
        _gasPrice = gasPrice;
        _logger = logger;
    }
    
    public static Contract GetContract(Web3 web3, string abi, string contractAddress) =>
        web3.Eth.GetContract(abi, contractAddress);
}
