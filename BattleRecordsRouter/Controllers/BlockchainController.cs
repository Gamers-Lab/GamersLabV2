using BattleRecordsRouter.Controllers.Settings;
using BattleRecordsRouter.Helper.Blockchain.Response;
using BattleRecordsRouter.Models;
using BattleRecordsRouter.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BattleRecordsRouter.Controllers;

[ApiController]
[OnlyInEnvironment("EnableBlockchainEndpoints")]
[Route("api/[controller]"), Authorize(Roles = "admin")]
public class BlockchainController : ControllerBase
{
    private readonly IGenericBlockchainService _genericBlockchainService;
    private readonly ILogger<BlockchainController> _logger;

    public BlockchainController(IGenericBlockchainService genericBlockchainService,
        ILogger<BlockchainController> logger)
    {
        _genericBlockchainService = genericBlockchainService;
        _logger = logger;
    }

    [HttpGet("balance/{address}"), Authorize(Roles = "admin")]
    public async Task<IActionResult> GetBalance(string address)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var balance = await _genericBlockchainService.GetAccountBalance(address);
            return Ok(balance);
        }, _logger, "GetBalance");
    }

    [HttpGet("block/current"), Authorize(Roles = "admin")]
    public async Task<IActionResult> GetCurrentBlockNumber()
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var blockNumber = await _genericBlockchainService.GetBlockNumber();
            return Ok(blockNumber.ToString());
        }, _logger, "GetCurrentBlockNumber");
    }

    [HttpPost("contract/read"), Authorize(Roles = "admin")]
    public async Task<IActionResult> ReadContract([FromBody] ContractFunctionCallModel model)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var result = await _genericBlockchainService.CallContractFunction(
                model.ContractAddress,
                model.Abi,
                model.FunctionName,
                model.Parameters?.ToArray() ?? Array.Empty<object>());

            return Ok(result);
        }, _logger, "ReadContract");
    }

    [HttpPost("contract/write"), Authorize(Roles = "admin")]
    public async Task<IActionResult> WriteToContract([FromBody] ContractFunctionCallModel model)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var transactionHash = await _genericBlockchainService.SendTransactionToContract(
                model.ContractAddress,
                model.Abi,
                model.FunctionName,
                model.Parameters?.ToArray() ?? Array.Empty<object>());

            return Ok(new { TransactionHash = transactionHash });
        }, _logger, "WriteToContract");
    }

    [HttpPost("contract/data"), Authorize(Roles = "admin")]
    public async Task<IActionResult> GetContractData([FromBody] ContractInfoModel model)
    {
        return await ApiResponseHelper.HandleSafe(async () =>
        {
            var data = await _genericBlockchainService.GetContractData(model.ContractAddress, model.Abi);
            return Ok(data);
        }, _logger, "GetContractData");
    }
}