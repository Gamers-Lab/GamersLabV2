namespace BattleRecordsRouter.Siwe.Authorisation;

/// <summary>
/// GamersLab authorisation service – handles login via nonce + signature, Sequence wallet JWTs,
/// and admin passwords. Issues secure JWTs with embedded player metadata.
/// </summary>
public interface IGamersLabAuthorisationService
{
    /// <summary>
    /// Extracts the wallet address from a JWT issued by this service.
    /// </summary>
    /// <param name="token">A previously issued JWT.</param>
    /// <returns>The wallet address if valid, otherwise null.</returns>
    string? GetWalletFromToken(string token);

    /// <summary>
    /// Authenticates a user by verifying a Sequence-issued JWT and issues a new platform JWT.
    /// </summary>
    /// <param name="sequenceJwt">A JWT from the Sequence wallet system.</param>
    /// <returns>The issued JWT on success, or null.</returns>
    Task<(string? Jwt, GamersLabAuthorisationService.SequenceJwtAuthResult Status)> AuthenticateWithSequenceJwtAsync(
        string sequenceJwt);

    /// <summary>
    /// Authenticates an admin using two passwords and returns a privileged JWT.
    /// </summary>
    /// <param name="password">The first password (could be static).</param>
    /// <returns>An admin-signed JWT on success, or null.</returns>
    Task<string?> AuthenticateAdminPassword(string password);

    /// <summary>
    /// Authenticates a simple password for trying to create a new account.
    /// </summary>
    /// <param name="password"></param>
    /// <returns></returns>
    Task<bool> AuthenticateAccountCreationPassword(string password);

    /// <summary>
    /// Check if server side player creation is allowed
    /// </summary>
    /// <returns></returns>
    Task<bool> AllowServerSidePlayerCreation();

    /// <summary>
    /// Checks whether the provided password matches the stored application-level password.
    /// </summary>
    /// <param name="password">User input to check.</param>
    /// <returns>True if valid, false otherwise.</returns>
    bool IsApplicationPasswordValid(string password);

    /// <summary>
    /// Refresh the JWT access token. Up to 24 times
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<string?> RefreshAccessTokenAsync(string token);

    /// <summary>
    /// Authenticates a user using a Steam ticket and returns a new JWT.
    /// </summary>
    /// <param name="steamTicket"></param>
    /// <returns></returns>
    Task<string?> AuthenticateSteamTicketAsync(string sessionTicket, string sessionUsername);

    /// <summary>
    /// Generate a JWT for a player index. Wrapper for internal function.
    /// </summary>
    /// <param name="playerIndex"></param>
    /// <returns></returns>
    Task<string?> GenerateJwtFromPlayerIndexAsync(uint playerIndex);
}