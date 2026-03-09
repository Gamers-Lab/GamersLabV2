// SPDX-License-Identifier: MIT
pragma solidity ^0.8.28;

import "./DataTypes.sol";
import "./CoreLib.sol";
import "./LibErrors.sol";

/**
 * @title SessionLib
 * @notice Library for creating / ending login‑sessions and handling metadata.
 *         All reverts now use short custom‑error selectors (no revert‑strings).
 */
library SessionLib {

    /**
     * @notice Creates a new login session for a player
     * @param loginSessions Array of all login sessions
     * @param _playerIndex Index of the player
     * @param _applicationId ID of the application
     * @param _startTime Start time of the session (Unix timestamp)
     * @param _device Device type for this login session
     * @return sessionId ID of the newly created login session
     */
    function loginSessionCreate(
        DataTypes.LoginSession[] storage loginSessions,
        uint256 _playerIndex,
        uint256 _applicationId,
        uint256 _startTime,
        DataTypes.Device _device
    ) internal returns (uint256 sessionId) {
        if (_startTime == 0) revert InvalidTime();

        /* —— Create the new session —— */
        sessionId = loginSessions.length;
        DataTypes.LoginSession storage s = loginSessions.push();

        // Cast to packed types (Solidity 0.8 reverts on overflow)
        s.baseData.startTime = uint40(_startTime);
        s.baseData.applicationId = uint64(_applicationId);
        s.baseData.playerIndex = uint64(_playerIndex);
        s.device = _device;

        return sessionId;
    }


    /**
     * @notice Ends a login session
     * @param loginSessions Array of all login sessions
     * @param _sessionId ID of the session to end
     * @param _endTime End time of the session (Unix timestamp)
     */
    function loginSessionEnd(
        DataTypes.LoginSession[] storage loginSessions,
        uint256 _sessionId,
        uint256 _endTime
    ) internal {
        if (_endTime == 0) revert InvalidTime();
        if (_sessionId >= loginSessions.length)
            revert IndexOutOfBounds();

        DataTypes.LoginSession storage ls = loginSessions[_sessionId];
        if (ls.baseData.endTime != 0) revert SessionAlreadyEnded();
        if (_endTime <= ls.baseData.startTime) revert InvalidTime();

        ls.baseData.endTime = uint40(_endTime);
    }


    // /**
    //  * @notice Adds metadata to a login session
    //  * @dev Uses CoreLib.addMetadata to handle the metadata storage
    //  * @param loginSessions Array of all login sessions
    //  * @param _sessionId ID of the session to add metadata to
    //  * @param _key Metadata key
    //  * @param _value Metadata value
    //  */
    // function setLoginSessionMetadata(
    //     DataTypes.LoginSession[] storage loginSessions,
    //     uint256 _sessionId,
    //     string calldata _key,
    //     string calldata _value
    // ) internal {
    //     CoreLib.addMetadata(loginSessions[_sessionId].metadata, _key, _value);
    // }
}
