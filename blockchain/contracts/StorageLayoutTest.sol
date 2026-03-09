// SPDX-License-Identifier: MIT
pragma solidity ^0.8.28;

import "./DataTypes.sol";

/**
 * @title StorageLayoutTest
 * @notice Test contract to verify struct packing in Records
 * @dev Deploy this on localhost and call the test functions
 */
contract StorageLayoutTest {
    // Store a single record for testing
    DataTypes.Records private testRecord;

    /**
     * @notice Writes all fields to test storage (new layout with BaseData)
     */
    function setTestRecord(
        uint40 _startTime,
        uint40 _endTime,
        uint64 _applicationId,
        uint64 _playerIndex,
        int64 _score,
        uint64 _playDataId,
        uint64 _keyValueId
    ) external {
        // BaseData fields (Slot 1)
        testRecord.baseData.startTime = _startTime;
        testRecord.baseData.endTime = _endTime;
        testRecord.baseData.applicationId = _applicationId;
        testRecord.baseData.playerIndex = _playerIndex;
        // Packed fields (Slot 2)
        testRecord.score = _score;
        testRecord.playDataId = _playDataId;
        testRecord.keyValueId = _keyValueId;
    }

    /**
     * @notice Reads raw storage slot to verify packing
     * @param slotOffset Offset from base slot (0 = first slot, 1 = second slot, etc)
     * @return value The raw 32-byte value at that slot
     */
    function readStorageSlot(uint256 slotOffset) external view returns (bytes32 value) {
        // testRecord is at storage slot 0 (first state variable)
        // For a struct, fields are stored starting at that slot
        uint256 slot = slotOffset;
        assembly {
            value := sload(slot)
        }
    }

    /**
     * @notice Returns the expected layout information
     * @dev Records now uses 3 slots: array pointer, BaseData, packed fields
     */
    function getExpectedLayout() external pure returns (string memory) {
        return string(abi.encodePacked(
            "Slot 0: otherPlayer array pointer (32 bytes)\n",
            "Slot 1: BaseData - startTime(5) + endTime(5) + applicationId(8) + playerIndex(8) = 26 bytes\n",
            "Slot 2: score(8) + playDataId(8) + keyValueId(8) = 24 bytes packed"
        ));
    }

    /**
     * @notice Measures gas for writing all fields
     * @dev Now uses 2 slots for data (BaseData + packed fields)
     */
    function measureWriteGas() external returns (uint256 gasUsed) {
        uint256 startGas = gasleft();

        // Write BaseData fields (Slot 1)
        testRecord.baseData.startTime = uint40(block.timestamp);
        testRecord.baseData.endTime = 0;
        testRecord.baseData.applicationId = 1;
        testRecord.baseData.playerIndex = 42;
        // Write packed fields (Slot 2)
        testRecord.score = 12345;
        testRecord.playDataId = 999;
        testRecord.keyValueId = 888;

        gasUsed = startGas - gasleft();
    }

    /**
     * @notice Returns the record values to verify reads work
     */
    function getTestRecord() external view returns (
        uint40 startTime,
        uint40 endTime,
        uint64 applicationId,
        uint64 playerIndex,
        int64 score,
        uint64 playDataId,
        uint64 keyValueId
    ) {
        return (
            testRecord.baseData.startTime,
            testRecord.baseData.endTime,
            testRecord.baseData.applicationId,
            testRecord.baseData.playerIndex,
            testRecord.score,
            testRecord.playDataId,
            testRecord.keyValueId
        );
    }

    // ======================== BaseData Packing Tests ========================

    DataTypes.BaseData private testBaseData;

    /**
     * @notice Sets all BaseData fields
     */
    function setTestBaseData(
        uint40 _startTime,
        uint40 _endTime,
        uint64 _applicationId,
        uint64 _playerIndex
    ) external {
        testBaseData.startTime = _startTime;
        testBaseData.endTime = _endTime;
        testBaseData.applicationId = _applicationId;
        testBaseData.playerIndex = _playerIndex;
    }

    /**
     * @notice Reads raw storage slot for BaseData (starts at slot 1 after Records)
     */
    function readBaseDataSlot(uint256 slotOffset) external view returns (bytes32 value) {
        // testBaseData is at storage slot 1 (after testRecord which uses slots 0-1)
        // Actually need to calculate based on Records size (2 slots: array ptr + packed)
        uint256 baseSlot = 2; // testRecord uses slots 0-1
        uint256 slot = baseSlot + slotOffset;
        assembly {
            value := sload(slot)
        }
    }

    /**
     * @notice Returns BaseData values
     */
    function getTestBaseData() external view returns (
        uint40 startTime,
        uint40 endTime,
        uint64 applicationId,
        uint64 playerIndex
    ) {
        return (testBaseData.startTime, testBaseData.endTime, testBaseData.applicationId, testBaseData.playerIndex);
    }

    /**
     * @notice Expected layout for BaseData
     */
    function getBaseDataExpectedLayout() external pure returns (string memory) {
        return "Slot 0: startTime(5) + endTime(5) + applicationId(8) + playerIndex(8) = 26 bytes packed";
    }

    // ======================== PlayData Packing Tests ========================

    DataTypes.PlayData private testPlayData;

    /**
     * @notice Sets all PlayData fields
     */
    function setTestPlayData(
        uint40 _startTime,
        uint40 _endTime,
        uint64 _applicationId,
        uint64 _playerIndex,
        int64 _score,
        int16 _matchPosition,
        DataTypes.WinLoss _winLoss
    ) external {
        testPlayData.baseData.startTime = _startTime;
        testPlayData.baseData.endTime = _endTime;
        testPlayData.baseData.applicationId = _applicationId;
        testPlayData.baseData.playerIndex = _playerIndex;
        testPlayData.score = _score;
        testPlayData.matchPosition = _matchPosition;
        testPlayData.winLoss = _winLoss;
    }

    /**
     * @notice Reads raw storage slot for PlayData
     */
    function readPlayDataSlot(uint256 slotOffset) external view returns (bytes32 value) {
        // testPlayData is at storage slot 3 (after Records=2, BaseData=1)
        uint256 baseSlot = 3;
        uint256 slot = baseSlot + slotOffset;
        assembly {
            value := sload(slot)
        }
    }

    /**
     * @notice Returns PlayData values
     */
    function getTestPlayData() external view returns (
        uint40 startTime,
        uint40 endTime,
        uint64 applicationId,
        uint64 playerIndex,
        int64 score,
        int16 matchPosition,
        DataTypes.WinLoss winLoss
    ) {
        return (
            testPlayData.baseData.startTime,
            testPlayData.baseData.endTime,
            testPlayData.baseData.applicationId,
            testPlayData.baseData.playerIndex,
            testPlayData.score,
            testPlayData.matchPosition,
            testPlayData.winLoss
        );
    }

    /**
     * @notice Expected layout for PlayData
     */
    function getPlayDataExpectedLayout() external pure returns (string memory) {
        return string(abi.encodePacked(
            "Slot 0: BaseData packed (26 bytes) - includes playerIndex\n",
            "Slot 1: records array pointer (32 bytes)\n",
            "Slot 2: score(8) + matchPosition(2) + winLoss(1) = 11 bytes packed\n",
            "Total: 3 slots (was 8 slots before packing)"
        ));
    }
}

