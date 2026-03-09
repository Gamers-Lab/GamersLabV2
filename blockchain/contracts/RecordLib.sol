// SPDX-License-Identifier: MIT
pragma solidity ^0.8.28;

import "./DataTypes.sol";
import "./CoreLib.sol";
import "./LibErrors.sol";

/**
 * @title RecordLib
 * @notice Creates and links record objects to PlayData.
 * @dev    All reverts use custom‑error selectors (cheaper than strings).
 *         Player index is now stored directly in record.baseData.playerIndex.
 *         Record uses BaseData for: startTime, endTime, applicationId, playerIndex.
 *         isPlayerAccount flag marks records tied directly to player account (not a match).
 *         Key-value pairs are deduplicated via a separate registry.
 */
library RecordLib {
    /**
     * @notice Creates a new record and links it to PlayData
     * @param _playDataId            Target play‑data index (ignored if isPlayerAccount is true)
     * @param playDatas              Storage array with every PlayData
     * @param records                Storage array with every Record
     * @param recordsByPlayer        playerIndex => all recordIds for that player
     * @param _score                 Numerical score (must fit in int64)
     * @param _keyValueId            ID referencing the KeyValue registry
     * @param _otherPlayers          Auxiliary player indices
     * @param _startTime             Unix timestamp (must fit in uint40)
     * @param _playerIndex           Player index (used for all records now)
     * @param _applicationId         Application ID for this record
     * @param _isPlayerAccount       True if this record is tied to player account (not a match)
     * @return recordId The ID of the newly created record
     * @return playerIndex The index of the player associated with this record
     */
    function setRecord(
        uint256 _playDataId,
        DataTypes.PlayData[] storage playDatas,
        DataTypes.Records[] storage records,
        mapping(uint256 => uint256[]) storage recordsByPlayer,
        int256 _score,
        uint64 _keyValueId,
        uint256[] calldata _otherPlayers,
        uint256 _startTime,
        uint256 _playerIndex,
        uint256 _applicationId,
        bool _isPlayerAccount
    ) internal returns (uint256 recordId, uint256 playerIndex) {
        /* ─── basic validation ─── */
        if (_startTime == 0) revert InvalidTime();

        /* ─── build and store the new record ─── */
        recordId = records.length;
        DataTypes.Records storage rec = records.push();

        // Populate BaseData (Slot 1: startTime, endTime, applicationId, playerIndex)
        rec.baseData.startTime = uint40(_startTime);
        // endTime left as 0 (can be set later if needed)
        rec.baseData.applicationId = uint64(_applicationId);

        // Store isPlayerAccount flag (Slot 1: fits in remaining space after BaseData)
        rec.isPlayerAccount = _isPlayerAccount;

        // Pack score, playDataId, keyValueId into Slot 2
        rec.score = int64(_score);
        rec.keyValueId = _keyValueId;
        if (_otherPlayers.length != 0) rec.otherPlayer = _otherPlayers;

        // Use isPlayerAccount flag to determine if this is a match record or player account record
        if (!_isPlayerAccount) {
            /* ─── match record: link to PlayData ─── */
            if (_playDataId >= playDatas.length) revert IndexOutOfBounds();
            DataTypes.PlayData storage pd = playDatas[_playDataId];
            pd.records.push(recordId);
            // Get playerIndex from PlayData for match records
            playerIndex = pd.baseData.playerIndex;
            rec.playDataId = uint64(_playDataId);
        } else {
            /* ─── player account record: use provided playerIndex directly ─── */
            playerIndex = _playerIndex;
            rec.playDataId = type(uint64).max;
        }

        // Store playerIndex directly in record's baseData
        rec.baseData.playerIndex = uint64(playerIndex);

        /* ─── add to player's record array ─── */
        recordsByPlayer[playerIndex].push(recordId);
    }

    /**
     * @notice Finds the playDataId for a player in a match
     * @param matchSessions Storage array of match sessions
     * @param playDatas Storage array of play data
     * @param mid The match ID
     * @param pIdx The player index
     * @return id The playData ID for this player in the match
     * @dev Reverts if match ID is invalid or player not in match
     */
    function findPlayDataId(
        DataTypes.MatchSession[] storage matchSessions,
        DataTypes.PlayData[] storage playDatas,
        uint256 mid,
        uint256 pIdx
    ) internal view returns (uint256 id) {
        if (mid >= matchSessions.length) revert IndexOutOfBounds();

        DataTypes.MatchSession storage ms = matchSessions[mid];

        for (uint256 i; i < ms.playData.length; ++i) {
            uint256 pdId = ms.playData[i];
            if (playDatas[pdId].baseData.playerIndex == pIdx) return pdId;
        }

        revert NotFound(); // player not in match
    }

    /**
     * @notice Gets or creates a KeyValue entry in the registry
     * @param keyValueRegistry Storage array of key-value pairs
     * @param keyValueHash Mapping from hash to registry index + 1
     * @param _key The record key
     * @param _value The record value
     * @return keyValueId The ID of the (existing or new) KeyValue entry
     */
    function getOrCreateKeyValueId(
        DataTypes.KeyValue[] storage keyValueRegistry,
        mapping(bytes32 => uint64) storage keyValueHash,
        string calldata _key,
        string calldata _value
    ) internal returns (uint64) {
        bytes32 hash = keccak256(abi.encodePacked(_key, _value));
        uint64 existing = keyValueHash[hash];

        if (existing != 0) {
            // Already exists, return index (stored as index + 1)
            return existing - 1;
        }

        // Create new entry
        uint64 newId = uint64(keyValueRegistry.length);
        keyValueRegistry.push(DataTypes.KeyValue(_key, _value));
        keyValueHash[hash] = newId + 1; // +1 because 0 means "not found"

        return newId;
    }

    /**
     * @notice Processes a single record input and creates the record (calldata version)
     * @param input The record input data (calldata)
     * @param matchSessions Storage array of match sessions
     * @param playDatas Storage array of play data
     * @param records Storage array of records
     * @param recordsByPlayer Mapping from player index to record IDs
     * @param keyValueRegistry Storage array of key-value pairs
     * @param keyValueHash Mapping from hash to registry index + 1
     * @return rid The record ID
     * @return playerId The player index
     * @return pdId The playData ID (or max uint64 for player account records)
     */
    function processRecordInput(
        DataTypes.RecordInput calldata input,
        DataTypes.MatchSession[] storage matchSessions,
        DataTypes.PlayData[] storage playDatas,
        DataTypes.Records[] storage records,
        mapping(uint256 => uint256[]) storage recordsByPlayer,
        DataTypes.KeyValue[] storage keyValueRegistry,
        mapping(bytes32 => uint64) storage keyValueHash
    ) internal returns (uint256 rid, uint256 playerId, uint256 pdId) {
        return _processRecordInputInternal(
            input.matchId,
            input.playerIndex,
            input.score,
            input.key,
            input.value,
            input.otherPlayers,
            input.startTime,
            input.isPlayerAccount,
            matchSessions,
            playDatas,
            records,
            recordsByPlayer,
            keyValueRegistry,
            keyValueHash
        );
    }

    /**
     * @notice Processes a single record input and creates the record (memory version)
     * @param input The record input data (memory)
     * @param matchSessions Storage array of match sessions
     * @param playDatas Storage array of play data
     * @param records Storage array of records
     * @param recordsByPlayer Mapping from player index to record IDs
     * @param keyValueRegistry Storage array of key-value pairs
     * @param keyValueHash Mapping from hash to registry index + 1
     * @return rid The record ID
     * @return playerId The player index
     * @return pdId The playData ID (or max uint64 for player account records)
     */
    function processRecordInputMemory(
        DataTypes.RecordInput memory input,
        DataTypes.MatchSession[] storage matchSessions,
        DataTypes.PlayData[] storage playDatas,
        DataTypes.Records[] storage records,
        mapping(uint256 => uint256[]) storage recordsByPlayer,
        DataTypes.KeyValue[] storage keyValueRegistry,
        mapping(bytes32 => uint64) storage keyValueHash
    ) internal returns (uint256 rid, uint256 playerId, uint256 pdId) {
        return _processRecordInputInternal(
            input.matchId,
            input.playerIndex,
            input.score,
            input.key,
            input.value,
            input.otherPlayers,
            input.startTime,
            input.isPlayerAccount,
            matchSessions,
            playDatas,
            records,
            recordsByPlayer,
            keyValueRegistry,
            keyValueHash
        );
    }

    /**
     * @notice Internal implementation for processing record input
     */
    function _processRecordInputInternal(
        uint256 matchId,
        uint256 playerIndex,
        int256 score,
        string memory key,
        string memory value,
        uint256[] memory otherPlayers,
        uint256 startTime,
        bool isPlayerAccount,
        DataTypes.MatchSession[] storage matchSessions,
        DataTypes.PlayData[] storage playDatas,
        DataTypes.Records[] storage records,
        mapping(uint256 => uint256[]) storage recordsByPlayer,
        DataTypes.KeyValue[] storage keyValueRegistry,
        mapping(bytes32 => uint64) storage keyValueHash
    ) private returns (uint256 rid, uint256 playerId, uint256 pdId) {
        // Validate strings before creating KeyValue entry
        CoreLib.validateStringMemory(key);
        CoreLib.validateStringMemory(value);

        if (isPlayerAccount) {
            // Player account records are not linked to a match/playData
            pdId = type(uint64).max;
        } else {
            pdId = findPlayDataId(matchSessions, playDatas, matchId, playerIndex);
        }

        // Get or create deduplicated key-value entry
        uint64 keyValueId = getOrCreateKeyValueIdMemory(keyValueRegistry, keyValueHash, key, value);

        (rid, playerId) = setRecordMemory(
            pdId,
            playDatas,
            records,
            recordsByPlayer,
            score,
            keyValueId,
            otherPlayers,
            startTime,
            playerIndex,
            0, // applicationId - can be enhanced later
            isPlayerAccount
        );
    }

    /**
     * @notice Gets or creates a KeyValue entry (memory version)
     */
    function getOrCreateKeyValueIdMemory(
        DataTypes.KeyValue[] storage keyValueRegistry,
        mapping(bytes32 => uint64) storage keyValueHash,
        string memory _key,
        string memory _value
    ) internal returns (uint64) {
        bytes32 hash = keccak256(abi.encodePacked(_key, _value));
        uint64 existing = keyValueHash[hash];

        if (existing != 0) {
            return existing - 1;
        }

        uint64 newId = uint64(keyValueRegistry.length);
        keyValueRegistry.push(DataTypes.KeyValue(_key, _value));
        keyValueHash[hash] = newId + 1;

        return newId;
    }

    /**
     * @notice Creates a new record (memory version for otherPlayers)
     */
    function setRecordMemory(
        uint256 _playDataId,
        DataTypes.PlayData[] storage playDatas,
        DataTypes.Records[] storage records,
        mapping(uint256 => uint256[]) storage recordsByPlayer,
        int256 _score,
        uint64 _keyValueId,
        uint256[] memory _otherPlayers,
        uint256 _startTime,
        uint256 _playerIndex,
        uint256 _applicationId,
        bool _isPlayerAccount
    ) internal returns (uint256 recordId, uint256 playerIndex) {
        if (_startTime == 0) revert InvalidTime();

        recordId = records.length;
        DataTypes.Records storage rec = records.push();

        rec.baseData.startTime = uint40(_startTime);
        rec.baseData.applicationId = uint64(_applicationId);
        rec.isPlayerAccount = _isPlayerAccount;
        rec.score = int64(_score);
        rec.keyValueId = _keyValueId;
        if (_otherPlayers.length != 0) rec.otherPlayer = _otherPlayers;

        if (!_isPlayerAccount) {
            if (_playDataId >= playDatas.length) revert IndexOutOfBounds();
            DataTypes.PlayData storage pd = playDatas[_playDataId];
            pd.records.push(recordId);
            playerIndex = pd.baseData.playerIndex;
            rec.playDataId = uint64(_playDataId);
        } else {
            playerIndex = _playerIndex;
            rec.playDataId = type(uint64).max;
        }

        rec.baseData.playerIndex = uint64(playerIndex);
        recordsByPlayer[playerIndex].push(recordId);
    }
}
