// SPDX-License-Identifier: MIT
pragma solidity ^0.8.28;

import "./DataTypes.sol";
import "./LibErrors.sol";

/**
 * @title CoreLib
 * @notice Core library for metadata and string validation functions
 * @dev Contains constants and utility functions used across the application
 */
library CoreLib {
    uint256 internal constant MAX_METADATA_KEYS = 999;
    uint256 internal constant MAX_METADATA_VALUES = 999;
    uint256 internal constant MAX_PLAYERS_PER_MATCH = 9_999;
    uint256 internal constant MAX_VERIFIED_ADDRESSES = 999;
    uint256 internal constant MAX_VERIFIED_IDS = 999;
    uint256 internal constant MAX_CONTRACTS = 999;
    uint256 private constant MAX_STRING_LENGTH = 512;

    /**
     * @notice Validates a string in calldata
     * @param s The string to validate
     * @dev Reverts if string is empty or exceeds maximum length
     */
    function validateStringCalldata(string calldata s) internal pure {
        uint256 len = bytes(s).length;
        if (len == 0) revert EmptyString();
        if (len > MAX_STRING_LENGTH) revert TooManyItems();
    }

    /**
     * @notice Validates a string in memory
     * @param s The string to validate
     * @dev Reverts if string is empty or exceeds maximum length
     */
    function validateStringMemory(string memory s) internal pure {
        uint256 len = bytes(s).length;
        if (len == 0) revert EmptyString();
        if (len > MAX_STRING_LENGTH) revert TooManyItems();
    }

    function addMetadata(
        DataTypes.MetaData storage metadata,
        string calldata key,
        string calldata value
    ) internal returns (uint256 index) {
        validateStringCalldata(key);
        validateStringCalldata(value);

        bytes32 keyHash = keccak256(bytes(key));

        for (uint256 i = 0; i < metadata.key.length; ++i) {
            if (keccak256(bytes(metadata.key[i])) == keyHash) {
                // Overwrite value
                metadata.values[i] = value;
                return i;
            }
        }

        // New entry
        if (metadata.key.length >= MAX_METADATA_KEYS) revert TooManyItems();
        metadata.key.push(key);
        metadata.values.push(value);
        return metadata.key.length - 1;
    }
}
