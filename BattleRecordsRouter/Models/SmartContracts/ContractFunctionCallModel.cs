namespace BattleRecordsRouter.Models;

public class ContractFunctionCallModel
{
    public required string ContractAddress { get; set; }
    public required string Abi { get; set; }
    public required string FunctionName { get; set; }
    public List<object>? Parameters { get; set; }
}
