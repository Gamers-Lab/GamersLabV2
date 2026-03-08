namespace BattleRecordsRouter.Config;

public class BlockchainConfig
{
    public string NodeUrl { get; set; } = "http://127.0.0.1:8545"; // Default Hardhat node URL
    public string DefaultAccount { get; set; } = string.Empty;
    public int DefaultGasLimit { get; set; } = 900000000;
    public long DefaultGasPrice { get; set; } = 90000000000; // 20 Gwei
    public long ChainId { get; set; } = 31337; // Default Hardhat chain ID

    // NEW: Mode selection
    public bool BlockchainEnabled { get; set; } = true;

    // Account information
    public AccountInfo AdminAccount { get; set; } = new();
    public AccountInfo ModeratorAccount { get; set; } = new();

    // Contract addresses
    public string OnChainDataStorageAddress { get; set; } = string.Empty;
}

public class AccountInfo
{
    public string Address { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
}
