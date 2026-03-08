using BattleRecordsRouter.Models;
using BattleRecordsRouter.Repositories;
using Nethereum.Contracts;

namespace BattleRecordsRouter.Services.Database;

public interface IBlockchainLoggingService : IErrorLogDBServices, IWriteOperationLogger
{
    // Inherits all methods from both interfaces
}