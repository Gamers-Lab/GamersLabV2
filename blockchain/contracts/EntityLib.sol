// SPDX-License-Identifier: MIT
pragma solidity ^0.8.28;

import "./DataTypes.sol";
import "./CoreLib.sol";
import "./LibErrors.sol";

/**
 * @title EntityLib
 * @notice Library for managing player entities and application records
 * @dev Provides functions for player creation, identifier management, and application data
 */
library EntityLib {

    /*-------------  helpers -------------*/

    /**
     * @notice Gets a player's index from their unique ID
     * @param playerByUniqueId Mapping from player ID to index
     * @param playerID The unique ID of the player
     * @return idx The index of the player (0-based)
     * @dev Reverts if player ID is empty or not found
     */
    function _getPlayerIndex(
        mapping(string => uint256) storage playerByUniqueId,
        string memory playerID
    ) internal view returns (uint256 idx) {
        if (bytes(playerID).length == 0) revert EmptyString();
        uint256 stored = playerByUniqueId[playerID];
        if (stored == 0) revert NotFound();          // not registered
        return stored - 1;                           // convert 1‑based → 0‑based
    }

    /**
     * @notice Gets a player's unique ID from their index
     * @param _players Array of player structs
     * @param _playerIndex The index of the player
     * @return The unique ID of the player
     * @dev Reverts if player index is out of bounds
     */
    function _getPlayerUniqueId(
        DataTypes.Player[] storage _players,
        uint256 _playerIndex
    ) internal view returns (string memory) {
        if (_playerIndex >= _players.length) revert IndexOutOfBounds();
        return _players[_playerIndex].playerID;
    }

    /*-------------  player creation -------------*/

    /**
     * @notice Creates a new player
     * @param _players Array of player structs
     * @param _playerByUniqueId Mapping from player ID to index
     * @param _playersByAddress Mapping from player address to index
     * @param _playerID The unique ID for the new player
     * @param _playerType The type of player (Human, NPC)
     * @param _playerAddress The blockchain address of the player
     * @return playerIndex The index of the created player
     * @dev Reverts if:
     *      - Player ID is empty or invalid
     *      - Player type is invalid
     *      - Player address is zero
     *      - Player ID already exists
     */
    function createPlayer(
        DataTypes.Player[] storage _players,
        mapping(string => uint256) storage _playerByUniqueId,
        mapping(address => uint256) storage _playersByAddress,
        string calldata _playerID,
        DataTypes.PlayerType _playerType,
        address _playerAddress
    ) internal returns (uint256 playerIndex) {
        // Delegate to memory version - calldata auto-converts to memory
        return createPlayerMemory(_players, _playerByUniqueId, _playersByAddress, _playerID, _playerType, _playerAddress);
    }

    /**
     * @notice Creates a new player (memory version)
     * @dev THIN LAYER: This memory version exists because constructors cannot use calldata.
     *      If Solidity adds calldata support in constructors, this can be removed and
     *      createPlayer can contain the logic directly.
     */
    function createPlayerMemory(
        DataTypes.Player[] storage _players,
        mapping(string => uint256) storage _playerByUniqueId,
        mapping(address => uint256) storage _playersByAddress,
        string memory _playerID,
        DataTypes.PlayerType _playerType,
        address _playerAddress
    ) internal returns (uint256 playerIndex) {

        CoreLib.validateStringMemory(_playerID);
        if (_playerType >= DataTypes.PlayerType.Last) revert IndexOutOfBounds();
        if (_playerAddress == address(0)) revert ZeroAddress();
        if (_playerByUniqueId[_playerID] != 0) revert AlreadyExists();

        playerIndex = _players.length;
        _players.push();

        DataTypes.Player storage p = _players[playerIndex];
        p.playerID     = _playerID;
        p.playerType   = _playerType;
        p.playerAddress= _playerAddress;

        _playerByUniqueId[_playerID]   = playerIndex + 1;
        _playersByAddress[_playerAddress] = playerIndex + 1;
    }

    /*-------------  identifiers -------------*/

    /**
     * @notice Adds or updates an identifier for a player
     * @param _players Array of player structs
     * @param _playerIndex The index of the player
     * @param _identifierType The type of identifier
     * @param _identifierValue The value of the identifier
     * @dev Reverts if:
     *      - Identifier type or value is empty or invalid
     *      - Player index is out of bounds
     *      - Maximum number of identifiers exceeded
     */
    function addPlayerIdentifier(
        DataTypes.Player[] storage _players,
        uint256 _playerIndex,
        string calldata _identifierType,
        string calldata _identifierValue
    ) internal {
        CoreLib.validateStringCalldata(_identifierType);
        CoreLib.validateStringCalldata(_identifierValue);

        if (_playerIndex >= _players.length) revert IndexOutOfBounds();
        DataTypes.Player storage p = _players[_playerIndex];

        if (p.verifiedIdKey.length >= CoreLib.MAX_VERIFIED_IDS)
            revert TooManyItems();

        bytes32 idHash = keccak256(bytes(_identifierType));
        for (uint256 i; i < p.verifiedIdKey.length; ++i) {
            if (keccak256(bytes(p.verifiedIdKey[i])) == idHash) {
                // update existing value
                p.verifiedIdValue[i] = _identifierValue;
                return;
            }
        }

        // add new
        p.verifiedIdKey.push(_identifierType);
        p.verifiedIdValue.push(_identifierValue);
    }

    /**
     * @notice Updates a player's unique username/ID
     * @param _players Array of player structs
     * @param _playerByUniqueId Mapping from player ID to index (1-based)
     * @param _playerIndex The index of the player
     * @param _newPlayerID The new unique ID for the player
     * @dev Reverts if:
     *      - Player index is out of bounds
     *      - New player ID already exists
     */
    function updateUniqueId(
        DataTypes.Player[] storage _players,
        mapping(string => uint256) storage _playerByUniqueId,
        uint256 _playerIndex,
        string calldata _newPlayerID
    ) internal {
        if (_playerIndex >= _players.length) revert IndexOutOfBounds();
        if (_playerByUniqueId[_newPlayerID] != 0) revert AlreadyExists();

        // Delete old mapping
        string memory oldPid = _players[_playerIndex].playerID;
        delete _playerByUniqueId[oldPid];

        // Update player ID
        _players[_playerIndex].playerID = _newPlayerID;

        // Store new mapping (+1 for 1-based indexing)
        _playerByUniqueId[_newPlayerID] = _playerIndex + 1;
    }

    /*-------------  verified addresses -------------*/

    /**
     * @notice Adds a verified address to a player
     * @param _players Array of player structs
     * @param _playerByVerifiedAddress Mapping from verified address to player index (1-based)
     * @param _playerIndex The index of the player
     * @param _verifiedAddress The address to verify for the player
     * @dev Reverts if:
     *      - Address is zero
     *      - Player index is out of bounds
     *      - Maximum number of verified addresses exceeded
     *      - Address already verified for any player (global uniqueness)
     */
    function addVerifiedAddress(
        DataTypes.Player[] storage _players,
        mapping(address => uint256) storage _playerByVerifiedAddress,
        uint256 _playerIndex,
        address _verifiedAddress
    ) internal {
        if (_verifiedAddress == address(0)) revert ZeroAddress();
        if (_playerIndex >= _players.length) revert IndexOutOfBounds();

        // Check global uniqueness - address can only be verified for one player
        if (_playerByVerifiedAddress[_verifiedAddress] != 0) revert AlreadyExists();

        DataTypes.Player storage p = _players[_playerIndex];

        if (p.verifiedAddresses.length >= CoreLib.MAX_VERIFIED_ADDRESSES)
            revert TooManyItems();

        p.verifiedAddresses.push(_verifiedAddress);
        _playerByVerifiedAddress[_verifiedAddress] = _playerIndex + 1; // 1-based
    }

    /**
     * @notice Removes a verified address from a player
     * @param _players Array of player structs
     * @param _playerByVerifiedAddress Mapping from verified address to player index (1-based)
     * @param _playerIndex The index of the player
     * @param _verifiedAddress The address to remove
     * @dev Reverts if:
     *      - Player index is out of bounds
     *      - Address not found in player's verified addresses
     */
    function removeVerifiedAddress(
        DataTypes.Player[] storage _players,
        mapping(address => uint256) storage _playerByVerifiedAddress,
        uint256 _playerIndex,
        address _verifiedAddress
    ) internal {
        if (_playerIndex >= _players.length) revert IndexOutOfBounds();

        DataTypes.Player storage p = _players[_playerIndex];
        uint256 len = p.verifiedAddresses.length;

        // Find the address in the array
        for (uint256 i; i < len; ++i) {
            if (p.verifiedAddresses[i] == _verifiedAddress) {
                // Swap with last element and pop (O(1) removal, changes order)
                p.verifiedAddresses[i] = p.verifiedAddresses[len - 1];
                p.verifiedAddresses.pop();

                // Clear the mapping
                delete _playerByVerifiedAddress[_verifiedAddress];
                return;
            }
        }

        // Address not found
        revert NotFound();
    }

    /*-------------  application -------------*/

    /**
     * @notice Sets application details
     * @param _app Application storage reference
     * @param _name The name of the application
     * @param _owner The owner address of the application
     * @dev Reverts if name is empty or invalid
     */
    function setApplication(
        DataTypes.Application storage _app,
        string calldata _name,
        address _owner
    ) internal {
        // Delegate to memory version
        setApplicationMemory(_app, _name, _owner);
    }

    /**
     * @notice Sets application info (memory version)
     * @dev THIN LAYER: This memory version exists because constructors cannot use calldata.
     *      If Solidity adds calldata support in constructors, this can be removed.
     */
    function setApplicationMemory(
        DataTypes.Application storage _app,
        string memory _name,
        address _owner
    ) internal {
        CoreLib.validateStringMemory(_name);
        _app.name  = _name;
        _app.owner = _owner;
    }

    /**
     * @notice Creates a new application record
     * @param records Array of application records
     * @param _version The version of the application
     * @param _company The company name
     * @param _contracts Array of contract addresses
     * @return id The ID of the created application record
     * @dev Reverts if:
     *      - Company name is empty or invalid
     *      - Too many contracts provided
     *      - Any contract address is zero
     */
    function createApplicationRecord(
        DataTypes.ApplicationRecord[] storage records,
        uint256 _version,
        string calldata _company,
        address[] calldata _contracts,
        uint256 _startDate
    ) internal returns (uint256 id) {
        CoreLib.validateStringCalldata(_company);

        if (_contracts.length > CoreLib.MAX_CONTRACTS) revert TooManyItems();

        id = records.length;
        records.push();

        DataTypes.ApplicationRecord storage r = records[id];
        r.applicationVersion = uint64(_version);
        r.startDate          = uint40(_startDate);
        r.companyName        = _company;
        r.contractAddresses  = _validateAddresses(_contracts);
    }

    /**
     * @notice Creates a new application record (memory version, no contracts)
     * @dev THIN LAYER: This memory version exists because constructors cannot use calldata.
     *      If Solidity adds calldata support in constructors, this can be removed.
     *      Note: This version doesn't accept contracts array - use for constructor init only.
     */
    function createApplicationRecordMemory(
        DataTypes.ApplicationRecord[] storage records,
        uint256 _version,
        string memory _company,
        uint256 _startDate
    ) internal returns (uint256 id) {
        CoreLib.validateStringMemory(_company);

        id = records.length;
        records.push();

        DataTypes.ApplicationRecord storage r = records[id];
        r.applicationVersion = uint64(_version);
        r.startDate          = uint40(_startDate);
        r.companyName        = _company;
        // contractAddresses left empty (default)
    }

    /**
     * @notice Updates contract addresses for an application record
     * @param records Array of application records
     * @param id The ID of the application record
     * @param _contracts New array of contract addresses
     * @dev Reverts if:
     *      - ID is out of bounds
     *      - Contracts array is empty
     *      - Too many contracts provided
     *      - Any contract address is zero
     */
    function updateApplicationRecordAddresses(
        DataTypes.ApplicationRecord[] storage records,
        uint256 id,
        address[] calldata _contracts
    ) internal {
        if (id >= records.length) revert IndexOutOfBounds();
        if (_contracts.length == 0)  revert ZeroAddress();      // empty list
        if (_contracts.length > CoreLib.MAX_CONTRACTS) revert TooManyItems();

        records[id].contractAddresses = _validateAddresses(_contracts);
    }

    /*-------------  helpers -------------*/

    /**
     * @notice Validates an array of addresses
     * @param arr Array of addresses to validate
     * @return out The validated array of addresses
     * @dev Reverts if any address is zero
     */
    function _validateAddresses(
        address[] calldata arr
    ) private pure returns (address[] memory out) {
        for (uint256 i; i < arr.length; ++i)
            if (arr[i] == address(0)) revert ZeroAddress();

        return arr; // dynamic‑to‑memory copy happens automatically
    }

    /*-------------  metadata -------------*/

    // /**
    //  * @notice Sets metadata for an application record
    //  * @param records Array of application records
    //  * @param id The ID of the application record
    //  * @param key The metadata key
    //  * @param value The metadata value
    //  * @dev Reverts if:
    //  *      - ID is out of bounds
    //  *      - Key or value is empty or invalid
    //  *      - Maximum metadata keys or values exceeded
    //  *      - Value already exists for the key
    //  */
    // function setApplicationRecordMetadata(
    //     DataTypes.ApplicationRecord[] storage records,
    //     uint256 id,
    //     string calldata key,
    //     string calldata value
    // ) internal {
    //     if (id >= records.length) revert IndexOutOfBounds();
    //     CoreLib.addMetadata(records[id].metadata, key, value);
    // }

    /**
     * @notice Gets a player's index by verified identifier (key-value pair)
     * @param _players Storage array of players
     * @param k The identifier key to search for
     * @param v The identifier value to search for
     * @return The player's index (0-based)
     * @dev Iterates through all players and their verified identifiers. Reverts if not found.
     */
    function getPlayerIndexByVerifiedId(
        DataTypes.Player[] storage _players,
        string calldata k,
        string calldata v
    ) internal view returns (uint256) {
        for (uint256 i; i < _players.length; ++i) {
            DataTypes.Player storage p = _players[i];
            for (uint256 j; j < p.verifiedIdKey.length; ++j) {
                if (
                    keccak256(bytes(p.verifiedIdKey[j])) == keccak256(bytes(k)) &&
                    keccak256(bytes(p.verifiedIdValue[j])) == keccak256(bytes(v))
                ) {
                    return i;
                }
            }
        }
        revert NotFound();
    }
}

