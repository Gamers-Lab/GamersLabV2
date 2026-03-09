// SPDX-License-Identifier: MIT
pragma solidity ^0.8.28;

import "./DataTypes.sol";

/**
 * @title BatchLib
 * @notice Library for batch getter operations
 * @dev Provides gas-efficient batch read functions for records, players, and key-values
 */
library BatchLib {

    /**
     * @notice Batch get core record data with packed types
     * @param records Storage array of records
     * @param rids Array of record IDs to fetch
     * @return playerIndexes Player index for each record (uint64)
     * @return keyValueIds KeyValue registry ID for each record (uint64)
     * @return scores Score for each record (int64)
     * @return startTimes Start timestamp for each record (uint40)
     * @return endTimes End timestamp for each record (uint40)
     * @return applicationIds Application ID for each record (uint64)
     * @return playDataIds PlayData ID for each record (uint64)
     * @return isPlayerAccounts Whether record is player-account level (uint8: 0=false, 1=true)
     * @return otherPlayerCounts Number of other players in each record (uint16)
     */
    function batchGetRecords(
        DataTypes.Records[] storage records,
        uint256[] calldata rids
    )
        internal
        view
        returns (
            uint64[] memory playerIndexes,
            uint64[] memory keyValueIds,
            int64[] memory scores,
            uint40[] memory startTimes,
            uint40[] memory endTimes,
            uint64[] memory applicationIds,
            uint64[] memory playDataIds,
            uint8[] memory isPlayerAccounts,
            uint16[] memory otherPlayerCounts
        )
    {
        uint256 len = rids.length;
        playerIndexes = new uint64[](len);
        keyValueIds = new uint64[](len);
        scores = new int64[](len);
        startTimes = new uint40[](len);
        endTimes = new uint40[](len);
        applicationIds = new uint64[](len);
        playDataIds = new uint64[](len);
        isPlayerAccounts = new uint8[](len);
        otherPlayerCounts = new uint16[](len);

        for (uint256 i = 0; i < len; ++i) {
            if (rids[i] >= records.length) continue; // Skip invalid, leaves zeros
            DataTypes.Records storage r = records[rids[i]];

            // BaseData fields
            playerIndexes[i] = r.baseData.playerIndex;
            startTimes[i] = r.baseData.startTime;
            endTimes[i] = r.baseData.endTime;
            applicationIds[i] = r.baseData.applicationId;

            // Record-specific fields
            keyValueIds[i] = r.keyValueId;
            scores[i] = r.score;
            playDataIds[i] = r.playDataId;
            isPlayerAccounts[i] = r.isPlayerAccount ? 1 : 0;

            // Other players count (actual data fetched separately if needed)
            otherPlayerCounts[i] = uint16(r.otherPlayer.length);
        }
    }

    /**
     * @notice Batch get otherPlayer arrays for records (flat encoding)
     * @param records Storage array of records
     * @param rids Array of record IDs (same IDs used in batchGetRecords)
     * @return allOtherPlayers Flat array of all other player IDs
     * @return counts Number of other players per record (for reconstruction)
     * @dev Use counts to reconstruct per-record arrays:
     *      Record i's others start at sum(counts[0..i-1]) with length counts[i]
     */
    function batchGetOtherPlayers(
        DataTypes.Records[] storage records,
        uint256[] calldata rids
    )
        internal
        view
        returns (uint256[] memory allOtherPlayers, uint16[] memory counts)
    {
        uint256 len = rids.length;
        counts = new uint16[](len);

        // First pass: count total other players
        uint256 totalOthers = 0;
        for (uint256 i = 0; i < len; ++i) {
            if (rids[i] < records.length) {
                uint256 c = records[rids[i]].otherPlayer.length;
                counts[i] = uint16(c);
                totalOthers += c;
            }
        }

        // Allocate flat array
        allOtherPlayers = new uint256[](totalOthers);

        // Second pass: populate flat array
        uint256 offset = 0;
        for (uint256 i = 0; i < len; ++i) {
            if (rids[i] >= records.length) continue;
            uint256[] storage others = records[rids[i]].otherPlayer;
            uint256 c = others.length;
            for (uint256 j = 0; j < c; ++j) {
                allOtherPlayers[offset++] = others[j];
            }
        }
    }

    /**
     * @notice Batch get key-value pairs by registry IDs
     * @param keyValueRegistry Storage array of key-value pairs
     * @param kvIds Array of key-value registry IDs
     * @return keys Array of key strings
     * @return values Array of value strings
     * @dev Invalid IDs return empty strings
     */
    function batchGetKeyValues(
        DataTypes.KeyValue[] storage keyValueRegistry,
        uint64[] calldata kvIds
    )
        internal
        view
        returns (string[] memory keys, string[] memory values)
    {
        uint256 len = kvIds.length;
        keys = new string[](len);
        values = new string[](len);

        for (uint256 i = 0; i < len; ++i) {
            if (kvIds[i] < keyValueRegistry.length) {
                DataTypes.KeyValue storage kv = keyValueRegistry[kvIds[i]];
                keys[i] = kv.key;
                values[i] = kv.value;
            }
            // Invalid IDs leave empty strings (default)
        }
    }

    /**
     * @notice Batch get player data by indexes
     * @param players Storage array of players
     * @param playerIdxs Array of player indexes
     * @return playerIDs Array of player unique IDs/usernames
     * @return playerAddresses Array of player addresses
     * @return playerTypes Array of player types (0=Human, 1=NPC, 2=Ai)
     * @return totalScores Array of player total scores
     * @dev Invalid indexes return default values (empty string, address(0), 0, 0)
     */
    function batchGetPlayers(
        DataTypes.Player[] storage players,
        uint256[] calldata playerIdxs
    )
        internal
        view
        returns (
            string[] memory playerIDs,
            address[] memory playerAddresses,
            uint8[] memory playerTypes,
            int64[] memory totalScores
        )
    {
        uint256 len = playerIdxs.length;
        playerIDs = new string[](len);
        playerAddresses = new address[](len);
        playerTypes = new uint8[](len);
        totalScores = new int64[](len);

        for (uint256 i = 0; i < len; ++i) {
            if (playerIdxs[i] < players.length) {
                DataTypes.Player storage p = players[playerIdxs[i]];
                playerIDs[i] = p.playerID;
                playerAddresses[i] = p.playerAddress;
                playerTypes[i] = uint8(p.playerType);
                totalScores[i] = int64(p.totalScore);
            }
            // Invalid indexes leave default values
        }
    }
}

