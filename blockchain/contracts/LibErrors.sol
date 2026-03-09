// SPDX-License-Identifier: MIT
pragma solidity ^0.8.28;

/**
 * @notice Error thrown when an index is out of the valid range
 */
error IndexOutOfBounds();

/**
 * @notice Error thrown when an invalid time value is provided
 */
error InvalidTime();

/**
 * @notice Error thrown when attempting to modify a match that has already ended
 */
error MatchAlreadyEnded();

/**
 * @notice Error thrown when attempting to end a login session that has already ended
 */
error SessionAlreadyEnded();

/**
 * @notice Error thrown when attempting to modify a match that is finished
 */
error MatchFinished();

/**
 * @notice Error thrown when an empty array is provided where a non-empty one is required
 */
error EmptyArray();

/**
 * @notice Error thrown when too many players are added to a match
 */
error TooManyPlayers();

/**
 * @notice Error thrown when attempting to add a duplicate player to a match
 */
error DuplicatePlayer();

/**
 * @notice Error thrown when a match has reached its maximum capacity
 */
error MatchFull();

/**
 * @notice Error thrown when attempting to add a player who is already in a match
 */
error PlayerAlreadyInMatch();

/**
 * @notice Error thrown when a zero address is provided where a non-zero address is required
 */
error ZeroAddress();          

/**
 * @notice Error thrown when an empty string is provided where a non-empty string is required
 */
error EmptyString(); 

/**
 * @notice Error thrown when attempting to add an item that already exists
 */
error AlreadyExists();

/**
 * @notice Error thrown when a collection has reached its maximum capacity
 */
error TooManyItems();

/**
 * @notice Error thrown when a requested item cannot be found
 */
error NotFound();

/**
 * @notice Error thrown when an invalid value or parameter is provided
 */
error Invalid();

/**
 * @notice Error thrown when attempting to set play data that has already been finalized
 */
error PlayDataAlreadySet();

/**
 * @notice Error thrown when a value exceeds the allowed range
 */
error ValueOutOfRange();
