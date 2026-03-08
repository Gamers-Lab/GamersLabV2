using BattleRecordsRouter.Models;

namespace BattleRecordsRouter.Repositories;

public interface IPlayerCredentialDBServices
{
    /// <summary>
    /// Creates a new player credential record in the database.
    /// </summary>
    /// <param name="address">Ethereum wallet address</param>
    /// <param name="privateKey">Private key for the wallet</param>
    /// <param name="immutablePlayerIdentifier">External identifier (e.g., Steam ID)</param>
    /// <returns>The created record</returns>
    Task<PlayerCredentialRecord> CreateAsync(string address, string privateKey, string immutablePlayerIdentifier);

    /// <summary>
    /// Checks if a player credential exists for the given immutable player identifier.
    /// </summary>
    /// <param name="immutablePlayerIdentifier">External identifier to check</param>
    /// <returns>The player credential record if found, otherwise null</returns>
    Task<PlayerCredentialRecord?> PeekByIdentifierAsync(string immutablePlayerIdentifier);
    
    /// <summary>
    /// Checks if a player credential exists for the given address.
    /// </summary>
    /// <param name="address">Ethereum wallet address to check</param>
    /// <returns>The player credential record if found, otherwise null</returns>
    Task<PlayerCredentialRecord?> PeekByAddressAsync(string address);
}