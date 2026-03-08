using Nethereum.ABI.FunctionEncoding.Attributes;

namespace UP.HTTP
{
    // Auto-mapping Solidity custom errors

    [Error("IndexOutOfBounds")]
    public class IndexOutOfBounds : IErrorDTO
    {
    }

    [Error("InvalidTime")]
    public class InvalidTime : IErrorDTO
    {
    }

    [Error("MatchAlreadyEnded")]
    public class MatchAlreadyEnded : IErrorDTO
    {
    }

    [Error("MatchFinished")]
    public class MatchFinished : IErrorDTO
    {
    }

    [Error("EmptyArray")]
    public class EmptyArray : IErrorDTO
    {
    }

    [Error("TooManyPlayers")]
    public class TooManyPlayers : IErrorDTO
    {
    }

    [Error("DuplicatePlayer")]
    public class DuplicatePlayer : IErrorDTO
    {
    }

    [Error("MatchFull")]
    public class MatchFull : IErrorDTO
    {
    }

    [Error("PlayerAlreadyInMatch")]
    public class PlayerAlreadyInMatch : IErrorDTO
    {
    }

    [Error("ZeroAddress")]
    public class ZeroAddress : IErrorDTO
    {
    }

    [Error("EmptyString")]
    public class EmptyString : IErrorDTO
    {
    }

    [Error("AlreadyExists")]
    public class AlreadyExists : IErrorDTO
    {
    }

    [Error("TooManyItems")]
    public class TooManyItems : IErrorDTO
    {
    }

    [Error("NotFound")]
    public class NotFound : IErrorDTO
    {
    }

    [Error("Invalid")]
    public class Invalid : IErrorDTO
    {
    }
    
}