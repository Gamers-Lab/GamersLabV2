// namespace BattleRecordsRouter.Helper;
//
// using System.Threading;
// using System.Threading.Tasks;
// using Nethereum.Hex.HexTypes;
// using Nethereum.JsonRpc.Client;
// using Nethereum.RPC.Eth.Transactions;
// using Nethereum.RPC.NonceServices;
//
// public sealed class CachedNonceService : INonceService
// {
//     private readonly IClient _client;
//     private readonly string _account;
//     private readonly SemaphoreSlim _gate = new(1, 1);
//
//     public bool UseLatestTransactionsOnly { get; set; } = false;
//     public System.Numerics.BigInteger CurrentNonce { get; set; } = -1;
//
//     public CachedNonceService(string account, IClient client)
//     {
//         _account = account;
//         _client  = client;
//     }
//
//     public async Task<HexBigInteger> GetNextNonceAsync()
//     {
//         var ethGetTransactionCount = new EthGetTransactionCount(_client);
//         await _gate.WaitAsync().ConfigureAwait(false);
//         try
//         {
//             if (CurrentNonce < 0)
//             {
//                 var block = UseLatestTransactionsOnly
//                     ? Nethereum.RPC.Eth.DTOs.BlockParameter.CreateLatest()
//                     : Nethereum.RPC.Eth.DTOs.BlockParameter.CreatePending();
//
//                 var fetched = await ethGetTransactionCount.SendRequestAsync(_account, block)
//                     .ConfigureAwait(false);
//                 CurrentNonce = fetched.Value;
//             }
//             else
//             {
//                 CurrentNonce = CurrentNonce + 1;
//             }
//
//             return new HexBigInteger(CurrentNonce);
//         }
//         finally
//         {
//             _gate.Release();
//         }
//     }
//
//     public async Task ResetNonceAsync()
//     {
//         await _gate.WaitAsync().ConfigureAwait(false);
//         try { CurrentNonce = -1; }
//         finally { _gate.Release(); }
//     }
//
//     public IClient Client { get; set; }
// }
