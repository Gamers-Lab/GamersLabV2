using BattleRecordsRouter.Models;
using Microsoft.AspNetCore.Http;

namespace BattleRecordsRouter.Services;

public interface IGamersLabStorageService
{
    // Application operations
    Task<string> SetApplication(HttpContext httpContext, string name);

    Task<(string transactionHash, ulong recordId)> CreateApplicationRecord(
        HttpContext httpContext,
        string applicationVersion,
        string companyName,
        string[] contractAddresses);

    Task<string> SetApplicationRecordMetadata(
        HttpContext httpContext,
        ulong recordId,
        string key,
        string value);

    Task<string> UpdateApplicationRecordAddresses(
        HttpContext httpContext,
        ulong recordId,
        string[] contractAddresses);

    Task<ulong?> GetLatestApplicationRecord();
    Task<(string transactionHash, ApplicationRecordResponse record)> GetApplicationRecord(ulong recordId);
    Task<(string transactionHash, ApplicationResponse application)> GetApplication();

    // Match operations
    Task<string> SetRecord(
        HttpContext httpContext,
        uint matchSessionId,
        uint playerIndex,
        int score,
        string key,
        string value,
        uint[] otherPlayers,
        uint startTime);

    Task<RecordInfo> GetRecordsByIndex(uint index);
    Task<uint[]> GetRecordIdsByPlayer(uint playerIndex);


    // Player operations
    Task<(string transactionHash, uint playerIndex)> CreatePlayer(
        HttpContext httpContext,
        string playerId, 
        PlayerType playerType,
        string playerAddress);

    Task<string> UpdatePlayerScore(HttpContext httpContext, uint playerIndex, int newScore);

    Task<uint> GetPlayerByVerifiedId(string identifierType, string identifierValue);

    Task<string> AddVerifiedAddress(HttpContext httpContext, uint playerIndex, string verifiedAddress);

    Task<string> AddPlayerIdentifier(HttpContext httpContext, uint playerIndex, string identifierType, string identifierValue);
    Task<uint> GetPlayerIndex(string playerId);
    Task<string> GetPlayerUniqueId(uint playerIndex);


    // Session operations
    Task<(string transactionHash, ulong sessionId)> LoginSessionCreate(
        HttpContext httpContext,
        uint playerIndex,
        ulong applicationId,
        uint startTime,
        Device device);

    Task<string> LoginSessionEnd(
        HttpContext httpContext,
        ulong sessionId,
        uint endTime);

    Task<string> SetLoginSessionMetadata(
        HttpContext httpContext,
        ulong sessionId,
        string key,
        string value);

    Task<ulong[]> GetAllActiveLoginPlayers();
    Task<ulong[]> GetAllActiveLoginSessionIds();
    Task<ulong> GetLoginSessionCount();
    Task<LoginSessionInfo> GetLoginSessionById(ulong sessionId);

    // Match Session operations
    Task<(string transactionHash, uint matchSessionId, uint[] playDataIds)> StartMatchSession(
        HttpContext httpContext,
        ulong applicationId,
        uint startTime,
        bool multiplayer,
        uint[] playerIds,
        string level);

    Task<MatchSessionInfo> GetMatchSessionByIndex(uint index);

    Task<string> EndMatchSession(HttpContext httpContext, uint matchSessionId, uint endTime);
    Task<string> SetMatchSessionMetadata(HttpContext httpContext, uint matchSessionId, string key, string value);

    Task<uint[]> GetActiveMatchSessions(uint playerIndex);
    Task<uint> GetActiveMatchSessionsLength();
    Task<uint[]> GetAllActiveMatchSessions();

// Play Data operations
    Task<string> AddPlayerToMatchSession(
        HttpContext httpContext,
        uint matchSessionId,
        uint playerIndex,
        uint startTime);

    Task<string> UpdatePlayDataForMatchSession(
        HttpContext httpContext,
        uint matchSessionId,
        uint playerIndex,
        uint endTime,
        int score,
        byte winLoss,
        int matchPosition);

    Task<PlayDataInfo> GetPlayDataByIndex(uint index);
    Task<uint> GetPlayDataCount();


    // admin operations
    Task<string> Pause(HttpContext httpContext);
    Task<string> Unpause(HttpContext httpContext);
    Task<bool> Paused();
    Task<string> BanPlayer(HttpContext httpContext, uint playerIndex);
    Task<string> UnbanPlayer(HttpContext httpContext, uint playerIndex);
    Task<string> RevokeModerator(HttpContext httpContext, string account);
    Task<string> GrantModerator(HttpContext httpContext, string account);
    Task<bool> HasRole(string role, string account);
    Task<string> RevokeRole(HttpContext httpContext, string role, string account);
    Task<string> GrantRole(HttpContext httpContext, string role, string account);

    Task<uint> GetPlayerBannedByIndex(uint index);
    Task<List<uint>> GetAllBannedPlayers();
    Task<bool> IsPlayerBanned(uint playerIndex);

    Task<uint> GetPlayerIndexByAddress(string address);
    Task<uint> GetPlayerCount();
    Task<(string transactionHash, PlayerResponse player)> GetPlayerByIndex(uint index);

    Task<(bool ok, uint playerIndex, string message)> TryGetPlayerIndexByAddressAsync(string address);

    Task<Dictionary<string, string>> GetDiagnosticInfo();

    Task<string> BatchSetRecords(HttpContext httpContext, RecordInput[] inputs);

    // Player metadata
    Task<string> SetPlayerMetadata(HttpContext httpContext, uint playerIndex, string key, string value);

    Task<uint> FindPlayDataId(uint matchSessionId, uint playerIndex);

    Task<string> UpdatePlayerUniqueId(HttpContext httpContext, uint playerIndex, string newPlayerId);

    Task<Dictionary<string, object>> GetSystemDiagnostics();
}