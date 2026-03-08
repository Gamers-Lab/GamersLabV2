using BattleRecordsRouter.Config;
using BattleRecordsRouter.Helper;
using BattleRecordsRouter.Services.Database;
using Microsoft.Extensions.Options;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Web3;

namespace BattleRecordsRouter.Services
{
    public partial class GamersLabStorageService : IGamersLabStorageService
    {
        private readonly Web3 _web3;
        private readonly BlockchainConfig _config;
        private readonly ILogger<GamersLabStorageService> _logger;
        private readonly string _contractAddress;
        private readonly IBlockchainLoggingService _loggingService;
        private readonly SharedNonceService _sharedNonceService;

        public GamersLabStorageService(
            IOptions<BlockchainConfig> config,
            ILogger<GamersLabStorageService> logger,
            BlockchainTransactionHelper txHelper,
            Web3 web3,
            IBlockchainLoggingService loggingService,
            SharedNonceService sharedNonceService)
        {
            _config = config.Value;
            _logger = logger;
            _web3 = web3;
            _loggingService = loggingService;
            _sharedNonceService = sharedNonceService;
            _contractAddress = _config.OnChainDataStorageAddress;
            _logger.LogInformation("ChainStorage service initialized with contract address: {ContractAddress}",
                _contractAddress);
        }
        
        [FunctionOutput]
        public class BaseDataDTO : IFunctionOutputDTO
        {
            [Parameter("uint256", "id", 1)]
            public ulong Id { get; set; }
        
            [Parameter("uint256", "startTime", 2)]
            public uint StartTime { get; set; }
        
            [Parameter("uint256", "endTime", 3)]
            public uint EndTime { get; set; }
        
            [Parameter("uint256", "applicationId", 4)]
            public ulong ApplicationId { get; set; }
        }

        [FunctionOutput]
        public class MetadataDTO : IFunctionOutputDTO
        {
            [Parameter("string[]", "key", 1)]
            public List<string> Keys { get; set; } = new();

            [Parameter("string[][]", "values", 2)]
            public List<List<string>> Values { get; set; } = new();
        }
    }
}