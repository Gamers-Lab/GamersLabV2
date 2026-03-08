using System.Net;
using BattleRecordsRouter.Config;
using BattleRecordsRouter.Helper;
using BattleRecordsRouter.Helper.Blockchain.Response;
using BattleRecordsRouter.Helper.Swagger;
using BattleRecordsRouter.Services;
using BattleRecordsRouter.Siwe.Authorisation;
using BattleRecordsRouter.Repositories;
using BattleRecordsRouter.Controllers.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3.Accounts;
using System.Reflection;
using System.Text;
using UP.HTTP;
using Azure.Identity;
using BattleRecordsRouter.Middleware;
using BattleRecordsRouter.Services.Database;
using Microsoft.AspNetCore.Mvc;
using Nethereum.JsonRpc.Client;
using Web3 = Nethereum.Web3.Web3; // Required for KeyVault access

namespace BattleRecordsRouter;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // ─────────────────────────────────────────────────────────────
        // 🔐 Load secrets from Azure Key Vault (in production)
        // ─────────────────────────────────────────────────────────────
        if (builder.Environment.IsProduction())
        {
            var keyVaultName = builder.Configuration["KeyVault:Name"];
            if (!string.IsNullOrWhiteSpace(keyVaultName))
            {
                builder.Configuration.AddAzureKeyVault(
                    new Uri($"https://{keyVaultName}.vault.azure.net/"),
                    new DefaultAzureCredential()
                );
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 📋 Logging Setup (+ Activity correlation)
        // ─────────────────────────────────────────────────────────────
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();

        // Activity tracking for AppInsights / correlation
        builder.Logging.Configure(o =>
        {
            o.ActivityTrackingOptions =
                ActivityTrackingOptions.TraceId |
                ActivityTrackingOptions.SpanId |
                ActivityTrackingOptions.ParentId |
                ActivityTrackingOptions.Baggage |
                ActivityTrackingOptions.Tags;
        });

        if (builder.Environment.IsProduction())
        {
            builder.Logging.AddFilter("Microsoft", LogLevel.Information);
            builder.Logging.AddFilter("System", LogLevel.Information);
            builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning); // trim HTTP noise
            builder.Logging.AddFilter("BattleRecordsRouter", LogLevel.Debug);

            builder.Services.AddApplicationInsightsTelemetry(options =>
            {
                options.EnableAdaptiveSampling = false;
                options.EnableQuickPulseMetricStream = true;
            });

            builder.Logging.AddAzureWebAppDiagnostics();
        }
        else
        {
            builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
            builder.Logging.AddFilter("BattleRecordsRouter", LogLevel.Debug);
            builder.Services.AddApplicationInsightsTelemetry(); // local AppInsights
        }

        // ─────────────────────────────────────────────────────────────
        // 📋 API Setup
        // ─────────────────────────────────────────────────────────────

        builder.Services.Configure<ApiBehaviorOptions>(o =>
        {
            o.InvalidModelStateResponseFactory = ctx =>
            {
                var problems = new ValidationProblemDetails(ctx.ModelState)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Instance = ctx.HttpContext.Request.Path
                };

                var http = ctx.HttpContext;
                var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString()
                              ?? http.TraceIdentifier;

                http.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ModelValidation")
                    .LogWarning(
                        "Model validation failed [{TraceId}] {Method} {Path} -> {Status}. Problems: {@Problems}",
                        traceId,
                        http.Request.Method,
                        http.Request.Path.Value,
                        problems.Status,
                        problems);

                // (Optional) include trace id in payload for clients
                problems.Extensions["traceId"] = traceId;

                return new BadRequestObjectResult(problems);
            };
        });


        // ─────────────────────────────────────────────────────────────
        // ⚙️ JSON & MVC Configuration
        // ─────────────────────────────────────────────────────────────
        // Register the convention as a service first
        builder.Services.AddSingleton<OnlyInEnvironmentConvention>();
        // Configure MVC options using IConfigureOptions impl
        builder.Services.ConfigureOptions<MvcOptionsConfigurator>();

        // Single AddControllers: add AuthorizeFilter + JSON options
        // AddControllers: JSON options only (AuthorizeFilter handled by MvcOptionsConfigurator)
        builder.Services.AddControllers()
            .AddJsonOptions(opts =>
            {
                opts.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                opts.JsonSerializerOptions.Converters.Add(new ControllerDeviceToEnum());
            });

        // ─────────────────────────────────────────────────────────────
        // 🔗 Bind App Config Sections
        // ─────────────────────────────────────────────────────────────
        builder.Services.Configure<BlockchainConfig>(builder.Configuration.GetSection("Blockchain"));
        builder.Services.Configure<SupabaseConfigModels>(builder.Configuration.GetSection("Supabase"));
        builder.Services.Configure<AppSettingsModel>(builder.Configuration.GetSection("AppSettings"));

        // ─────────────────────────────────────────────────────────────
        // ✅ Validate Critical Configuration
        // ─────────────────────────────────────────────────────────────
        var blockchainConfig = builder.Configuration.GetSection("Blockchain").Get<BlockchainConfig>();
        if (blockchainConfig == null)
            throw new InvalidOperationException("Blockchain configuration is missing");
        if (string.IsNullOrWhiteSpace(blockchainConfig.NodeUrl))
            throw new InvalidOperationException("Blockchain:NodeUrl is required");
        if (!Uri.TryCreate(blockchainConfig.NodeUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException($"Blockchain:NodeUrl is not a valid URI: {blockchainConfig.NodeUrl}");
        if (string.IsNullOrWhiteSpace(blockchainConfig.AdminAccount?.PrivateKey))
            throw new InvalidOperationException("Blockchain:AdminAccount:PrivateKey is required");
        if (string.IsNullOrWhiteSpace(blockchainConfig.OnChainDataStorageAddress))
            throw new InvalidOperationException("Blockchain:OnChainDataStorageAddress is required");

        var supabaseConfig = builder.Configuration.GetSection("Supabase").Get<SupabaseConfigModels>();
        if (supabaseConfig == null || string.IsNullOrWhiteSpace(supabaseConfig.Url) || string.IsNullOrWhiteSpace(supabaseConfig.Key))
            throw new InvalidOperationException("Supabase configuration (Url and Key) is required");

        // ─────────────────────────────────────────────────────────────
        // 🔐 JWT Authentication & Role Authorization
        // ─────────────────────────────────────────────────────────────
        var appSettings = builder.Configuration.GetRequiredSection("AppSettings").Get<AppSettingsModel>()!;
        if (string.IsNullOrWhiteSpace(appSettings.JWTKey))
            throw new InvalidOperationException("JWTKey is missing from configuration");

        byte[] jwtKey = Encoding.UTF8.GetBytes(appSettings.JWTKey);

        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = builder.Environment.IsProduction();
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = appSettings.ApplicationName,
                    ValidateAudience = true,
                    ValidAudience = appSettings.ApplicationAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(jwtKey),
                    ClockSkew = TimeSpan.Zero
                };
            });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("User", policy => policy.RequireAuthenticatedUser());
            options.AddPolicy("Admin", policy => policy.RequireRole("admin"));
        });

        // ─────────────────────────────────────────────────────────────
        // 🌐 Nethereum HTTP client with sane timeouts & pooling
        // ─────────────────────────────────────────────────────────────
        builder.Services.AddHttpClient("NethereumRpc", client => { client.Timeout = TimeSpan.FromSeconds(120); })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(10),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2), // NEW
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                MaxConnectionsPerServer = 256,
                UseCookies = false
            });

        builder.Services.AddSingleton(provider =>
        {
            var cfg = provider.GetRequiredService<IOptions<BlockchainConfig>>().Value;

            var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("NethereumRpc");
            var rpcClient = new RpcClient(new Uri(cfg.NodeUrl), http);

            // One account (single signer)
            var account = new Account(cfg.AdminAccount.PrivateKey);

            // Chain ID from config (e.g., 50312 for Somnia testnet, 31337 for Hardhat)
            var txManager =
                new AccountSignerTransactionManager(rpcClient, account, new System.Numerics.BigInteger(cfg.ChainId));

            // Optional: keep your existing polling interval / defaults
            txManager.TransactionReceiptService.SetPollingRetryIntervalInMilliseconds(250);

            var web3 = new Web3(rpcClient)
            {
                TransactionManager = txManager
            };
            web3.TransactionManager.UseLegacyAsDefault = true;
            web3.TransactionManager.DefaultGas = new Nethereum.Hex.HexTypes.HexBigInteger(5_000_000);

            return web3;
        });


        // ─────────────────────────────────────────────────────────────
        // 🔧 BlockchainTransactionHelper
        // ─────────────────────────────────────────────────────────────
        builder.Services.AddSingleton<BlockchainTransactionHelper>(provider =>
        {
            var cfg = provider.GetRequiredService<IOptions<BlockchainConfig>>().Value;
            var web3 = provider.GetRequiredService<Web3>();
            var logger = provider.GetRequiredService<ILogger<BlockchainTransactionHelper>>();

            return new BlockchainTransactionHelper(
                web3,
                cfg.OnChainDataStorageAddress,
                new HexBigInteger(20_000_000), // 20M gas for batch operations
                new HexBigInteger(cfg.DefaultGasPrice),
                logger);
        });

        // ─────────────────────────────────────────────────────────────
        // 🧠 Core Services
        // ─────────────────────────────────────────────────────────────
        // Shared Nonce Service for ModeratorAccount (used by SetRecord/BatchSetRecords)
        builder.Services.AddSingleton<SharedNonceService>(provider =>
        {
            var cfg = provider.GetRequiredService<IOptions<BlockchainConfig>>().Value;
            var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("NethereumRpc");
            var rpcClient = new RpcClient(new Uri(cfg.NodeUrl), http);
            var logger = provider.GetRequiredService<ILogger<SharedNonceService>>();

            return new SharedNonceService(cfg.ModeratorAccount.Address, rpcClient, logger);
        });

        // Blockchain Services
        builder.Services.AddSingleton<IGenericBlockchainService, GenericGenericBlockchainService>();
        builder.Services.AddSingleton<IGamersLabStorageService, GamersLabStorageService>();
        builder.Services.AddSingleton<IGamersLabAuthorisationService, GamersLabAuthorisationService>();

        // Logging Services
        builder.Services.AddSingleton<IBlockchainLoggingService, DatabaseLoggingService>(); //wrapper for ErrorLogDBServices & WriteOperationLogger
        builder.Services.AddSingleton<IErrorLogDBServices, ErrorLoggerToDB>();
        builder.Services.AddSingleton<IWriteOperationLogger, WriteOperationLoggerToDB>();

        // Server side player creation
        builder.Services.AddSingleton<IPlayerCredentialDBServices, PlayerCredentialDBServices>(); // player credentials for server side player creation


        // ─────────────────────────────────────────────────────────────
        // 📖 Swagger / OpenAPI
        // ─────────────────────────────────────────────────────────────
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(opt =>
        {
            opt.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Gamers L.A.B. by Uprising Labs",
                Version = "v1.1",
                Description = "API for managing blockchain & player game data"
            });

            var xml = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xml);
            if (File.Exists(xmlPath))
            {
                opt.IncludeXmlComments(xmlPath);
            }
            opt.UseInlineDefinitionsForEnums();
            opt.SchemaFilter<SwaggerFilterJsonStringEnum>();
            opt.EnableAnnotations();
            opt.OperationFilter<SwaggerFilterRolesOperation>();
            opt.OperationFilter<AutoResponseTypeOperationFilter>();

            opt.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "JWT Authorization header using the Bearer scheme",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer"
            });

            opt.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        // ─────────────────────────────────────────────────────────────
        // 📦 Supabase Configuration
        // ─────────────────────────────────────────────────────────────
        builder.Services.AddSingleton(provider =>
        {
            var cfg = provider.GetRequiredService<IOptions<SupabaseConfigModels>>().Value;
            var options = new Supabase.SupabaseOptions
            {
                AutoRefreshToken = true,
                AutoConnectRealtime = true
            };
            return new Supabase.Client(cfg.Url, cfg.Key, options);
        });

        // ─────────────────────────────────────────────────────────────
        // 🔀 HTTP Pipeline
        // ─────────────────────────────────────────────────────────────
        WebApplication app = builder.Build();

        // Configure ContractUtils static properties
        var lockLogger = app.Services.GetRequiredService<ILogger<AccountSendLock>>();
        ContractUtils.SendLock = new AccountSendLock(lockLogger);
        ContractUtils.DefaultSendLockOptions = new AccountLockOptions
        {
            AcquireTimeout = TimeSpan.FromSeconds(60),
            MaxWaiters = 100
        };

        // Swagger UI toggle
        if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("EnableSwaggerInProduction"))
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        // redirect to https in prod
        if (app.Environment.IsProduction())
        {
            app.UseHttpsRedirection();
            app.UseExceptionHandler(errorApp =>
            {
                errorApp.Run(async context =>
                {
                    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                    var exceptionFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();

                    if (exceptionFeature?.Error != null)
                    {
                        logger.LogError(exceptionFeature.Error, "Unhandled exception occurred");
                    }

                    context.Response.StatusCode = 500;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "An internal server error occurred",
                        traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier
                    });
                });
            });
            app.UseHsts();
        }

        // Add request logging in prod
        if (app.Environment.IsProduction())
        {
            app.Use(async (context, next) =>
            {
                var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                var requestId = Guid.NewGuid().ToString();

                logger.LogInformation("Request {Id} {Method} {Path}", requestId, context.Request.Method,
                    context.Request.Path);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    await next();
                    sw.Stop();
                    logger.LogInformation("Completed {Id} with {Code} in {Ms}ms", requestId,
                        context.Response.StatusCode, sw.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    logger.LogError(ex, "Request {Id} failed in {Ms}ms", requestId, sw.ElapsedMilliseconds);
                    throw;
                }
            });
        }

        app.UseAuthentication(); // validates token signature
        app.UseMiddleware<SiweJwtMiddleware>(); // extract JWT claims to HttpContext.Items
        app.UseMiddleware<ErrorLoggingMiddleware>(); // log errors with full context including wallet
        app.UseAuthorization();

        // Add root endpoint
        app.MapGet("/", () => "Online");

        app.MapControllers();

        // Log application URLs on startup
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            var addresses = app.Urls;

            logger.LogInformation("=".PadRight(60, '='));
            logger.LogInformation("Application started successfully!");
            logger.LogInformation("=".PadRight(60, '='));

            foreach (var address in addresses)
            {
                logger.LogInformation("Listening on: {Address}", address);
            }

            if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("EnableSwaggerInProduction"))
            {
                foreach (var address in addresses)
                {
                    var swaggerUrl = $"{address}/swagger";
                    logger.LogInformation("Swagger UI: {SwaggerUrl}", swaggerUrl);
                }
            }

            logger.LogInformation("=".PadRight(60, '='));
        });

        // Final app run with graceful shutdown handling
        try
        {
            app.Run();
        }
        catch (OperationCanceledException)
        {
            // expected on graceful stop
        }
        catch (HostAbortedException)
        {
            // expected during shutdown
        }
        catch (Exception ex)
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogCritical(ex, "Application terminated unexpectedly");
            throw;
        }
    }
}