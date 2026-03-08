# GamersLabV2

`GamersLabV2` is an ASP.NET Core 9 Web API for game-facing authentication, player/session record handling, and blockchain-backed write operations used by the Gamers Lab platform.

The API combines:

- JWT-based authentication for game and admin flows
- blockchain read/write integration through Nethereum
- Supabase-backed operational logging and credential storage
- Swagger/OpenAPI for local exploration

## Repository layout

- `BattleRecordsRouter/` - main API project
- `BattleRecordsRouter/Controllers/` - auth, admin, health, blockchain, and storage endpoints
- `BattleRecordsRouter/RestTest/` - `.http` request samples
- `docs/ERROR_HANDLING_GUIDE.md` - API error handling conventions

## Main API areas

- `api/Auth` - wallet, admin, and token-related auth endpoints
- `api/BlockchainStorage` - players, records, login sessions, match sessions, playdata
- `api/Admin` - admin and role-controlled contract operations
- `api/Blockchain` - low-level blockchain utilities for admin use
- `api/Health` - health and diagnostics endpoints

## Tech stack

- `.NET 9`
- `ASP.NET Core`
- `Nethereum`
- `Supabase`
- `Swagger / Swashbuckle`
- `Azure Key Vault` support for production secret loading

## Prerequisites

- `.NET SDK 9`
- access to an EVM-compatible RPC endpoint
- Supabase project URL and key
- signing keys and app secrets supplied through local config, environment variables, or Key Vault

## Configuration

Production config is represented by the template at `BattleRecordsRouter/appsettings.Production.json`. Do not commit real secrets to this repository.

The app expects configuration for:

- `Supabase:Url`
- `Supabase:Key`
- `AppSettings:JWTKey`
- `AppSettings:AdminPassword`
- `AppSettings:ApplicationPassword`
- `Blockchain:NodeUrl`
- `Blockchain:AdminAccount:PrivateKey`
- `Blockchain:ModeratorAccount:PrivateKey`
- `OnChainDataStorageAddress`

For local development, prefer one of these approaches:

- `BattleRecordsRouter/appsettings.Development.json` kept untracked
- environment variables
- Azure Key Vault-backed configuration

## Running locally

```powershell
dotnet restore
dotnet run --project .\BattleRecordsRouter\BattleRecordsRouter.csproj
```

The project launch settings use:

- `http://localhost:5036`
- `https://localhost:7295`

In development, Swagger UI is available at:

- `https://localhost:7295/swagger`
- `http://localhost:5036/swagger`

## Testing requests

Sample HTTP requests live in `BattleRecordsRouter/RestTest/` and can be run from IDE HTTP clients after supplying local environment values.

## Security

- This public repository is intended to contain templates and code only, not live credentials.
- Secret-bearing local files such as development appsettings and HTTP client env files are ignored.
- Before deploying, rotate any credentials that were ever used in private history.

## Notes

- The current solution name still reflects the original internal project naming.
- Error response conventions are documented in `docs/ERROR_HANDLING_GUIDE.md`.
