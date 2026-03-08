namespace BattleRecordsRouter.Siwe.Authorisation;

/*
 * Password Usage Summary:
 * 
 * | Password Type                    | Purpose                           | Used In Endpoints                                    | Returns/Grants                |
 * |----------------------------------|-----------------------------------|------------------------------------------------------|-------------------------------|
 * | AdminPassword                    | Admin user authentication         | LoginAdmin                                           | JWT with admin role           |
 * | CreatePlayerAnonymousRequest     | Player creation operations        | CreatePlayerAnon, CreatePlayerServerSide, UnauthenticatedLogin | Permission to create accounts |
 * 
 * Security Layers:
 * - AdminPassword: High privilege access (admin operations)
 * - CreatePlayerAnonymousRequest: Account creation gate (prevents unauthorized signups)
 */

public class AppSettingsModel
{
    public string JWTKey { get; set; } = ""; // jwt string
    public string AdminPassword { get; init; } = ""; // password to sign in as admin
    public string SequenceProjectId { get; init; } = ""; // sequence project ID (eg 388) for sequence JWT check
    public string ApplicationName { get; init; } = ""; // name of application for logging and JWT
    public string ApplicationAudience { get; init; } = "game-clients"; // audience for JWTs

    // used in the admin controller attribute. Dont remove. [OnlyInEnvironment("EnableAdminEndpoints")]
    // set to false in production
    public bool EnableAdminEndpoints { get; init; } = false; // enable admin endpoints

    // used in the blockchain controller attribute. Dont remove. [OnlyInEnvironment("EnableBlockchainEndpoints")]
    // set to false in production
    public bool EnableBlockchainEndpoints { get; init; } = false; // enable blockchain endpoints
    public string CreatePlayerAnonymousRequest { get; init; } = ""; // password to create player

    public bool ServerSidePlayerCreationAllowed { get; init; } = false; // allow server side player creation

    public string SteamAppid { get; init; } = ""; // steam app id

    public string SteamKey { get; init; } = ""; // steam key
}