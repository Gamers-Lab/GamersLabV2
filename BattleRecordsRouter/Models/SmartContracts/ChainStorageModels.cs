using System.ComponentModel;
using System.Numerics;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;

namespace BattleRecordsRouter.Models;

public class SetApplicationRequest
{
    public required string Name { get; set; }
}

public class SetRecordRequest
{
    public uint MatchSessionId { get; set; }
    public int Score { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    [DefaultValue(null)]
    [SwaggerSchema(Description = "Player index override (admin only). If not provided, uses the player index from JWT. Optional.")]
    public uint? PlayerIndex { get; set; }

    [DefaultValue(null)]
    [SwaggerSchema(Description = "Other player indices. Optional.")]
    public uint[]? OtherPlayers { get; set; }

    [DefaultValue(null)]
    [SwaggerSchema(Description = "Unix timestamp. Optional.")]
    public uint? StartTime { get; set; }
}

public class RecordInfo
{
    public required uint RecordId { get; init; }
    public required string Key { get; init; }
    public required string Value { get; init; }
    public required int Score { get; init; }
    public required uint StartTime { get; init; }
    public required uint PlayerIndex { get; init; }
    public required uint[] OtherPlayers { get; init; }
}

public class AddPlayerToMatchSessionRequest
{
    public required uint MatchSessionId { get; set; }

    [DefaultValue(null)]
    [SwaggerSchema(Description = "Player index override (admin only). If not provided, uses the player index from JWT. Optional.")]
    public uint? PlayerIndex { get; set; }

    [DefaultValue(null)]
    [SwaggerSchema(Description = "Unix timestamp. Optional.")]
    public uint? StartTime { get; set; }
}

public class UpdatePlayDataRequest
{
    public required uint MatchSessionId { get; set; }

    public required int Score { get; set; }
    public required byte WinLoss { get; set; }
    public required int MatchPosition { get; set; }

    [DefaultValue(null)]
    [SwaggerSchema(Description = "Unix timestamp. Optional.")]
    public uint? EndTime { get; set; }

    [DefaultValue(null)]
    [SwaggerSchema(Description = "Player index override (admin only). If not provided, uses the player index from JWT. Optional.")]
    public uint? PlayerIndex { get; set; }
}

public class PlayDataResponse
{
    public string TransactionHash { get; set; } = string.Empty;
    public uint PlayDataId { get; set; }
    public uint PlayerIndex { get; set; }
}

public class CreateApplicationRecordRequest
{
    public required string ApplicationVersion { get; set; }
    public required string CompanyName { get; set; }
    public required string[] ContractAddresses { get; set; }
}

public class CreateLoginSessionResponse
{
    public string TransactionHash { get; set; } = string.Empty;
    public ulong SessionId { get; set; }
}

public class TransactionResponse
{
    public string TransactionHash { get; set; } = string.Empty;
}

public enum PlayerType
{
    Human = 0,
    AI = 1,
    Last = 2
}

public class SetApplicationRecordMetadataRequest
{
    public required ulong RecordId { get; set; }
    public required string Key { get; set; }
    public required string Value { get; set; }
}

public class LoginSessionMetadataRequest
{
    public required ulong SessionId { get; set; }
    public required string Key { get; set; }
    public required string Value { get; set; }
}

/// <summary>
/// The type of device a player is using.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))] // optional for string display
public enum Device : byte
{
    /// <summary>iOS device</summary>
    iOS = 0,

    /// <summary>Android device</summary>
    Android = 1,

    /// <summary>WebGL browser</summary>
    WebGL = 2,

    /// <summary>PC / Desktop</summary>
    PC = 3,

    /// <summary>Console (e.g., Xbox, PlayStation)</summary>
    Console = 4,

    /// <summary>Other / Unknown</summary>
    Other = 5,

    /// <summary>Reserved internal enum end marker</summary>
    Last = 6
}

public class LoginSessionRequest
{
    [DefaultValue(Device.PC)]
    [SwaggerSchema(Description = "Device type (e.g. PC, Android, iOS)")]
    public required Device Device { get; set; }

    [DefaultValue(null)]
    [SwaggerSchema(Description = "Player index override (admin only). If not provided, uses the player index from JWT. Optional.")]
    public uint? PlayerIndex { get; set; }

    [DefaultValue(null)]
    [SwaggerSchema(Description = "Application number, Optional.")]
    public uint? ApplicationId { get; set; }

    [DefaultValue(null)]
    [SwaggerSchema(Description = "Unix timestamp. Optional.")]
    public uint? StartTime { get; set; }
}

public class LoginSessionEndRequest
{
    public required ulong SessionId { get; set; }

    [DefaultValue(null)]
    [SwaggerSchema(Description = "Unix timestamp. Optional.")]
    public uint? EndTime { get; set; }
}

public class StartMatchSessionRequest
{
    public required string Level { get; set; }

