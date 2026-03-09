// SPDX-License-Identifier: MIT
pragma solidity ^0.8.28;

library DataTypes {
    
    enum Device {
        iOS,
        Android,
        WebGL,
        PC,
        Nintendo,
        Microsoft,
        Sony,
        OtherConsole,
        Other,
        Last
    }

    enum WinLoss {
        Win,
        Loss,
        Forfeit,
        Draw,
        NotSet,
        Fail,
        Last
    }

    enum PlayerType {
        Admin,
        Human,
        NPC,
        Ai,
        Last
    }

    struct BaseData {
        uint40 startTime;
        uint40 endTime;
        uint64 applicationId;
        uint64 playerIndex;
    }

    struct MetaData {
        string[] key;
        string[] values;
    }

    struct LoginSession {
        BaseData baseData;
        MetaData metadata;
        uint256[] matchIds;
        Device device;
    }

    struct Player {
        string playerID;
        address playerAddress;
        PlayerType playerType;
        int256 totalScore;
        string[] verifiedIdKey;
        string[] verifiedIdValue;
        address[] verifiedAddresses;
        MetaData metadata;
    }

    struct MatchSession {
        BaseData baseData;
        MetaData metadata;
        uint256[] playData;
        string level;
    }

    struct PlayData {
        BaseData baseData;
        uint256[] records;
        int64 score;
        int16 matchPosition;
        WinLoss winLoss;
    }

    struct KeyValue {
        string key;
        string value;
    }

    struct Records {
        uint256[] otherPlayer;   // Slot 0: dynamic array pointer
        BaseData baseData;       // Slot 1: 26 bytes (startTime, endTime, applicationId, playerIndex)
        bool isPlayerAccount;    // Slot 1: 1 byte (fits in remaining 6 bytes)
        int64 score;             // Slot 2: 8 bytes
        uint64 playDataId;       // Slot 2: 8 bytes
        uint64 keyValueId;       // Slot 2: 8 bytes
    }

    struct ApplicationRecord {
        uint64 applicationVersion;
        uint40 startDate; 
        uint40 endDate;
        string companyName;
        address[] contractAddresses;
    }

    struct Application {
        string name;
        address owner;
    }

    struct RecordInput {
        uint256 matchId;
        uint256 playerIndex;
        int256 score;
        string key;
        string value;
        uint256[] otherPlayers;
        uint256 startTime;
        bool isPlayerAccount;
    }

}