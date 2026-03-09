// SPDX-License-Identifier: MIT
pragma solidity ^0.8.28;

import "./DataTypes.sol";
import "./CoreLib.sol";
import "./LibErrors.sol";

/**
 * @title MatchLib
 * @notice Library for managing match sessions and player match data
 * @dev Provides functions for creating, ending, and modifying match sessions
 *      as well as managing player data within matches.
 *      Combines previous MatchLibSession and MatchLibPlayer functionality.
 */
library MatchLib {

    /* ====================== PLAYER DATA FUNCTIONS ====================== */

    /**
     * @notice Creates player data for a match
     * @param playerDatas Storage array of player data
     * @param _playerIndex Index of the player
     * @param _applicationId ID of the application
     * @param _startTime Start time of the player's participation
     * @return playerDataId ID of the created player data
     * @dev Reverts if start time is 0
     */
    function createPlayerData(
        DataTypes.PlayData[] storage playerDatas,
        uint256 _playerIndex,
        uint256 _applicationId,
        uint256 _startTime
    ) internal returns (uint256 playerDataId) {
        if (_startTime == 0) revert Invalid();

        playerDataId = playerDatas.length;
        DataTypes.PlayData storage pd = playerDatas.push();

        // Cast to packed types (Solidity 0.8 reverts on overflow)
        pd.baseData.startTime = uint40(_startTime);
        pd.baseData.applicationId = uint64(_applicationId);
        pd.baseData.playerIndex = uint64(_playerIndex);

        pd.winLoss = DataTypes.WinLoss.NotSet;
    }


    /**
     * @notice Sets player data for a match (can only be set once)
     * @param _playDataId ID of the player data to set
     * @param playerDatas Storage array of player data
     * @param _endTime End time of the player's participation
     * @param _score Player's score (must fit in int64: -9223372036854775808 to 9223372036854775807)
     * @param _winLoss Win/loss status
     * @param _matchPosition Player's position in the match (must fit in int16: -32768 to 32767)
     * @return playDataId ID of the set player data
     * @return playerIndex Index of the player
     * @dev Reverts if:
     *      - Play data ID is out of bounds (IndexOutOfBounds)
     *      - Win/loss value is invalid (IndexOutOfBounds)
     *      - End time is 0 (InvalidTime)
     *      - End time is before or equal to start time (InvalidTime)
     *      - Play data already has an end time set (PlayDataAlreadySet)
     *      - Score or match position values are out of range (ValueOutOfRange)
     */
    function setPlayData(
        uint256 _playDataId,
        DataTypes.PlayData[] storage playerDatas,
        uint256 _endTime,
        int256 _score,
        DataTypes.WinLoss _winLoss,
        int256 _matchPosition
    ) internal returns (uint256 playDataId, uint256 playerIndex) {
        if (_playDataId >= playerDatas.length) revert IndexOutOfBounds();
        if (_winLoss >= DataTypes.WinLoss.Last) revert IndexOutOfBounds();
        if (_endTime == 0) revert InvalidTime();

        // Range validation for packed types
        if (_score < type(int64).min || _score > type(int64).max)
            revert ValueOutOfRange();
        if (_matchPosition < type(int16).min || _matchPosition > type(int16).max)
            revert ValueOutOfRange();

        DataTypes.PlayData storage pd = playerDatas[_playDataId];

        if (pd.baseData.endTime != 0) revert PlayDataAlreadySet();
        if (_endTime <= pd.baseData.startTime) revert InvalidTime();

        pd.baseData.endTime = uint40(_endTime);
        pd.score = int64(_score);
        pd.winLoss = _winLoss;
        pd.matchPosition = int16(_matchPosition);

        return (_playDataId, uint256(pd.baseData.playerIndex));
    }

    /* ====================== MATCH SESSION FUNCTIONS ====================== */

    /**
     * @notice Starts a new match session
     * @param activeMatchSessions Storage array of active match session IDs
     * @param loginSessions Storage array of login sessions
     * @param matchSessions Storage array of match sessions
     * @param playData Storage array of player data
     * @param applicationId ID of the application
     * @param startTime Start time of the match
     * @param playerIndices Array of player indices participating in the match
     * @param level The level/map identifier for the match
     * @return matchSessionId ID of the created match session
     * @dev Reverts if:
     *      - Player array is empty
     *      - Too many players for a single match
     *      - Start time is 0
     *      - Level string is empty or invalid
     *      - Duplicate players are detected
     */
    function startMatchSession(
        uint256[] storage activeMatchSessions,
        DataTypes.LoginSession[] storage loginSessions,
        DataTypes.MatchSession[] storage matchSessions,
        DataTypes.PlayData[] storage playData,
        uint256 applicationId,
        uint256 startTime,
        uint256[] memory playerIndices,
        string memory level
    ) internal returns (uint256 matchSessionId) {
        uint256 playerCount = playerIndices.length;
        if (playerCount == 0) revert EmptyArray();
        if (playerCount > CoreLib.MAX_PLAYERS_PER_MATCH)
            revert TooManyPlayers();
        if (startTime == 0) revert InvalidTime();

        CoreLib.validateStringMemory(level);

        /* create session --------------------------------------------------------- */
        matchSessionId = matchSessions.length;
        DataTypes.MatchSession storage ms = matchSessions.push();

        // Cast to packed types (Solidity 0.8 reverts on overflow)
        ms.baseData.startTime = uint40(startTime);
        ms.baseData.applicationId = uint64(applicationId);
        ms.level = level;

        ms.playData = new uint256[](playerCount);

        /* uniqueness check ------------------------------------------------------- */
        unchecked {
            for (uint256 i; i < playerCount - 1; ++i) {
                for (uint256 j = i + 1; j < playerCount; ++j)
                    if (playerIndices[i] == playerIndices[j])
                        revert DuplicatePlayer();
            }
        }

        /* create play‑data ------------------------------------------------------- */
        unchecked {
            for (uint256 i; i < playerCount; ++i) {
                uint256 pIdx = playerIndices[i];

                ms.playData[i] = createPlayerData(
                    playData,
                    pIdx,
                    applicationId,
                    startTime
                );

                // attach matchId to any active login‑session of this player
                for (uint256 j; j < loginSessions.length; ++j) {
                    DataTypes.LoginSession storage ls = loginSessions[j];
                    if (ls.baseData.endTime == 0 && ls.baseData.playerIndex == pIdx) {
                        ls.matchIds.push(matchSessionId);
                        break;
                    }
                }
            }
        }

        activeMatchSessions.push(matchSessionId);
    }


    /**
     * @notice Ends a match session
     * @param matchSessions Storage array of match sessions
     * @param activeMatchSessions Storage array of active match session IDs
     * @param playerDatas Storage array of player data
     * @param _matchSessionId ID of the match session to end
     * @param _endTime End time of the match
     * @dev Reverts if:
     *      - End time is 0
     *      - Match session ID is out of bounds
     *      - Match is already ended
     *      - End time is before or equal to start time
     * @dev Sets unfinished players to Forfeit status
     */
    function endMatchSession(
        DataTypes.MatchSession[] storage matchSessions,
        uint256[] storage activeMatchSessions,
        DataTypes.PlayData[] storage playerDatas,
        uint256 _matchSessionId,
        uint256 _endTime
    ) internal {
        if (_endTime == 0) revert InvalidTime();
        if (_matchSessionId >= matchSessions.length) revert IndexOutOfBounds();

        DataTypes.MatchSession storage ms = matchSessions[_matchSessionId];
        if (ms.baseData.endTime != 0) revert MatchAlreadyEnded();
        if (_endTime <= ms.baseData.startTime) revert InvalidTime();

        // Cast to packed type
        uint40 endTime40 = uint40(_endTime);
        ms.baseData.endTime = endTime40;

        /* remove from active list (swap‑pop) */
        for (uint256 i; i < activeMatchSessions.length; ++i)
            if (activeMatchSessions[i] == _matchSessionId) {
                activeMatchSessions[i] = activeMatchSessions[
                    activeMatchSessions.length - 1
                ];
                activeMatchSessions.pop();
                break;
            }

        /* set unfinished players to Forfeit */
        uint256 n = ms.playData.length;
        for (uint256 i; i < n; ++i) {
            DataTypes.PlayData storage pd = playerDatas[ms.playData[i]];
            if (pd.baseData.endTime == 0) {
                pd.baseData.endTime = endTime40;
                pd.winLoss = DataTypes.WinLoss.Forfeit;
            }
        }
    }


    /**
     * @notice Adds a player to an existing match
     * @param matchSessions Storage array of match sessions
     * @param playDatas Storage array of player data
     * @param _matchSessionId ID of the match session
     * @param _playerIndex Index of the player to add
     * @param _startTime Start time for the player in the match
     * @return playDataId ID of the created player data
     * @dev Reverts if:
     *      - Match session ID is out of bounds
     *      - Match has already ended
     *      - Match is full
     *      - Player is already in the match
     */
    function addPlayerToMatch(
        DataTypes.MatchSession[] storage matchSessions,
        DataTypes.PlayData[] storage playDatas,
        uint256 _matchSessionId,
        uint256 _playerIndex,
        uint256 _startTime
    ) internal returns (uint256 playDataId) {
        if (_matchSessionId >= matchSessions.length) revert IndexOutOfBounds();

        DataTypes.MatchSession storage ms = matchSessions[_matchSessionId];
        if (ms.baseData.endTime != 0) revert MatchFinished();
        if (ms.playData.length >= CoreLib.MAX_PLAYERS_PER_MATCH)
            revert MatchFull();

        for (uint256 i; i < ms.playData.length; ++i)
            if (playDatas[ms.playData[i]].baseData.playerIndex == _playerIndex)
                revert PlayerAlreadyInMatch();

        playDataId = createPlayerData(
            playDatas,
            _playerIndex,
            ms.baseData.applicationId,
            _startTime
        );

        ms.playData.push(playDataId);
    }


    /**
     * @notice Cleans up stale match sessions that have been active too long
     * @param activeMatchSessions Storage array of active match session IDs
     * @param matchSessions Storage array of match sessions
     * @param playDatas Storage array of player data
     * @param cutoff Unix timestamp cutoff - matches started before this are stale
     * @param maxCleanup Maximum number of matches to clean in this call
     * @return cleanedIds Array of match IDs that were cleaned up
     */
    function cleanupStaleMatches(
        uint256[] storage activeMatchSessions,
        DataTypes.MatchSession[] storage matchSessions,
        DataTypes.PlayData[] storage playDatas,
        uint256 cutoff,
        uint256 maxCleanup
    ) internal returns (uint256[] memory cleanedIds) {
        // First pass: count matches to clean (up to max)
        uint256 cleanCount;
        for (uint256 i; i < activeMatchSessions.length && cleanCount < maxCleanup; ++i) {
            uint256 mid = activeMatchSessions[i];
            DataTypes.MatchSession storage ms = matchSessions[mid];

            if (ms.baseData.startTime < cutoff && ms.baseData.endTime == 0) {
                cleanCount++;
            }
        }

        // Allocate return array
        cleanedIds = new uint256[](cleanCount);
        uint256 idx;

        // Second pass: clean (iterate backwards for safe removal)
        uint40 endTime = uint40(block.timestamp);
        for (uint256 i = activeMatchSessions.length; i > 0 && idx < maxCleanup; ) {
            unchecked { --i; }
            uint256 mid = activeMatchSessions[i];
            DataTypes.MatchSession storage ms = matchSessions[mid];

            if (ms.baseData.startTime < cutoff && ms.baseData.endTime == 0) {
                // Force end the match
                ms.baseData.endTime = endTime;

                // Mark all players as forfeit
                for (uint256 j; j < ms.playData.length; ++j) {
                    DataTypes.PlayData storage pd = playDatas[ms.playData[j]];
                    if (pd.baseData.endTime == 0) {
                        pd.baseData.endTime = endTime;
                        pd.winLoss = DataTypes.WinLoss.Forfeit;
                    }
                }

                // Swap-and-pop from active list
                activeMatchSessions[i] = activeMatchSessions[activeMatchSessions.length - 1];
                activeMatchSessions.pop();

                cleanedIds[idx++] = mid;
            }
        }
    }

    // /**
    //  * @notice Adds metadata to a match session
    //  * @param matchSessions Storage array of match sessions
    //  * @param _matchSessionId ID of the match session
    //  * @param _key Metadata key
    //  * @param _value Metadata value
    //  * @dev Reverts if:
    //  *      - Match session ID is out of bounds
    //  *      - Match has already ended
    //  *      - Key or value is empty or invalid
    //  *      - Maximum metadata keys or values exceeded
    //  *      - Value already exists for the key
    //  */
    // function setMatchSessionMetadata(
    //     DataTypes.MatchSession[] storage matchSessions,
    //     uint256 _matchSessionId,
    //     string calldata _key,
    //     string calldata _value
    // ) internal {
    //     if (_matchSessionId >= matchSessions.length) revert IndexOutOfBounds();

    //     DataTypes.MatchSession storage ms = matchSessions[_matchSessionId];
    //     if (ms.baseData.endTime != 0) revert MatchFinished();

    //     CoreLib.addMetadata(ms.metadata, _key, _value);
    // }
}