    [DefaultValue(null)]
    public uint[]? PlayerIds { get; set; }

    [DefaultValue(null)]
    [SwaggerSchema(Description = "Application number, Optional.")]
    public ulong? ApplicationId { get; set; }

    [DefaultValue(null)]
    [SwaggerSchema(Description = "Unix timestamp. Optional.")]
    public uint? StartTime { get; set; }
}

public class EndMatchSessionRequestMultiplayer
{
    public required uint MatchSessionId { get; set; }

    [DefaultValue(null)]
    [SwaggerSchema(Description = "Unix timestamp. Optional.")]
    public uint? EndTime { get; set; }
}

public class EndMatchSessionSingleplayer
{
    public required uint MatchSessionId { get; set; }

    [DefaultValue(null)]
    [SwaggerSchema(Description = "Unix timestamp. Optional.")]
    public uint? EndTime { get; set; }
}

public class MatchSessionMetadataRequest
{
    public required uint MatchSessionId { get; set; }
    public required string Key { get; set; }
    public required string Value { get; set; }
}

public class CreateMatchSessionResponse
{
    public string TransactionHash { get; set; } = string.Empty;
    public uint MatchSessionId { get; set; }
    public uint[] PlayDataIds { get; set; } = Array.Empty<uint>();
}

public class UpdateApplicationRecordAddressesRequest
{
    public required ulong RecordId { get; set; }
    public required string[] ContractAddresses { get; set; }
}

public class ApplicationRecordResponse
{
    public string ApplicationVersion { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>Addresses of the related contracts (new field).</summary>
    public List<string> ContractAddresses { get; set; } = new();

    /// <summary>Arbitrary metadata key → list of values.</summary>
    public Dictionary<string, List<string>> Metadata { get; set; } = new();
}

public class ActiveMatchSessionsResponse
{
    public uint[] SessionIds { get; set; } = Array.Empty<uint>();
}

public class ActiveMatchSessionsLengthResponse
{
    public uint Length { get; set; }
}

/// <summary>
/// Generic response model for transactions that return an ID and a transaction hash
/// </summary>
/// <typeparam name="T"></typeparam>
public class TransactionWithIdResponse<T>
{
    public string TransactionHash { get; set; } = string.Empty;
    public T Id { get; set; } = default!;
}

public class ApplicationResponse
{
    public string Name { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
}

public class LoginSessionInfo
{
    public BaseData BaseData { get; set; } = new();
    public MetadataInfo Metadata { get; set; } = new();

    public Device Device { get; set; } = Device.Other;
    public uint PlayerIndex { get; set; }

    // ✅ Added this to reflect `matchIds_` from the ABI
    public List<ulong> MatchIds { get; set; } = new();
}

public class MatchSessionInfo
{
    public BaseData BaseData { get; set; } = new();
    public MetadataInfo Metadata { get; set; } = new();
    public bool Multiplayer { get; set; }
    public List<uint> PlayerIds { get; set; } = new();
    public List<uint> PlayData { get; set; } = new();
    public string Level { get; set; } = string.Empty;
}

public class BaseData
{
    public ulong Id { get; set; }
    public uint StartTime { get; set; }
    public uint EndTime { get; set; }
    public ulong ApplicationId { get; set; }
}

public class MetadataInfo
{
    public List<string> Keys { get; set; } = new();
    public List<List<string>> Values { get; set; } = new();
}

public class PlayDataInfo
{
    public BaseData BaseData { get; set; } = new();
    public MetadataInfo Metadata { get; set; } = new();

