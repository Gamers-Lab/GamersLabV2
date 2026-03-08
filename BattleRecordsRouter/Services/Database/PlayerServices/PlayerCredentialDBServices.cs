using BattleRecordsRouter.Models;
using Nethereum.Util;
using Supabase;
using System.Text.RegularExpressions;

namespace BattleRecordsRouter.Repositories;

/// <summary>
/// Service for managing server-side player wallet credentials for walletless authentication.
/// Stores Ethereum wallet addresses and private keys for players who authenticate via external services (Steam, Epic, etc.).
/// </summary>
/// <remarks>
/// **Use Case**: Server-Side Player Creation (Walletless Players)
///
/// When players authenticate via external services (Steam, Epic, etc.) instead of connecting a wallet:
/// 1. System generates a new Ethereum wallet for the player
/// 2. Private key is stored in database (encrypted at rest by Supabase)
/// 3. Player's external ID (Steam ID, etc.) is linked to the wallet address
/// 4. Server uses stored private key to sign blockchain transactions on player's behalf
///
/// **Security Considerations**:
/// - Private keys are stored in plaintext in the database (acceptable for low-value wallets)
/// - Database encryption at rest is handled by Supabase
/// - Access to this table should be restricted to server-side operations only
/// - Never expose private keys in API responses
///
/// **Workflow**:
/// - CreatePlayerServerSide endpoint → Generates wallet → Stores credentials here
/// - UnauthenticatedLogin endpoint → Retrieves credentials → Signs transactions
///
/// Thread Safety: Safe for concurrent use via Supabase client.
/// </remarks>
public sealed class PlayerCredentialDBServices : IPlayerCredentialDBServices
{
    private readonly Client _supabase;
    private readonly AddressUtil _addressUtil;

    /// <summary>
    /// Initializes a new instance of the PlayerCredentialDBServices class.
    /// </summary>
    /// <param name="supabase">Supabase client for database operations</param>
    public PlayerCredentialDBServices(Client supabase)
    {
        _supabase = supabase;
        _addressUtil = new AddressUtil();
    }

    /// <summary>
    /// Creates a new player credential record in the database with wallet address and private key.
    /// </summary>
    /// <param name="address">Ethereum wallet address (will be converted to checksum format)</param>
    /// <param name="privateKey">Private key for the wallet (will be prefixed with 0x if missing)</param>
    /// <param name="immutablePlayerIdentifier">External player identifier (e.g., Steam ID, Epic ID)</param>
    /// <returns>The created PlayerCredentialRecord with database-assigned ID</returns>
    /// <exception cref="ArgumentException">Thrown if private key format is invalid</exception>
    /// <exception cref="InvalidOperationException">Thrown if database insert fails or duplicate record exists</exception>
    /// <remarks>
    /// This method:
    /// 1. Validates and normalizes the private key format (must be 64 hex chars + 0x prefix)
    /// 2. Converts address to checksum format for consistency
    /// 3. Checks for duplicate records (by address or identifier)
    /// 4. Inserts the credential record into Supabase
    ///
    /// **Security**: Private key is stored in plaintext. Ensure database has encryption at rest enabled.
    /// </remarks>
    public async Task<PlayerCredentialRecord> CreateAsync(string address, string privateKey, string immutablePlayerIdentifier)
    {
        // Normalize address to checksum format
        address = _addressUtil.ConvertToChecksumAddress(address);

        // Normalize and validate private key format
        if (!privateKey.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            privateKey = "0x" + privateKey;
        }

        // Validate private key is 66 characters (0x + 64 hex chars)
        if (privateKey.Length != 66 || !Regex.IsMatch(privateKey, "^0x[0-9a-fA-F]{64}$"))
        {
            throw new ArgumentException(
                "Private key must be 64 hexadecimal characters (with or without 0x prefix). " +
                $"Received length: {privateKey.Length - 2} chars after 0x prefix.",
                nameof(privateKey));
        }

        // Check for duplicate address
        var existingByAddress = await PeekByAddressAsync(address);
        if (existingByAddress != null)
        {
            throw new InvalidOperationException(
                $"A player credential already exists for address {address}. " +
                $"Existing record ID: {existingByAddress.Id}, Identifier: {existingByAddress.ImmutablePlayerIdentifier}");
        }

        // Check for duplicate identifier
        var existingByIdentifier = await PeekByIdentifierAsync(immutablePlayerIdentifier);
        if (existingByIdentifier != null)
        {
            throw new InvalidOperationException(
                $"A player credential already exists for identifier '{immutablePlayerIdentifier}'. " +
                $"Existing record ID: {existingByIdentifier.Id}, Address: {existingByIdentifier.Address}");
        }

        var record = new PlayerCredentialRecord
        {
            Address = address,
            PrivateKey = privateKey,
            ImmutablePlayerIdentifier = immutablePlayerIdentifier,
            CreatedAt = DateTime.UtcNow
        };

        var response = await _supabase
            .From<PlayerCredentialRecord>()
            .Insert(record);

        // Validate insert succeeded
        if (response.Models == null || response.Models.Count == 0)
        {
            throw new InvalidOperationException(
                $"Failed to insert player credential for identifier '{immutablePlayerIdentifier}'. " +
                $"Supabase response: {response.ResponseMessage}");
        }

        return response.Models.First();
    }

    /// <summary>
    /// Retrieves a player credential record by external player identifier (e.g., Steam ID).
    /// </summary>
    /// <param name="immutablePlayerIdentifier">External player identifier to search for</param>
    /// <returns>The matching PlayerCredentialRecord if found, otherwise null</returns>
    /// <remarks>
    /// Use this method to check if a player already has server-side credentials before creating new ones.
    /// Common use case: Player logs in via Steam → Check if credentials exist → Use existing or create new.
    /// </remarks>
    public async Task<PlayerCredentialRecord?> PeekByIdentifierAsync(string immutablePlayerIdentifier)
    {
        var response = await _supabase
            .From<PlayerCredentialRecord>()
            .Where(x => x.ImmutablePlayerIdentifier == immutablePlayerIdentifier)
            .Get();

        return response.Models.FirstOrDefault();
    }

    /// <summary>
    /// Retrieves a player credential record by Ethereum wallet address.
    /// </summary>
    /// <param name="address">Ethereum wallet address to search for (will be converted to checksum format)</param>
    /// <returns>The matching PlayerCredentialRecord if found, otherwise null</returns>
    /// <remarks>
    /// Address is automatically normalized to checksum format before querying.
    /// Use this method to check if a wallet address is already associated with server-side credentials.
    /// </remarks>
    public async Task<PlayerCredentialRecord?> PeekByAddressAsync(string address)
    {
        // Normalize address to checksum format for consistent querying
        address = _addressUtil.ConvertToChecksumAddress(address);

        var response = await _supabase
            .From<PlayerCredentialRecord>()
            .Where(x => x.Address == address)
            .Get();

        return response.Models.FirstOrDefault();
    }
}