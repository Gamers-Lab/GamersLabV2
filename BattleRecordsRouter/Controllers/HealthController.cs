using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using BattleRecordsRouter.Services;
using Microsoft.AspNetCore.Authorization;
using BattleRecordsRouter.Helper.Blockchain.Response;

namespace BattleRecordsRouter.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class HealthController : ControllerBase
    {
        private readonly IConfiguration _cfg;
        private readonly ILogger<HealthController> _log;
        private readonly IGamersLabStorageService _storageService;

        public HealthController(
            IConfiguration cfg, 
            ILogger<HealthController> log,
            IGamersLabStorageService storageService)
        {
            _cfg = cfg;
            _log = log;
            _storageService = storageService;
        }

        [HttpGet("check"), AllowAnonymous]
        [ProducesResponseType(typeof(HealthStatus), 200)]
        public async Task<IActionResult> Ping()
        {
            return await ApiResponseHelper.HandleSafe(async () =>
            {
                var check = new HealthStatus
                {
                    Status = "healthy",
                    Timestamp = DateTime.UtcNow,
                    JWTKeyPresent = !string.IsNullOrWhiteSpace(_cfg["AppSettings:JWTKey"]),
                    SequenceProjectId = _cfg["AppSettings:SequenceProjectId"],
                    SupabaseUrl = _cfg["Supabase:Url"],
                    BlockchainNode = _cfg["Blockchain:NodeUrl"],
                    ApplicationName = _cfg["AppSettings:ApplicationName"]
                };

                return Ok(check);
            }, _log, "HealthCheck");
        }

        /// <summary>
        /// Gets diagnostic information about the blockchain connection and contract.
        /// </summary>
        /// <returns>A dictionary with diagnostic information.</returns>
        [HttpGet("diagnostics"), AllowAnonymous]
        [ProducesResponseType(typeof(Dictionary<string, string>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetDiagnosticInfo()
        {
            _log.LogInformation("[API] GetDiagnosticInfo called");

            return await ApiResponseHelper.HandleSafe(async () =>
            {
                var info = await _storageService.GetDiagnosticInfo();
                return Ok(info);
            }, _log, "GetDiagnosticInfo");
        }

        /// <summary>
        /// Gets comprehensive system diagnostics including blockchain data.
        /// </summary>
        /// <returns>System diagnostics with player counts, match counts, and transaction data.</returns>
        [HttpGet("system"), AllowAnonymous]
        [ProducesResponseType(typeof(Dictionary<string, object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSystemDiagnostics()
        {
            _log.LogInformation("[API] GetSystemDiagnostics called");

            return await ApiResponseHelper.HandleSafe(async () =>
            {
                var diagnostics = await _storageService.GetSystemDiagnostics();
                return Ok(diagnostics);
            }, _log, "GetSystemDiagnostics");
        }
        
        
        public sealed class HealthStatus
        {
            public string Status { get; set; } = "healthy";
            public DateTime Timestamp { get; set; }
            public bool JWTKeyPresent { get; set; }
            public string? SequenceProjectId { get; set; }
            public string? SupabaseUrl { get; set; }
            public string? BlockchainNode { get; set; }
            public string? ApplicationName { get; set; }
        }
    }
}