    public uint PlayerIndex { get; set; }
    public int Score { get; set; }
    public WinLoss WinLoss { get; set; }
    public int MatchPosition { get; set; }
    public List<ulong> RecordIds { get; set; } = new();
}

/// <summary>
/// Represents the outcome of a match for a player
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WinLoss : byte
{
    /// <summary>Player won the match</summary>
    [Description("Player won the match")]
    Win = 0,

    /// <summary>Player lost the match</summary>
    [Description("Player lost the match")]
    Loss = 1,

    /// <summary>Player forfeited/abandoned the match</summary>
    [Description("Player forfeited the match")]
    Forfeit = 2,

    /// <summary>Match ended in a draw</summary>
    [Description("Match ended in a draw")]
    Draw = 3,

    /// <summary>Outcome not yet determined</summary>
    [Description("Outcome not yet determined")]
    NotSet = 4,

    /// <summary>Player failed to complete the match</summary>
    [Description("Player failed to complete the match")]
    Fail = 5,

    /// <summary>Reserved internal enum end marker</summary>
    [Description("Reserved internal enum end marker")]
    Last = 6
}

public class PlayerResponse
{
    public string PlayerId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public PlayerType PlayerType { get; set; }
    public BigInteger Score { get; set; }
    public Dictionary<string, string> Identifiers { get; set; } = new();
    public List<string> VerifiedAddresses { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public class PlayerApiResponse
{
    public string PlayerId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public PlayerType PlayerType { get; set; }
    public ulong Score { get; set; }
    public Dictionary<string, string> Identifiers { get; set; } = new();
    public List<string> VerifiedAddresses { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public class GetPlayerCountResponse
{
    public uint Count { get; set; }
}

public class CreatePlayerRequest
{
    [SwaggerSchema(Description = "Unique player name")]
    public required string PlayerId { get; set; }

    [SwaggerSchema(Description = "Player address from wallet")]
    public required string PlayerAddress { get; set; }

    [DefaultValue(null)]
    [SwaggerSchema(Description = "Player type. Optional.")]
    public PlayerType? PlayerType { get; set; }
}

public class CreatePlayerAnonymousRequest
{
    [SwaggerSchema(Description = "Unique player name")]
    public required string PlayerId { get; set; }

    [SwaggerSchema(Description = "Player address from wallet")]
    public required string PlayerAddress { get; set; }

    [SwaggerSchema(Description = "Password")]
    public required string SignInPassword { get; set; }
}

public class CreatePlayerServerSideRequest
{
    [SwaggerSchema(Description = "Immutable player identifier (e.g., Steam ID)")]
    public required string ImmutablePlayerIdentifier { get; set; }

    [SwaggerSchema(Description = "Player username. Optional.")]
    public required string PlayerUsername { get; set; }

    [SwaggerSchema(Description = "Set to null to generate a new address")]
    public required string PlayerAddress { get; set; }

    [SwaggerSchema(Description = "Game Verification Password")]
    public required string GameVerificationPassword { get; set; }
}

/// <summary>
/// Response model for player creation
/// </summary>
public class CreatePlayerResponse
{
    /// <summary>
    /// Transaction hash of the creation operation
    /// </summary>
    /// <example>0x123...abc</example>
    public string TransactionHash { get; set; } = string.Empty;

    /// <summary>
    /// Index assigned to the created player
    /// </summary>
    /// <example>1</example>
    public uint PlayerIndex { get; set; }
}

public class UpdatePlayerScoreRequest
{
    public required int NewScore { get; set; }

    [DefaultValue(null)]
    [SwaggerSchema(Description = "Player index override (admin only). If not provided, uses the player index from JWT. Optional.")]
    public uint? PlayerIndex { get; set; }
}

public class UpdatePlayerScoreResponse
{
    public string TransactionHash { get; set; } = string.Empty;
    public uint PlayerIndex { get; set; }
    public int NewScore { get; set; }
}

public class AddVerifiedAddressRequest
{
    public required string VerifiedAddress { get; set; }

    [DefaultValue(null)]
    [SwaggerSchema(Description = "Player index override (admin only). If not provided, uses the player index from JWT. Optional.")]
    public uint? PlayerIndex { get; set; }
}

public class AddVerifiedAddressResponse
{
    public string TransactionHash { get; set; } = string.Empty;
    public uint PlayerIndex { get; set; }
    public string VerifiedAddress { get; set; } = string.Empty;
}

public class AddPlayerIdentifierRequest
{
    public required string IdentifierType { get; set; }
    public required string IdentifierValue { get; set; }

    [DefaultValue(null)]
    [SwaggerSchema(Description = "Player index override (admin only). If not provided, uses the player index from JWT. Optional.")]
    public uint? PlayerIndex { get; set; }
}

public class AddPlayerIdentifierResponse
{
    public string TransactionHash { get; set; } = string.Empty;
}

public class GetPlayerIndexResponse
{
    public uint PlayerIndex { get; set; }
}

public class GetPlayerUniqueIdResponse
{
    public string PlayerId { get; set; } = string.Empty;
}

public class BatchSetRecordsRequest
{
    public SetRecordRequest[] Records { get; set; } = Array.Empty<SetRecordRequest>();
}

public class BatchRecordResponse
{
    public string TransactionHash { get; set; } = string.Empty;
    public ulong[] RecordIds { get; set; } = Array.Empty<ulong>();
}

public class PlayerMetadataRequest
{
    public required string Key { get; set; }
    public required string Value { get; set; }

    [DefaultValue(null)]
    [SwaggerSchema(Description = "Player index override (admin only). If not provided, uses the player index from JWT. Optional.")]
    public uint? PlayerIndex { get; set; }
}

public class PlayDataIdResponse
{
    public uint PlayDataId { get; set; }
}

public class UpdatePlayerUniqueIdRequest
{
    [SwaggerSchema(Description = "New unique identifier for the player")]
    public string NewPlayerId { get; set; } = string.Empty;

    [DefaultValue(null)]
    [SwaggerSchema(Description = "Player index override (admin only). If not provided, uses the player index from JWT. Optional.")]
    public uint? PlayerIndex { get; set; }
}