// SPDX-License-Identifier: MIT
pragma solidity ^0.8.28;

import "./LibErrors.sol";

/**
 * @title AdminLib
 * @notice Library for administrative functions like banning/unbanning players
 * @dev All reverts use custom-error selectors (cheaper than strings).
 *      Designed for future expansion with additional admin functions.
 */
library AdminLib {

    /* ========================= BAN / UNBAN ========================= */

    /**
     * @notice Bans a player by adding them to the banned list
     * @param banned Storage array of banned player indices
     * @param bannedByPlayer Mapping from player index to position in banned array (1-based)
     * @param playerCount Total number of players (for bounds check)
     * @param idx The index of the player to ban
     * @dev Reverts if:
     *      - Player index is out of bounds (IndexOutOfBounds)
     *      - Player is already banned (AlreadyExists)
     */
    function banPlayer(
        uint256[] storage banned,
        mapping(uint256 => uint256) storage bannedByPlayer,
        uint256 playerCount,
        uint256 idx
    ) internal {
        if (idx >= playerCount) revert IndexOutOfBounds();
        if (bannedByPlayer[idx] != 0) revert AlreadyExists();

        banned.push(idx);
        bannedByPlayer[idx] = banned.length; // 1-based position
    }

    /**
     * @notice Unbans a player by removing them from the banned list
     * @param banned Storage array of banned player indices
     * @param bannedByPlayer Mapping from player index to position in banned array (1-based)
     * @param idx The index of the player to unban
     * @dev Uses swap-and-pop for O(1) removal
     * @dev Reverts if player is not banned (NotFound)
     */
    function unbanPlayer(
        uint256[] storage banned,
        mapping(uint256 => uint256) storage bannedByPlayer,
        uint256 idx
    ) internal {
        uint256 position = bannedByPlayer[idx];
        if (position == 0) revert NotFound();

        position--; // Convert from 1-based to 0-based index
        uint256 lastIdx = banned.length - 1;

        // If not the last element, swap with last
        if (position != lastIdx) {
            uint256 lastPlayer = banned[lastIdx];
            banned[position] = lastPlayer;
            bannedByPlayer[lastPlayer] = position + 1;
        }

        banned.pop();
        delete bannedByPlayer[idx];
    }

    /**
     * @notice Gets banned player indices with pagination
     * @param banned Storage array of banned player indices
     * @param offset Starting index in the array
     * @param limit Maximum number of IDs to return
     * @return ids Array of banned player indices (up to `limit` items)
     * @return total Total number of banned players
     */
    function getBannedPlayers(
        uint256[] storage banned,
        uint256 offset,
        uint256 limit
    ) internal view returns (uint256[] memory ids, uint256 total) {
        total = banned.length;

        // If offset is beyond array length, return empty
        if (offset >= total) {
            return (new uint256[](0), total);
        }

        // Calculate actual count to return
        uint256 remaining = total - offset;
        uint256 count = remaining < limit ? remaining : limit;

        // Build result array
        ids = new uint256[](count);
        for (uint256 i = 0; i < count; ++i) {
            ids[i] = banned[offset + i];
        }
    }

    /**
     * @notice Checks if a player is banned
     * @param bannedByPlayer Mapping from player index to position in banned array (1-based)
     * @param playerCount Total number of players (for bounds check)
     * @param idx The index of the player to check
     * @return True if the player is banned, false otherwise
     * @dev Reverts if player index is out of bounds
     */
    function isPlayerBanned(
        mapping(uint256 => uint256) storage bannedByPlayer,
        uint256 playerCount,
        uint256 idx
    ) internal view returns (bool) {
        if (idx >= playerCount) revert IndexOutOfBounds();
        return bannedByPlayer[idx] != 0;
    }
}

