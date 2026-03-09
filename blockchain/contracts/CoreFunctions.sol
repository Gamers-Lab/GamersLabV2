// SPDX-License-Identifier: MIT
pragma solidity ^0.8.28;

import "@openzeppelin/contracts-upgradeable/access/AccessControlUpgradeable.sol";
import "@openzeppelin/contracts-upgradeable/utils/PausableUpgradeable.sol";
import "@openzeppelin/contracts-upgradeable/proxy/utils/Initializable.sol";
import "@openzeppelin/contracts-upgradeable/proxy/utils/UUPSUpgradeable.sol";
import "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import "@openzeppelin/contracts/token/ERC20/utils/SafeERC20.sol";

import "./DataTypes.sol";
import "./CoreLib.sol";
import "./EntityLib.sol";
import "./SessionLib.sol";
import "./MatchSessionLib.sol";
import "./RecordLib.sol";
import "./AdminLib.sol";
import "./BatchLib.sol";
import "./LibErrors.sol";

/**
 * @title OnChainDataStorage
 * @dev Main contract for storing and managing game-related data on-chain.
 * Handles players, sessions, matches, and application data with role-based access control.
 * Uses UUPS proxy pattern for upgradeability.
 */
contract OnChainDataStorage is Initializable, AccessControlUpgradeable, PausableUpgradeable, UUPSUpgradeable {
    using EntityLib for DataTypes.Player[];
    using EntityLib for DataTypes.Application;
    using EntityLib for DataTypes.ApplicationRecord[];
    using SessionLib for DataTypes.LoginSession[];
    using MatchLib for DataTypes.MatchSession[];
    using MatchLib for DataTypes.PlayData[];
    using SafeERC20 for IERC20;

    /* -------------------------------- ROLES -------------------------------- */
    /// @notice Role identifier for moderators who can manage players and sessions
    bytes32 public constant MODERATOR_ROLE = keccak256("MODERATOR_ROLE");

    /* ------------------------------- STORAGE ------------------------------- */

    // players
    /// @notice Array of all registered players
    DataTypes.Player[] private players;
    /// @notice Mapping from player address to player index (1-based)
    mapping(address => uint256) private playersByAddress; // addr → idx+1
    /// @notice Mapping from player unique ID to player index
    mapping(string => uint256) private playerByUniqueId;
    /// @notice Mapping from verified address to player index (1-based, 0 = not verified)
    mapping(address => uint256) private playerByVerifiedAddress;

    // login sessions
    /// @notice Array of all login sessions
    DataTypes.LoginSession[] private loginSessions;

    // application
    /// @notice Application information
    DataTypes.Application public application;
    /// @notice Array of application records/versions
    DataTypes.ApplicationRecord[] private applicationRecords;

    // match sessions / play‑data
    /// @notice Array of all match sessions
    DataTypes.MatchSession[] private matchSessions;
    /// @notice Array of active match session indices
    uint256[] private activeMatchSessions;

    /// @notice Array of all play data records
    DataTypes.PlayData[] private playDatas;

    // records
    /// @notice Array of all player records
    DataTypes.Records[] private records;
    /// @notice Mapping from player index to all their record IDs
    mapping(uint256 => uint256[]) private recordsByPlayer;

    // key-value registry for record deduplication
    /// @notice Registry of unique key-value pairs
    DataTypes.KeyValue[] private keyValueRegistry;
    /// @notice Mapping from keccak256(key, value) hash to registry index + 1 (0 = not found)
    mapping(bytes32 => uint64) private keyValueHash;

    /// @notice Array of banned player indices
    uint256[] private banned;
    /// @notice Mapping from player index to ban index
    mapping(uint256 => uint256) private bannedByPlayer;

    /* ------------------------------ EVENTS ----------------------------- */
    /// @notice Emitted when a player is banned
    /// @param playerId The index of the banned player
    /// @param startTime The timestamp when the ban started
    event UserBanned(uint256 indexed playerId, uint256 startTime);

    /// @notice Emitted when a player is unbanned
    /// @param playerId The index of the unbanned player
    /// @param timestamp The timestamp when the player was unbanned
    event UserUnbanned(uint256 indexed playerId, uint256 timestamp);

    /// @notice Emitted when a new player is created
    /// @param playerIndex The index of the created player
    /// @param playerAddress The address of the player
    /// @param playerID The unique ID of the player
    /// @param playerType The type of player (Human, Bot, etc.)
    event PlayerCreated(
        uint256 indexed playerIndex,
        address indexed playerAddress,
        string playerID,
        DataTypes.PlayerType playerType
    );

    /// @notice Emitted when an identifier is added to a player
    /// @param playerIndex The index of the player
    /// @param identifierType The type of identifier
    /// @param identifierValue The value of the identifier
    event PlayerIdentifierAdded(
        uint256 indexed playerIndex,
        string identifierType,
        string identifierValue
    );

    /// @notice Emitted when a player's score is updated
    /// @param playerIndex The index of the player
    /// @param newScore The new score of the player
    event PlayerScoreUpdated(uint256 indexed playerIndex, int256 newScore);

    /// @notice Emitted when a verified address is added to a player
    /// @param playerIndex The index of the player
    /// @param verifiedAddress The verified address
    event PlayerVerifiedAddressAdded(
        uint256 indexed playerIndex,
        address indexed verifiedAddress
    );

    /// @notice Emitted when a verified address is removed from a player
    /// @param playerIndex The index of the player
    /// @param verifiedAddress The removed verified address
    event PlayerVerifiedAddressRemoved(
        uint256 indexed playerIndex,
        address indexed verifiedAddress
    );

    /* ─ login events ─ */
    /// @notice Emitted when a login session is created
    /// @param sessionId The ID of the created session
    /// @param playerIndex The index of the player
    /// @param startTime The start time of the session
    /// @param device The device used for the session
    /// @param applicationId The ID of the application
    event LoginSessionCreated(
        uint256 indexed sessionId,
        uint256 indexed playerIndex,
        uint256 indexed startTime,
        DataTypes.Device device,
        uint256 applicationId
    );

    /// @notice Emitted to track device information for a login session (not stored on-chain)
    /// @param sessionId The ID of the session
    /// @param device The device used for the session
    event LoginSessionDevice(
        uint256 indexed sessionId,
        DataTypes.Device device
    );

    /// @notice Emitted when a login session ends
    /// @param sessionId The ID of the ended session
    /// @param endTime The end time of the session
    event LoginSessionEnded(uint256 indexed sessionId, uint256 indexed endTime);

    /// @notice Emitted when metadata is added to a login session
    /// @param sessionId The ID of the session
    /// @param key The metadata key
    /// @param value The metadata value
    event LoginSessionMetadataAdded(
        uint256 indexed sessionId,
        string key,
        string value
    );

    /* ─ match / play‑data events ─ */
    /// @notice Emitted when a match session starts
    /// @param matchSessionId The ID of the match session
    /// @param applicationId The ID of the application
    /// @param startTime The start time of the match
    /// @param playerIndices The indices of the players in the match
    /// @param level The level/map of the match
    /// @param multiplayer Whether the match is multiplayer (derived from playerIndices.length > 1)
    event MatchSessionStarted(
        uint256 indexed matchSessionId,
        uint256 applicationId,
        uint256 indexed startTime,
        uint256[] playerIndices,
        string level,
        bool multiplayer
    );

    /// @notice Emitted when a match session ends
    /// @param matchSessionId The ID of the match session
    /// @param endTime The end time of the match
    event MatchSessionEnded(
        uint256 indexed matchSessionId,
        uint256 indexed endTime
    );

    /// @notice Emitted when a stale match session is expired during cleanup
    /// @param matchSessionId The ID of the expired match session
    event MatchSessionExpired(uint256 indexed matchSessionId);

    /// @notice Emitted when a player is added to a match session
    /// @param matchSessionId The ID of the match session
    /// @param playerIndex The index of the player
    /// @param playerDataId The ID of the player's data
    event PlayerAddedToMatchSession(
        uint256 indexed matchSessionId,
        uint256 indexed playerIndex,
        uint256 playerDataId
    );

    /// @notice Emitted when player data is updated
    /// @param playerIndex The index of the player
    /// @param playDataId The ID of the play data
    /// @param endTime The end time of the play data
    /// @param score The player's score
    /// @param winLoss The win/loss status
    /// @param matchPosition The player's position in the match
    event PlayerDataUpdated(
        uint256 indexed playerIndex,
        uint256 indexed playDataId,
        uint256 endTime,
        int256 score,
        DataTypes.WinLoss winLoss,
        int256 matchPosition
    );

    /* ─ application events ─ */
    /// @notice Emitted when the application is set
    /// @param name The name of the application
    /// @param owner The owner of the application
    event ApplicationSet(string name, address indexed owner);

    /// @notice Emitted when an application record is created
    /// @param recordId The ID of the record
    /// @param applicationVersion The version of the application
    /// @param startDate The start date of this version
    /// @param companyName The name of the company
    /// @param contractAddresses The addresses of the contracts
    event ApplicationRecordCreated(
        uint256 indexed recordId,
        uint256 applicationVersion,
        uint256 startDate,
        string companyName,
        address[] contractAddresses
    );

    /// @notice Emitted when application record addresses are updated
    /// @param recordId The ID of the record
    /// @param contractAddresses The updated contract addresses
    event ApplicationRecordAddressesUpdated(
        uint256 indexed recordId,
        address[] contractAddresses
    );

    /// @notice Emitted when metadata is added to an application record
    /// @param recordId The ID of the record
    /// @param key The metadata key
    /// @param value The metadata value
    event ApplicationRecordMetadataAdded(
        uint256 indexed recordId,
        string key,
        string value
    );

    /* ─ misc ─ */
    /// @notice Emitted when metadata is added to a match session
    /// @param matchSessionId The ID of the match session
    /// @param key The metadata key
    /// @param value The metadata value
    event MatchSessionMetadataAdded(
        uint256 indexed matchSessionId,
        string key,
        string value
    );

    /// @notice Emitted when a record is set
    /// @param recordId The ID of the record
    /// @param playerIndex The ID of the player
    /// @param playDataId The ID of the play data
    /// @param score The score for the record
    /// @param key The record key
    /// @param value The record value
    /// @param otherPlayers Other players involved
    /// @param startTime The start time of the record
    /// @param isPlayerAccount True if this record is tied to player account (not a match)
    event RecordSet(
        uint256 indexed recordId,
        uint256 indexed playerIndex,
        uint256 indexed playDataId,
        int256 score,
        string key,
        string value,
        uint256[] otherPlayers,
        uint256 startTime,
        bool isPlayerAccount
    );

    event PlayerMetadataAdded(
        uint256 indexed playerIndex,
        string key,
        string value
    );

    event PlayerIdUpdated(uint256 indexed playerIndex, string playerID);

    /* ─ Tag Events (event-only, no storage) ─ */
    /// @notice Emitted when a login session is tagged
    /// @param sessionId The ID of the session
    /// @param playerIndex The player who owns the session
    /// @param tag The tag string
    event LoginSessionTagged(
        uint256 indexed sessionId,
        uint256 indexed playerIndex,
        string tag
    );

    /// @notice Emitted when a match session is tagged
    /// @param matchSessionId The ID of the match session
    /// @param tag The tag string
    event MatchSessionTagged(uint256 indexed matchSessionId, string tag);

    /// @notice Emitted when play data is tagged
    /// @param playDataId The ID of the play data
    /// @param playerIndex The player who owns the play data
    /// @param tag The tag string
    event PlayDataTagged(
        uint256 indexed playDataId,
        uint256 indexed playerIndex,
        string tag
    );

    /// @notice Emitted when a record is tagged
    /// @param recordId The ID of the record
    /// @param playerIndex The player who owns the record
    /// @param tag The tag string
    event RecordTagged(
        uint256 indexed recordId,
        uint256 indexed playerIndex,
        string tag
    );

    /// @notice Emitted when a player is tagged
    /// @param playerIndex The index of the player
    /// @param tag The tag string
    event PlayerTagged(uint256 indexed playerIndex, string tag);

    /// @notice Emitted when an application record is tagged
    /// @param recordId The ID of the application record
    /// @param tag The tag string
    event ApplicationRecordTagged(uint256 indexed recordId, string tag);

    /* ----------------------------- INITIALIZER ----------------------------- */

    /// @custom:oz-upgrades-unsafe-allow constructor
    constructor() {
        _disableInitializers();
    }

    /**
     * @dev Initializes the contract with the deployer as the default admin and moderator.
     *      Also initializes the application, creates the admin player, and creates
     *      the first application record.
     * @param _appName The name of the application
     * @param _companyName The name of the company
     */
    function initialize(
        string memory _appName,
        string memory _companyName
    ) public initializer {
        __AccessControl_init();
        __Pausable_init();
        __UUPSUpgradeable_init();

        _grantRole(DEFAULT_ADMIN_ROLE, msg.sender);
        _grantRole(MODERATOR_ROLE, msg.sender);

        // 1. Set application
        EntityLib.setApplicationMemory(application, _appName, msg.sender);
        emit ApplicationSet(_appName, msg.sender);

        // 2. Create admin player (index 0)
        EntityLib.createPlayerMemory(
            players,
            playerByUniqueId,
            playersByAddress,
            "admin",
            DataTypes.PlayerType.Admin,
            msg.sender
        );
        emit PlayerCreated(0, msg.sender, "admin", DataTypes.PlayerType.Admin);

        // 3. Create application record v1
        EntityLib.createApplicationRecordMemory(
            applicationRecords,
            1, // version 1
            _companyName,
            block.timestamp // startDate = now
        );
        emit ApplicationRecordCreated(
            0,
            1,
            block.timestamp,
            _companyName,
            new address[](0)
        );
    }

    /**
     * @dev Required override for UUPS proxy pattern.
     *      Only admin can authorize upgrades.
     */
    function _authorizeUpgrade(address newImplementation) internal override onlyRole(DEFAULT_ADMIN_ROLE) {}

    modifier modOnlyWhenActive() {
        require(!paused(), "paused");
        require(hasRole(MODERATOR_ROLE, msg.sender), "not mod");
        _;
    }

    /* ---------------------------- WITHDRAW TOKENS ---------------------------- */
    /**
     * @notice Rescues ETH accidentally sent to this contract
     * @param recipient The address to send the ETH to
     * @dev Only callable by the admin
     */
    function rescueETH(
        address payable recipient
    ) external onlyRole(DEFAULT_ADMIN_ROLE) {
        uint256 bal = address(this).balance;
        if (bal == 0) revert NotFound();
        (bool ok, ) = recipient.call{value: bal}("");
        if (!ok) revert Invalid();
    }

    /**
     * @notice Rescues ERC20 tokens accidentally sent to this contract
     * @param token The ERC20 token address to rescue
     * @param recipient The address to send the tokens to
     * @dev Only callable by the admin. Uses SafeERC20 for compatibility with non-standard tokens (e.g., USDT).
     */
    function rescueERC20(
        address token,
        address recipient
    ) external onlyRole(DEFAULT_ADMIN_ROLE) {
        uint256 balance = IERC20(token).balanceOf(address(this));
        if (balance == 0) revert NotFound();
        IERC20(token).safeTransfer(recipient, balance);
    }

    /* ------------------------- APPLICATION LOGIC -------------------------- */
    /**
     * @notice Sets the application name and owner
     * @param _name The name of the application
     * @dev Only callable by the admin when not paused
     */
    function setApplication(
        string calldata _name
    ) external whenNotPaused onlyRole(DEFAULT_ADMIN_ROLE) {
        application.setApplication(_name, msg.sender);
        emit ApplicationSet(_name, msg.sender);
    }

    /**
     * @notice Creates a new application record
     * @param _ver The version of the application (uint)
     * @param _company The company name
     * @param _contracts The contract addresses
     * @param _startDate The start date for this version
     * @return recordId The ID of the created record
     * @dev Only callable by the admin when not paused
     */
    function createApplicationRecord(
        uint256 _ver,
        string calldata _company,
        address[] calldata _contracts,
        uint256 _startDate
    )
        external
        whenNotPaused
        onlyRole(DEFAULT_ADMIN_ROLE)
        returns (uint256 recordId)
    {
        recordId = applicationRecords.createApplicationRecord(
            _ver,
            _company,
            _contracts,
            _startDate
        );

        emit ApplicationRecordCreated(
            recordId,
            _ver,
            _startDate,
            _company,
            _contracts
        );
    }

    /**
     * @notice Updates the contract addresses for an application record
     * @param _id The ID of the record to update
     * @param _contracts The new contract addresses
     * @dev Only callable by the admin when not paused
     */
    function updateApplicationRecordAddresses(
        uint256 _id,
        address[] calldata _contracts
    ) external whenNotPaused onlyRole(DEFAULT_ADMIN_ROLE) {
        if (_id >= applicationRecords.length) revert IndexOutOfBounds();
        applicationRecords.updateApplicationRecordAddresses(_id, _contracts);
        emit ApplicationRecordAddressesUpdated(_id, _contracts);
    }

    /* --------------------------- APPPLICATION GETTERS ----------------------------- */

    /**
     * @notice Gets the index of the latest application record
     * @return The index of the latest record (0 for first entry)
     * @dev Reverts with NotFound() if no application records exist
     */
    function getLatestApplicationRecord() external view returns (uint256) {
        uint256 len = applicationRecords.length;
        if (len == 0) revert NotFound();
        return len - 1;
    }

    /**
     * @notice Gets an application record by index
     * @param idx The index of the record
     * @return ver The application version
     * @return startDate_ The start date of this version
     * @return endDate_ The end date of this version (0 = active)
     * @return company The company name
     * @return contracts_ The contract addresses
     * @dev Reverts with NotFound() if no records exist, IndexOutOfBounds() if idx is invalid
     */
    function getApplicationRecordByIndex(
        uint256 idx
    )
        external
        view
        returns (
            uint256 ver,
            uint256 startDate_,
            uint256 endDate_,
            string memory company,
            address[] memory contracts_
        )
    {
        uint256 len = applicationRecords.length;
        if (len == 0) revert NotFound();
        if (idx >= len) revert IndexOutOfBounds();
        DataTypes.ApplicationRecord storage r = applicationRecords[idx];
        return (
            uint256(r.applicationVersion),
            uint256(r.startDate),
            uint256(r.endDate),
            r.companyName,
            r.contractAddresses
        );
    }

    /**
     * @notice Gets the total number of application records
     * @return The number of application records
     */
    function getApplicationRecordCount() external view returns (uint256) {
        return applicationRecords.length;
    }

    /* --------------------------- PLAYER LOGIC ----------------------------- */
    /**
     * @notice Creates a new player
     * @param _pid The unique ID of the player
     * @param _ptype The type of player
     * @param _addr The address of the player
     * @return idx The index of the created player
     * @dev Only callable by moderators when not paused
     */
    function createPlayer(
        string calldata _pid,
        DataTypes.PlayerType _ptype,
        address _addr
    ) external modOnlyWhenActive returns (uint256 idx) {
        idx = players.createPlayer(
            playerByUniqueId,
            playersByAddress,
            _pid,
            _ptype,
            _addr
        );
        emit PlayerCreated(idx, _addr, _pid, _ptype);
    }

    /**
     * @notice Updates a player's score
     * @param idx The index of the player
     * @param newScore The new score
     * @dev Only callable by moderators when not paused
     */
    function updatePlayerScore(
        uint256 idx,
        int256 newScore
    ) external modOnlyWhenActive {
        if (idx >= players.length) revert IndexOutOfBounds();
        players[idx].totalScore = newScore;
        emit PlayerScoreUpdated(idx, newScore);
    }

    /**
     * @notice Adds a verified address to a player. Should be verified by mod or admin before adding.
     * @param idx The index of the player
     * @param a The address to verify
     * @dev Only callable by moderators when not paused. Address must be globally unique.
     */
    function addVerifiedAddress(
        uint256 idx,
        address a
    ) external modOnlyWhenActive {
        players.addVerifiedAddress(playerByVerifiedAddress, idx, a);
        emit PlayerVerifiedAddressAdded(idx, a);
    }

    /**
     * @notice Removes a verified address from a player
     * @param idx The index of the player
     * @param a The address to remove
     * @dev Only callable by moderators when not paused
     */
    function removeVerifiedAddress(
        uint256 idx,
        address a
    ) external modOnlyWhenActive {
        players.removeVerifiedAddress(playerByVerifiedAddress, idx, a);
        emit PlayerVerifiedAddressRemoved(idx, a);
    }

    /**
     * @notice Adds an identifier to a player, such as Steam ID | ID. Should be verified by mod or admin before adding.
     * @param idx The index of the player
     * @param k The identifier key
     * @param v The identifier value
     * @dev Only callable by moderators when not paused
     */
    function addPlayerIdentifier(
        uint256 idx,
        string calldata k,
        string calldata v
    ) external modOnlyWhenActive {
        players.addPlayerIdentifier(idx, k, v);
        emit PlayerIdentifierAdded(idx, k, v);
    }

     /**
     * @notice Updates a player's unique username
     * @param idx The index of the player
     * @param newPid The new unique ID
     * @dev Only callable by moderators when not paused
     */
    function updatePlayerUniqueUsername(
        uint256 idx,
        string calldata newPid
    ) external modOnlyWhenActive {
        players.updateUniqueId(playerByUniqueId, idx, newPid);
        emit PlayerIdUpdated(idx, newPid);
    }

    /**
     * @notice Sets metadata for a player
     * @param idx The index of the player
     * @param k The metadata key
     * @param v The metadata value
     * @dev Only callable by moderators when not paused
     */
    function setPlayerMetadata(
        uint256 idx,
        string calldata k,
        string calldata v
    ) external modOnlyWhenActive {
        if (idx >= players.length) revert IndexOutOfBounds();

        CoreLib.addMetadata(players[idx].metadata, k, v);

        emit PlayerMetadataAdded(idx, k, v);
    }

    /* Player Getters */

    /**
     * @notice Gets a player's details by index
     * @param idx The index of the player
     * @return pid The player's unique ID
     * @return addr The player's address
     * @return ptype The player's type
     * @return score The player's total score
     * @return idKeys The player's identifier keys
     * @return idVals The player's identifier values
     * @return verifiedAddrs The player's verified addresses
     * @return meta_ The player's metadata
     */
    function getPlayerByIndex(
        uint256 idx
    )
        external
        view
        returns (
            string memory pid,
            address addr,
            DataTypes.PlayerType ptype,
            int256 score,
            string[] memory idKeys,
            string[] memory idVals,
            address[] memory verifiedAddrs,
            DataTypes.MetaData memory meta_
        )
    {
        if (idx >= players.length) revert IndexOutOfBounds();
        DataTypes.Player storage p = players[idx];
        return (
            p.playerID,
            p.playerAddress,
            p.playerType,
            p.totalScore,
            p.verifiedIdKey,
            p.verifiedIdValue,
            p.verifiedAddresses,
            p.metadata
        );
    }

    /**
     * @notice Gets a player's index by address
     * @param a The player's address
     * @return The player's index
     * @dev Reverts if no player with the address exists
     */
    function getPlayerIndexByAddress(
        address a
    ) external view returns (uint256) {
        uint256 stored = playersByAddress[a];
        if (stored == 0) revert NotFound();
        return stored - 1;
    }

    /**
     * @notice Gets a player's index by verified address
     * @param a The verified address
     * @return The player's index
     * @dev Reverts if no player has this verified address
     */
    function getPlayerIndexByVerifiedAddress(
        address a
    ) external view returns (uint256) {
        uint256 stored = playerByVerifiedAddress[a];
        if (stored == 0) revert NotFound();
        return stored - 1;
    }

    /**
     * @notice Gets a player's unique username by index
     * @param idx The player's index
     * @return The player's unique username
     * @dev Reverts if no players exist or index is out of bounds
     */
    function getPlayerUniqueUsernameByIndex(
        uint256 idx
    ) external view returns (string memory) {
        if (players.length == 0) revert NotFound();
        if (idx >= players.length) revert IndexOutOfBounds();
        return players[idx].playerID;
    }

    /**
     * @notice Gets a player's index by unique username
     * @param _pid The player's unique username
     * @return The player's index
     * @dev Reverts if no player with the ID exists
     */
    function getPlayerIndexByUniqueUsername(
        string calldata _pid
    ) external view returns (uint256) {
        uint256 stored = playerByUniqueId[_pid];
        if (stored == 0) revert NotFound();
        return stored - 1;
    }

    /**
     * @notice Gets the total number of players
     * @return The number of players
     */
    function getPlayerCount() external view returns (uint256) {
        return players.length;
    }

    /**
     * @notice Gets a player's index by verified identifier
     * @param k The identifier key
     * @param v The identifier value
     * @return The player's index
     * @dev Reverts if no player with the identifier exists
     */
    function getPlayerIndexByVerifiedId(
        string calldata k,
        string calldata v
    ) external view returns (uint256) {
        return players.getPlayerIndexByVerifiedId(k, v);
    }

    /* Login Sessions */

    /**
     * @notice Creates a login session for a player
     * @param idx The index of the player
     * @param appId The application ID
     * @param start The start time of the session
     * @param devc The device type for this login session
     * @return sid The ID of the created session
     * @dev Only callable by moderators when not paused
     */
    function loginSessionCreate(
        uint256 idx,
        uint256 appId,
        uint256 start,
        DataTypes.Device devc
    ) external modOnlyWhenActive returns (uint256 sid) {
        if (appId >= applicationRecords.length) revert IndexOutOfBounds();
        if (idx >= players.length) revert IndexOutOfBounds();
        if (devc >= DataTypes.Device.Last) revert IndexOutOfBounds();

        sid = SessionLib.loginSessionCreate(
            loginSessions,
            idx,
            appId,
            start,
            devc
        );

        emit LoginSessionCreated(sid, idx, start, devc, appId);
    }

    /**
     * @notice Ends a login session
     * @param sid The ID of the session
     * @param end The end time of the session
     * @dev Only callable by moderators when not paused
     */
    function loginSessionEnd(
        uint256 sid,
        uint256 end
    ) external modOnlyWhenActive {
        SessionLib.loginSessionEnd(loginSessions, sid, end);
        emit LoginSessionEnded(sid, end);
    }

    /**
     * @notice Sets metadata for a login session
     * @param sid The ID of the session
     * @param k The metadata key
     * @param v The metadata value
     * @dev Only callable by moderators when not paused
     */
    function setLoginSessionMetadata(
        uint256 sid,
        string calldata k,
        string calldata v
    ) external modOnlyWhenActive {
        if (sid >= loginSessions.length) revert IndexOutOfBounds();

        CoreLib.addMetadata(loginSessions[sid].metadata, k, v);

        emit LoginSessionMetadataAdded(sid, k, v);
    }

    /* Login Session Getters */

    /**
     * @notice Gets the total number of login sessions
     * @return The number of login sessions
     */
    function getLoginSessionCount() external view returns (uint256) {
        return loginSessions.length;
    }

    /**
     * @notice Gets a login session by ID
     * @param sid The ID of the session
     * @return base_ The base data of the session (includes playerIndex)
     * @return meta_ The metadata of the session
     * @return matchIds_ The match IDs associated with the session
     * @return device_ The device used for this session
     */
    function getLoginSessionBySessionId(
        uint256 sid
    )
        external
        view
        returns (
            DataTypes.BaseData memory base_,
            DataTypes.MetaData memory meta_,
            uint256[] memory matchIds_,
            DataTypes.Device device_
        )
    {
        if (sid >= loginSessions.length) revert IndexOutOfBounds();
        DataTypes.LoginSession storage s = loginSessions[sid];
        return (
            s.baseData,
            s.metadata,
            s.matchIds,
            s.device
        );
    }

    /* Match Sessions */

    /**
     * @notice Starts a match session
     * @param appId The application ID
     * @param start The start time of the match
     * @param pIdx Array of player indices
     * @param level The level/map of the match
     * @return mid The ID of the created match
     * @dev Only callable by moderators when not paused
     */
    function startMatchSession(
        uint256 appId,
        uint256 start,
        uint256[] calldata pIdx,
        string calldata level
    ) external modOnlyWhenActive returns (uint256 mid) {
        if (appId >= applicationRecords.length) revert IndexOutOfBounds();

        mid = MatchLib.startMatchSession(
            activeMatchSessions,
            loginSessions,
            matchSessions,
            playDatas,
            appId,
            start,
            pIdx,
            level
        );

        // Emit the main match session event (multiplayer derived from player count)
        emit MatchSessionStarted(
            mid,
            appId,
            start,
            pIdx,
            level,
            pIdx.length > 1
        );

        // Emit individual events for each player
        for (uint256 i = 0; i < pIdx.length; i++) {
            emit PlayerAddedToMatchSession(
                mid,
                pIdx[i],
                matchSessions[mid].playData[i]
            );
        }
    }

    /**
     * @notice Ends a match session
     * @param mid The ID of the match
     * @param end The end time of the match
     * @dev Only callable by moderators when not paused
     */
    function endMatchSession(
        uint256 mid,
        uint256 end
    ) external modOnlyWhenActive {
        MatchLib.endMatchSession(
            matchSessions,
            activeMatchSessions,
            playDatas,
            mid,
            end
        );
        emit MatchSessionEnded(mid, end);
    }

    /**
     * @notice Sets metadata for a match session
     * @param mid The ID of the match session
     * @param k The metadata key
     * @param v The metadata value
     * @dev Only callable by moderators when not paused
     */
    function setMatchSessionMetadata(
        uint256 mid,
        string calldata k,
        string calldata v
    ) external modOnlyWhenActive {
        if (mid >= matchSessions.length) revert IndexOutOfBounds();

        CoreLib.addMetadata(matchSessions[mid].metadata, k, v);

        emit MatchSessionMetadataAdded(mid, k, v);
    }

    /**
     * @notice Cleans up stale match sessions that have been active too long
     * @param staleMinutes Minutes after which a match is considered stale
     * @param maxCleanup Maximum number of matches to clean in this call (suggested: 64)
     * @return cleanedIds Array of match IDs that were cleaned up
     * @dev Only callable by moderators when not paused.
     */
    function cleanupStaleMatches(
        uint256 staleMinutes,
        uint256 maxCleanup
    ) external modOnlyWhenActive returns (uint256[] memory cleanedIds) {
        uint256 cutoff = block.timestamp - (staleMinutes * 1 minutes);

        cleanedIds = MatchLib.cleanupStaleMatches(
            activeMatchSessions,
            matchSessions,
            playDatas,
            cutoff,
            maxCleanup
        );

        for (uint256 i; i < cleanedIds.length; ++i) {
            emit MatchSessionExpired(cleanedIds[i]);
        }
    }

    /* Match Session Getters */

    /**
     * @notice Gets a match session by index
     * @param idx The index of the match
     * @return base_ The base data of the match
     * @return meta_ The metadata of the match
     * @return playDataIds The play data IDs
     * @return level The level/map of the match
     */
    function getMatchSessionByIndex(
        uint256 idx
    )
        external
        view
        returns (
            DataTypes.BaseData memory base_,
            DataTypes.MetaData memory meta_,
            uint256[] memory playDataIds,
            string memory level
        )
    {
        if (idx >= matchSessions.length) revert IndexOutOfBounds();
        DataTypes.MatchSession storage m = matchSessions[idx];
        return (m.baseData, m.metadata, m.playData, m.level);
    }

    /**
     * @notice Gets active match session IDs with pagination
     * @param offset Starting index in the array
     * @param limit Maximum number of IDs to return
     * @return ids Array of active match session IDs (up to `limit` items)
     * @return total Total number of active match sessions
     */
    function getActiveMatchSessionIds(
        uint256 offset,
        uint256 limit
    ) external view returns (uint256[] memory ids, uint256 total) {
        total = activeMatchSessions.length;

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
            ids[i] = activeMatchSessions[offset + i];
        }
    }

    /**
     * @notice Gets the total number of match sessions
     * @return The number of match sessions
     */
    function getMatchSessionCount() external view returns (uint256) {
        return matchSessions.length;
    }

    /* Play Data */

    /**
     * @notice Adds a player to a match session
     * @param mid The ID of the match
     * @param pIdx The index of the player
     * @param startTime The start time for the player
     * @return pdId The ID of the created play data
     * @dev Only callable by moderators when not paused
     */
    function addPlayerToMatchSession(
        uint256 mid,
        uint256 pIdx,
        uint256 startTime
    ) external modOnlyWhenActive returns (uint256 pdId) {
        if (pIdx >= players.length) revert IndexOutOfBounds();

        pdId = MatchLib.addPlayerToMatch(
            matchSessions,
            playDatas,
            mid,
            pIdx,
            startTime
        );

        emit PlayerAddedToMatchSession(mid, pIdx, pdId);
    }

    /**
     * @notice Sets play data for a player in a match session (can only be set once)
     * @param mid The ID of the match
     * @param pIdx The index of the player
     * @param end The end time
     * @param score The player's score
     * @param wl The win/loss status
     * @param pos The player's position in the match
     * @return pdId The ID of the set play data
     * @return playerIdx The player index
     * @dev Only callable by moderators when not paused
     * @dev Play data can be set even after match ends (for out-of-order transactions)
     * @dev Reverts with PlayDataAlreadySet if play data was already finalized
     */
    function setPlayDataForMatchSession(
        uint256 mid,
        uint256 pIdx,
        uint256 end,
        int256 score,
        DataTypes.WinLoss wl,
        int256 pos
    ) external modOnlyWhenActive returns (uint256 pdId, uint256 playerIdx) {
        uint256 playId = RecordLib.findPlayDataId(matchSessions, playDatas, mid, pIdx);

        (pdId, playerIdx) = MatchLib.setPlayData(
            playId,
            playDatas,
            end,
            score,
            wl,
            pos
        );

        emit PlayerDataUpdated(playerIdx, pdId, end, score, wl, pos);
    }

    /* Play Data Getters */

    /**
     * @notice Gets play data by index
     * @param idx The index of the play data
     * @return base_ The base data
     * @return playerIdx The player index
     * @return score The player's score
     * @return wl The win/loss status
     * @return pos The player's position
     * @return recIds The record IDs
     */
    function getPlayDataByIndex(
        uint256 idx
    )
        external
        view
        returns (
            DataTypes.BaseData memory base_,
            uint256 playerIdx,
            int256 score,
            DataTypes.WinLoss wl,
            int256 pos,
            uint256[] memory recIds
        )
    {
        if (idx >= playDatas.length) revert IndexOutOfBounds();
        DataTypes.PlayData storage pd = playDatas[idx];
        return (
            pd.baseData,
            uint256(pd.baseData.playerIndex),
            int256(pd.score),
            pd.winLoss,
            int256(pd.matchPosition),
            pd.records
        );
    }

    /**
     * @notice Gets the total number of play data records
     * @return The number of play data records
     */
    function getPlayDataCount() external view returns (uint256) {
        return playDatas.length;
    }

    /* ----------------------------- RECORDS -------------------------------- */

    /**
     * @notice Gets or creates a KeyValue entry in the registry
     * @param _key The record key
     * @param _value The record value
     * @return keyValueId The ID of the KeyValue entry
     * @dev Uses hash lookup to deduplicate key-value pairs
     */
    function _getOrCreateKeyValueId(
        string calldata _key,
        string calldata _value
    ) internal returns (uint64) {
        return RecordLib.getOrCreateKeyValueId(keyValueRegistry, keyValueHash, _key, _value);
    }

    /**
     * @notice Sets a record for a player
     * @param mid The ID of the match (ignored if isPlayerAccount is true)
     * @param pIdx The index of the player
     * @param score The player's score
     * @param key The record key
     * @param value The record value
     * @param others Array of other player indices involved
     * @param start The start time of the record
     * @param isPlayerAccount True if this record is tied to player account (not a match)
     * @return rid The ID of the created record
     * @return playerId The player ID
     * @dev Only callable by moderators when not paused
     */
    function setRecord(
        uint256 mid,
        uint256 pIdx,
        int256 score,
        string calldata key,
        string calldata value,
        uint256[] calldata others,
        uint256 start,
        bool isPlayerAccount
    ) external modOnlyWhenActive returns (uint256 rid, uint256 playerId) {
        // Use RecordLib.processRecordInput by constructing a RecordInput struct
        DataTypes.RecordInput memory input = DataTypes.RecordInput({
            matchId: mid,
            playerIndex: pIdx,
            score: score,
            key: key,
            value: value,
            otherPlayers: others,
            startTime: start,
            isPlayerAccount: isPlayerAccount
        });

        uint256 pdId;
        (rid, playerId, pdId) = RecordLib.processRecordInputMemory(
            input,
            matchSessions,
            playDatas,
            records,
            recordsByPlayer,
            keyValueRegistry,
            keyValueHash
        );

        emit RecordSet(rid, playerId, pdId, score, key, value, others, start, isPlayerAccount);
    }

    /**
     * @notice Batch sets multiple records in one transaction
     * @param inputs An array of RecordInput structs (includes isPlayerAccount flag)
     * @dev Only callable by moderators when not paused
     */
    function batchSetRecords(
        DataTypes.RecordInput[] calldata inputs
    ) external modOnlyWhenActive {
        uint256 len = inputs.length;
        for (uint256 i = 0; i < len; ++i) {
            DataTypes.RecordInput calldata input = inputs[i];

            (uint256 rid, uint256 playerId, uint256 pdId) = RecordLib.processRecordInput(
                input,
                matchSessions,
                playDatas,
                records,
                recordsByPlayer,
                keyValueRegistry,
                keyValueHash
            );

            emit RecordSet(
                rid,
                playerId,
                pdId,
                input.score,
                input.key,
                input.value,
                input.otherPlayers,
                input.startTime,
                input.isPlayerAccount
            );
        }
    }

    /**
     * @notice Gets a record by index
     * @param idx The index of the record
     * @return id The record ID
     * @return key The record key
     * @return value The record value
     * @return score The score
     * @return start The start time
     * @return playerIdx The player index
     * @return others Other player indices involved
     */
    function getRecordsByIndex(
        uint256 idx
    )
        external
        view
        returns (
            uint256 id,
            string memory key,
            string memory value,
            int256 score,
            uint256 start,
            uint256 playerIdx,
            uint256[] memory others
        )
    {
        if (idx >= records.length) revert IndexOutOfBounds();
        DataTypes.Records storage r = records[idx];

        // playerIndex is now stored directly in record's baseData
        playerIdx = uint256(r.baseData.playerIndex);

        // Dereference key-value from registry
        DataTypes.KeyValue storage kv = keyValueRegistry[r.keyValueId];

        return (
            idx,
            kv.key,
            kv.value,
            int256(r.score), // Upcast int64 → int256
            uint256(r.baseData.startTime), // startTime is now in baseData
            playerIdx,
            r.otherPlayer
        );
    }

    // /**
    //  * @notice Gets the total number of records
    //  * @return The number of records
    //  */
    function recordsLength() external view returns (uint256) {
        return records.length;
    }

    /**
     * @notice Gets the total number of key-value entries in the registry
     * @return The number of unique key-value pairs
     */
    function getKeyValueCount() external view returns (uint256) {
        return keyValueRegistry.length;
    }

    /**
     * @notice Gets a key-value pair by its registry ID
     * @param kvId The key-value registry ID
     * @return key The key string
     * @return value The value string
     */
    function getKeyValue(uint256 kvId)
        external
        view
        returns (string memory key, string memory value)
    {
        if (kvId >= keyValueRegistry.length) revert IndexOutOfBounds();
        DataTypes.KeyValue storage kv = keyValueRegistry[kvId];
        return (kv.key, kv.value);
    }

    /**
     * @notice Gets the registry ID for a key-value pair
     * @param key The key string
     * @param value The value string
     * @return The key-value registry ID (reverts if not found)
     */
    function getKeyValueId(string calldata key, string calldata value)
        external
        view
        returns (uint64)
    {
        bytes32 hash = keccak256(abi.encodePacked(key, value));
        uint64 stored = keyValueHash[hash];
        if (stored == 0) revert NotFound();
        return stored - 1; // Convert from 1-based storage
    }

    /**
     * @notice Gets record IDs for a player with pagination
     * @param playerIdx The player index
     * @param offset Starting index in the array
     * @param limit Maximum number of IDs to return
     * @return ids Array of record IDs (up to `limit` items)
     * @return total Total number of records for this player
     */
    function getRecordIdsByPlayer(
        uint256 playerIdx,
        uint256 offset,
        uint256 limit
    ) external view returns (uint256[] memory ids, uint256 total) {
        uint256[] storage allIds = recordsByPlayer[playerIdx];
        total = allIds.length;

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
            ids[i] = allIds[offset + i];
        }
    }

    /* ------------------------- BAN / UNBAN PLAYERS ------------------------ */
    /**
     * @notice Bans a player
     * @param idx The index of the player to ban
     * @dev Only callable by moderators
     * @dev Emits UserBanned event
     */
    function banPlayer(uint256 idx) external modOnlyWhenActive {
        AdminLib.banPlayer(banned, bannedByPlayer, players.length, idx);
        emit UserBanned(idx, block.timestamp);
    }

    /**
     * @notice Unbans a player
     * @param idx The index of the player to unban
     * @dev Only callable by moderators
     * @dev Emits UserUnbanned event
     */
    function unbanPlayer(uint256 idx) external modOnlyWhenActive {
        AdminLib.unbanPlayer(banned, bannedByPlayer, idx);
        emit UserUnbanned(idx, block.timestamp);
    }

    /**
     * @notice Gets banned player indices with pagination
     * @param offset Starting index in the array
     * @param limit Maximum number of IDs to return
     * @return ids Array of banned player indices (up to `limit` items)
     * @return total Total number of banned players
     */
    function getBannedPlayers(
        uint256 offset,
        uint256 limit
    ) external view returns (uint256[] memory ids, uint256 total) {
        return AdminLib.getBannedPlayers(banned, offset, limit);
    }

    /**
     * @notice Checks if a player is banned
     * @param idx The index of the player
     * @return True if the player is banned, false otherwise
     */
    function isPlayerBanned(uint256 idx) external view returns (bool) {
        return AdminLib.isPlayerBanned(bannedByPlayer, players.length, idx);
    }

    /* --------------------------- INTERNAL HELPERS ------------------------- */
    /**
     * @notice Finds the play data ID for a player in a match
     * @param mid The match ID
     * @param pIdx The player index
     * @return id The play data ID
     * @dev Reverts if the player is not in the match or if match ID is invalid
     */
    function findPlayDataId(
        uint256 mid,
        uint256 pIdx
    ) public view returns (uint256 id) {
        return RecordLib.findPlayDataId(matchSessions, playDatas, mid, pIdx);
    }

    /* ----------------------------- TAG FUNCTIONS (Event-Only) ----------------------------- */

    /**
     * @notice Tags a login session (event only, no storage cost)
     * @param sid The ID of the session to tag
     * @param tag The tag string to apply
     * @dev Only callable by moderators when active
     */
    function tagLoginSession(
        uint256 sid,
        string calldata tag
    ) external modOnlyWhenActive {
        if (sid >= loginSessions.length) revert IndexOutOfBounds();
        emit LoginSessionTagged(
            sid,
            loginSessions[sid].baseData.playerIndex,
            tag
        );
    }

    /**
     * @notice Tags a match session (event only, no storage cost)
     * @param mid The ID of the match session to tag
     * @param tag The tag string to apply
     * @dev Only callable by moderators when active
     */
    function tagMatchSession(
        uint256 mid,
        string calldata tag
    ) external modOnlyWhenActive {
        if (mid >= matchSessions.length) revert IndexOutOfBounds();
        emit MatchSessionTagged(mid, tag);
    }

    /**
     * @notice Tags play data (event only, no storage cost)
     * @param pdId The ID of the play data to tag
     * @param tag The tag string to apply
     * @dev Only callable by moderators when active
     */
    function tagPlayData(
        uint256 pdId,
        string calldata tag
    ) external modOnlyWhenActive {
        if (pdId >= playDatas.length) revert IndexOutOfBounds();
        emit PlayDataTagged(pdId, playDatas[pdId].baseData.playerIndex, tag);
    }

    /**
     * @notice Tags a record (event only, no storage cost)
     * @param rId The ID of the record to tag
     * @param playerIndex The player index associated with this record
     * @param tag The tag string to apply
     * @dev Only callable by moderators when active
     */
    function tagRecord(
        uint256 rId,
        uint256 playerIndex,
        string calldata tag
    ) external modOnlyWhenActive {
        if (rId >= records.length) revert IndexOutOfBounds();
        emit RecordTagged(rId, playerIndex, tag);
    }

    /**
     * @notice Tags an application record (event only, no storage cost)
     * @param arId The ID of the application record to tag
     * @param tag The tag string to apply
     * @dev Only callable by admin when not paused
     */
    function tagApplicationRecord(
        uint256 arId,
        string calldata tag
    ) external whenNotPaused onlyRole(DEFAULT_ADMIN_ROLE) {
        if (arId >= applicationRecords.length) revert IndexOutOfBounds();
        emit ApplicationRecordTagged(arId, tag);
    }

    /**
     * @notice Tags a player (event only, no storage cost)
     * @param playerIdx The index of the player to tag
     * @param tag The tag string to apply
     * @dev Only callable by moderators when not paused
     */
    function tagPlayer(
        uint256 playerIdx,
        string calldata tag
    ) external modOnlyWhenActive {
        if (playerIdx >= players.length) revert IndexOutOfBounds();
        emit PlayerTagged(playerIdx, tag);
    }

    /* ----------------------- BATCH GETTERS (RECORDS) ----------------------- */

    /**
     * @notice Batch get core record data with packed types
     * @param rids Array of record IDs to fetch
     */
    function batchGetRecords(uint256[] calldata rids)
        external
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
        return BatchLib.batchGetRecords(records, rids);
    }

    /**
     * @notice Batch get otherPlayer arrays for records (flat encoding)
     * @param rids Array of record IDs (same IDs used in batchGetRecords)
     */
    function batchGetOtherPlayers(uint256[] calldata rids)
        external
        view
        returns (uint256[] memory allOtherPlayers, uint16[] memory counts)
    {
        return BatchLib.batchGetOtherPlayers(records, rids);
    }

    /* ----------------------- BATCH GETTERS (KEYVALUES) ----------------------- */

    /**
     * @notice Batch get key-value pairs by registry IDs
     * @param kvIds Array of key-value registry IDs
     */
    function batchGetKeyValues(uint64[] calldata kvIds)
        external
        view
        returns (string[] memory keys, string[] memory values)
    {
        return BatchLib.batchGetKeyValues(keyValueRegistry, kvIds);
    }

    /* ----------------------- BATCH GETTERS (PLAYERS) ----------------------- */

    /**
     * @notice Batch get player data by indexes
     * @param playerIdxs Array of player indexes
     */
    function batchGetPlayers(uint256[] calldata playerIdxs)
        external
        view
        returns (
            string[] memory playerIDs,
            address[] memory playerAddresses,
            uint8[] memory playerTypes,
            int64[] memory totalScores
        )
    {
        return BatchLib.batchGetPlayers(players, playerIdxs);
    }

    /* ----------------------------- PAUSE ----------------------------- */

    /**
     * @notice Pauses the contract
     * @dev Only callable by the admin
     */
    function pause() external onlyRole(DEFAULT_ADMIN_ROLE) {
        _pause();
    }

    /**
     * @notice Unpauses the contract
     * @dev Only callable by the admin
     */
    function unpause() external onlyRole(DEFAULT_ADMIN_ROLE) {
        _unpause();
    }

    /* ---------------------------- STORAGE GAP ---------------------------- */
    /**
     * @dev Reserved storage space for future upgrades.
     * This allows adding new state variables without shifting storage slots.
     * Using 50 slots as recommended by OpenZeppelin.
     */
    uint256[50] private __gap;
}
