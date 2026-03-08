using Supabase;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace BattleRecordsRouter.Models;

// 3.  public.player_credentials
//
[Table("player_credentials")]
public class PlayerCredentialRecord : BaseModel
{
    // PRIMARY KEY
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Column("address")]
    public string Address { get; set; } = string.Empty;

    [Column("private_key")]
    public string PrivateKey { get; set; } = string.Empty;

    [Column("immutable_player_identifier")]
    public string ImmutablePlayerIdentifier { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

//
// 4.  public.error_logs
//
[Table("error_logs")]
public class ErrorLogRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Column("operation_name")]
    public string OperationName { get; set; } = string.Empty;

    [Column("error_type")]
    public string ErrorType { get; set; } = string.Empty;

    [Column("error_message")]
    public string ErrorMessage { get; set; } = string.Empty;

    [Column("stack_trace")]
    public string? StackTrace { get; set; }

    [Column("contract_address")]
    public string? ContractAddress { get; set; }

    [Column("wallet_address")]
    public string? WalletAddress { get; set; }

    [Column("http_status_code")]
    public int? HttpStatusCode { get; set; }

    [Column("request_path")]
    public string? RequestPath { get; set; }

    [Column("user_agent")]
    public string? UserAgent { get; set; }

    [Column("client_ip")]
    public string? ClientIp { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

//
// 5.  public.write_operations
//
[Table("write_operations")]
public class WriteOperationRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Column("operation_name")]
    public string OperationName { get; set; } = string.Empty;

    [Column("contract_address")]
    public string ContractAddress { get; set; } = string.Empty;

    [Column("wallet_address")]
    public string? WalletAddress { get; set; }

    [Column("function_name")]
    public string FunctionName { get; set; } = string.Empty;

    [Column("function_parameters")]
    public string? FunctionParameters { get; set; }

    [Column("payload")]
    public string? Payload { get; set; }

    [Column("gas_limit")]
    public long? GasLimit { get; set; }

    [Column("max_fee_per_gas")]
    public long? MaxFeePerGas { get; set; }

    [Column("max_priority_fee_per_gas")]
    public long? MaxPriorityFeePerGas { get; set; }

    [Column("status")]
    public string Status { get; set; } = "pending";

    [Column("attempt_number")]
    public int AttemptNumber { get; set; } = 1;

    [Column("is_retry")]
    public bool IsRetry { get; set; } = false;

    [Column("transaction_hash")]
    public string? TransactionHash { get; set; }

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("send_duration_ms")]
    public long? SendDurationMs { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("player_index")]
    public uint? PlayerIndex { get; set; }
}