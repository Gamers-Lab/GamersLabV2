using System.Numerics;

namespace BattleRecordsRouter.Services;

public interface IGenericBlockchainService
{
    Task<string> GetAccountBalance(string address);
    Task<string> CallContractFunction(string contractAddress, string abi, string functionName, params object[] functionInput);
    Task<string> SendTransactionToContract(string contractAddress, string abi, string functionName, params object[] functionInput);
    Task<BigInteger> GetBlockNumber();
    Task<Dictionary<string, object>> GetContractData(string contractAddress, string abi);
    Task<ulong> GetContractTransactionCount(string contractAddress);
}
