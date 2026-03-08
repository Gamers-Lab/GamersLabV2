# Error Handling Guide

## Overview

This guide documents the standardized error handling patterns used across the BattleRecordsRouter API. All controllers follow a consistent approach using the `ApiResponseHelper` class to ensure uniform error responses, proper HTTP status codes, and structured logging.

---

## Core Principles

1. **Consistency** - All error responses follow the same JSON structure
2. **HTTP Semantics** - Use correct HTTP status codes for each scenario
3. **DRY (Don't Repeat Yourself)** - Centralized error handling logic
4. **Logging** - All errors are logged with appropriate context
5. **Type Safety** - Use `IActionResult` for all controller methods

---

## Error Response Format

All error responses follow this standardized JSON structure:

```json
{
  "error": "Error category",
  "details": "Specific error message with context"
}
```

### Examples

**404 Not Found:**
```json
{
  "error": "Player not found",
  "details": "No player exists with identifier: 0x1234..."
}
```

**400 Bad Request:**
```json
{
  "error": "Validation failed",
  "details": "Player address is required"
}
```

**401 Unauthorized:**
```json
{
  "error": "Authentication failed",
  "details": "Invalid password"
}
```

**403 Forbidden:**
```json
{
  "error": "Access denied",
  "details": "Server-side player creation is not allowed"
}
```

**409 Conflict:**
```json
{
  "error": "Resource conflict",
  "details": "Player with this identifier already exists"
}
```

**500 Internal Server Error:**
```json
{
  "error": "Internal server error",
  "details": "Failed to generate authentication token"
}
```

---

## HTTP Status Codes

Use the correct HTTP status code for each scenario:

| Status Code | When to Use | Helper Method | Example Scenarios |
|-------------|-------------|---------------|-------------------|
| **200 OK** | Successful operation | `Ok()` | Data retrieved, transaction submitted |
| **400 Bad Request** | Client input validation errors | `ValidationError()` | Missing required fields, invalid format |
| **401 Unauthorized** | Authentication failed | `AuthenticationError()` | Invalid password, expired JWT, invalid credentials |
| **403 Forbidden** | Authenticated but not authorized | `AuthorizationError()` | Insufficient permissions, feature disabled |
| **404 Not Found** | Resource doesn't exist | `NotFoundError()` | Player not found, record not found |
| **409 Conflict** | Resource already exists | `ConflictError()` | Duplicate player, address already in use |
| **500 Internal Server Error** | Unexpected server errors | `ObjectResult` with 500 | Database errors, transaction failures |

---

## ApiResponseHelper Methods

### 1. HandleSafe - Wrapper for All Operations

**Purpose:** Wraps async operations with try-catch and standardized error handling

**Usage:**
```csharp
[HttpGet("example")]
public async Task<IActionResult> ExampleEndpoint()
{
    return await ApiResponseHelper.HandleSafe(async () =>
    {
        var result = await _service.DoSomething();
        return Ok(result);
    }, _logger, "ExampleEndpoint");
}
```

**Benefits:**
- Automatic exception handling
- Consistent 500 error responses
- Structured error logging
- Reduces boilerplate code

---

### 2. NotFoundError - 404 Not Found

**Purpose:** Returns standardized 404 response when a resource doesn't exist

**Signature:**
```csharp
public static IActionResult NotFoundError(
    string resource,      // Type of resource (e.g., "Player", "Record")
    string identifier,    // Identifier used to search
    ILogger logger,       // Logger instance
    string operationName  // Operation name for logging
)
```

**Usage:**
```csharp
if (playerIndex == uint.MaxValue)
{
    return ApiResponseHelper.NotFoundError("Player", playerId, _logger, "GetPlayerIndex");
}
```

**When to Use:**
- Player not found
- Record not found
- Resource doesn't exist in database
- Smart contract returns "NotFound" error

---

### 5. AuthorizationError - 403 Forbidden

**Purpose:** Returns standardized 403 response when user is authenticated but not authorized

**Signature:**
```csharp
public static IActionResult AuthorizationError(
    string reason,        // Reason for authorization failure
    ILogger logger,       // Logger instance
    string operationName  // Operation name for logging
)
```

**Usage:**
```csharp
if (!access)
{
    return ApiResponseHelper.AuthorizationError("Server-side player creation is not allowed", _logger, "CreatePlayerServerSide");
}
```

**When to Use:**
- Feature disabled by configuration
- Insufficient permissions (not admin)
- User is banned
- Role check failed

**⚠️ Important:** Don't confuse with 401 Unauthorized
- **401** = "Who are you?" (authentication failed)
- **403** = "I know who you are, but you can't do this" (authorization failed)

---

### 6. ConflictError - 409 Conflict

**Purpose:** Returns standardized 409 response when a resource already exists

**Signature:**
```csharp
public static IActionResult ConflictError(
    string message,       // Conflict error message
    ILogger logger,       // Logger instance
    string operationName  // Operation name for logging
)
```

**Usage:**
```csharp
if (playerIndex != uint.MaxValue)
{
    return ApiResponseHelper.ConflictError("Player with this identifier already exists", _logger, "CreatePlayerServerSide");
}
```

**When to Use:**
- Duplicate player creation
- Address already in use
- Username already taken
- Resource already exists

---

### 7. TransactionOrError - Transaction Success/Failure

**Purpose:** Returns transaction hash on success or 500 error on failure

**Signature:**
```csharp
public static IActionResult TransactionOrError(
    string txHash,        // Transaction hash from blockchain
    ILogger logger,       // Logger instance
    string operationName  // Operation name for logging
)
```

**Usage:**
```csharp
return await ApiResponseHelper.HandleSafe(async () =>
{
    var txHash = await _storageService.CreatePlayer(HttpContext, username, playerType, address);
    return ApiResponseHelper.TransactionOrError(txHash, _logger, "CreatePlayer");
}, _logger, "CreatePlayer");
```

**When to Use:**
- Blockchain transaction submission
- Smart contract write operations

---

### 8. ViewOrError - View Data Success/Failure

**Purpose:** Returns payload on success or 500 error if payload is null

**Signature:**
```csharp
public static IActionResult ViewOrError<T>(
    T? payload,           // Data payload to return
    ILogger logger,       // Logger instance
    string operationName  // Operation name for logging
)
```

**Usage:**
```csharp
return await ApiResponseHelper.HandleSafe(async () =>
{
    var result = await _storageService.GetAllActivePlayers();
    return ApiResponseHelper.ViewOrError(result, _logger, "GetAllActivePlayers");
}, _logger, "GetAllActivePlayers");
```

**When to Use:**
- Smart contract read operations
- Database queries that might return null

---

## Common Patterns

### Pattern 1: Simple Endpoint with HandleSafe

```csharp
[HttpGet("example")]
public async Task<IActionResult> ExampleEndpoint()
{
    return await ApiResponseHelper.HandleSafe(async () =>
    {
        var result = await _service.DoSomething();
        return Ok(result);
    }, _logger, "ExampleEndpoint");
}
```

---

### Pattern 2: Validation Before Processing

```csharp
[HttpPost("example")]
public async Task<IActionResult> ExampleEndpoint([FromBody] ExampleRequest request)
{
    // Validate input first
    if (string.IsNullOrWhiteSpace(request.RequiredField))
    {
        return ApiResponseHelper.ValidationError("Required field is missing", _logger, "ExampleEndpoint");
    }

    // Then wrap the operation
    return await ApiResponseHelper.HandleSafe(async () =>
    {
        var result = await _service.DoSomething(request);
        return Ok(result);
    }, _logger, "ExampleEndpoint");
}
```

---

### Pattern 3: Authentication Check

```csharp
[HttpPost("example")]
public async Task<IActionResult> ExampleEndpoint([FromBody] ExampleRequest request)
{
    // Check authentication
    bool auth = await _auth.AuthenticatePassword(request.Password);
    if (!auth)
    {
        return ApiResponseHelper.AuthenticationError("Invalid password", _logger, "ExampleEndpoint");
    }

    // Continue with operation
    return await ApiResponseHelper.HandleSafe(async () =>
    {
        var result = await _service.DoSomething(request);
        return Ok(result);
    }, _logger, "ExampleEndpoint");
}
```

---

### Pattern 4: Resource Existence Check

```csharp
[HttpGet("player/{playerId}")]
public async Task<IActionResult> GetPlayer(string playerId)
{
    return await ApiResponseHelper.HandleSafe(async () =>
    {
        var player = await _service.GetPlayer(playerId);

        // Check if resource exists
        if (player == null)
        {
            return ApiResponseHelper.NotFoundError("Player", playerId, _logger, "GetPlayer");
        }

        return Ok(player);
    }, _logger, "GetPlayer");
}
```

---

### Pattern 5: Conflict Detection

```csharp
[HttpPost("player")]
public async Task<IActionResult> CreatePlayer([FromBody] CreatePlayerRequest request)
{
    return await ApiResponseHelper.HandleSafe(async () =>
    {
        // Check if player already exists
        var existingPlayer = await _service.GetPlayerByAddress(request.Address);
        if (existingPlayer != null)
        {
            return ApiResponseHelper.ConflictError("Player with this address already exists", _logger, "CreatePlayer");
        }

        var result = await _service.CreatePlayer(request);
        return Ok(result);
    }, _logger, "CreatePlayer");
}
```

---

### Pattern 6: Smart Contract Error Handling

```csharp
[HttpGet("player/{playerId}")]
public async Task<IActionResult> GetPlayer(string playerId)
{
    return await ApiResponseHelper.HandleSafe(async () =>
    {
        try
        {
            var player = await _storageService.GetPlayer(playerId);
            return Ok(player);
        }
        catch (SmartContractRevertException ex) when (ex.Message.Contains("NotFound"))
        {
            return ApiResponseHelper.NotFoundError("Player", playerId, _logger, "GetPlayer");
        }
        catch (SmartContractRevertException ex) when (ex.Message.Contains("IndexOutOfBounds"))
        {
            return ApiResponseHelper.NotFoundError("Player", playerId, _logger, "GetPlayer");
        }
        // Other exceptions are caught by HandleSafe and return 500
    }, _logger, "GetPlayer");
}
```

---

## Decision Tree

Use this decision tree to choose the correct error response:

```
Is the operation successful?
├─ YES → Return Ok(result)
└─ NO → What went wrong?
    ├─ Missing/invalid input → ValidationError (400)
    ├─ Authentication failed → AuthenticationError (401)
    ├─ Not authorized → AuthorizationError (403)
    ├─ Resource not found → NotFoundError (404)
    ├─ Resource already exists → ConflictError (409)
    └─ Unexpected error → Let HandleSafe return 500
```

---

## Best Practices

### ✅ DO

1. **Always use `HandleSafe`** for wrapping async operations
2. **Use specific error helpers** instead of generic `BadRequest()` or `Unauthorized()`
3. **Provide context** in error messages (include identifiers, resource types)
4. **Log before returning errors** (helpers do this automatically)
5. **Use consistent operation names** that match the method name
6. **Return early** for validation and authentication checks
7. **Use `IActionResult`** as return type for all controller methods

### ❌ DON'T

1. **Don't use manual try-catch** blocks (use `HandleSafe` instead)
2. **Don't return plain strings** as error messages
3. **Don't use 403 for "not found"** scenarios (use 404)
4. **Don't use 400 for authentication failures** (use 401)
5. **Don't create custom error formats** (use the helpers)
6. **Don't skip logging** (helpers log automatically)
7. **Don't use `ActionResult<T>`** when you need to return different types

---


## Migration Guide

### Converting Existing Code

**Before (Manual try-catch):**
```csharp
[HttpGet("example")]
public async Task<ActionResult<string>> GetExample()
{
    try
    {
        var result = await _service.DoSomething();
        return Ok(result);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error in GetExample");
        return StatusCode(500, $"Error: {ex.Message}");
    }
}
```

**After (Using HandleSafe):**
```csharp
[HttpGet("example")]
public async Task<IActionResult> GetExample()
{
    return await ApiResponseHelper.HandleSafe(async () =>
    {
        var result = await _service.DoSomething();
        return Ok(result);
    }, _logger, "GetExample");
}
```

---

**Before (Plain error messages):**
```csharp
if (player == null)
{
    return NotFound("Player not found");
}
```

**After (Using helper):**
```csharp
if (player == null)
{
    return ApiResponseHelper.NotFoundError("Player", playerId, _logger, "GetPlayer");
}
```

---

**Before (Incorrect status code):**
```csharp
if (player == null)
{
    return StatusCode(403, "Player not found");  // ❌ Wrong status code
}
```

**After (Correct status code):**
```csharp
if (player == null)
{
    return ApiResponseHelper.NotFoundError("Player", playerId, _logger, "GetPlayer");  // ✅ Returns 404
}
```

---

## Testing Error Responses

### Example Test Cases

```csharp
[Fact]
public async Task GetPlayer_WhenPlayerNotFound_Returns404()
{
    // Arrange
    var playerId = "nonexistent";

    // Act
    var result = await _controller.GetPlayer(playerId);

    // Assert
    var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
    Assert.Equal(404, notFoundResult.StatusCode);

    var errorResponse = Assert.IsType<dynamic>(notFoundResult.Value);
    Assert.Equal("Player not found", errorResponse.error);
    Assert.Contains(playerId, errorResponse.details);
}

[Fact]
public async Task CreatePlayer_WhenMissingRequiredField_Returns400()
{
    // Arrange
    var request = new CreatePlayerRequest { Address = null };

    // Act
    var result = await _controller.CreatePlayer(request);

    // Assert
    var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
    Assert.Equal(400, badRequestResult.StatusCode);

    var errorResponse = Assert.IsType<dynamic>(badRequestResult.Value);
    Assert.Equal("Validation failed", errorResponse.error);
}

[Fact]
public async Task Login_WhenInvalidPassword_Returns401()
{
    // Arrange
    var request = new LoginRequest { Password = "wrong" };

    // Act
    var result = await _controller.Login(request);

    // Assert
    var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
    Assert.Equal(401, unauthorizedResult.StatusCode);

    var errorResponse = Assert.IsType<dynamic>(unauthorizedResult.Value);
    Assert.Equal("Authentication failed", errorResponse.error);
}

[Fact]
public async Task CreatePlayer_WhenPlayerExists_Returns409()
{
    // Arrange
    var request = new CreatePlayerRequest { Address = "0x123..." };

    // Act
    var result = await _controller.CreatePlayer(request);

    // Assert
    var conflictResult = Assert.IsType<ConflictObjectResult>(result);
    Assert.Equal(409, conflictResult.StatusCode);

    var errorResponse = Assert.IsType<dynamic>(conflictResult.Value);
    Assert.Equal("Resource conflict", errorResponse.error);
}
```

---

## Swagger Documentation

All error responses should be documented in Swagger using `ProducesResponseType`:

```csharp
[HttpGet("player/{playerId}")]
[ProducesResponseType(typeof(PlayerResponse), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
[ProducesResponseType(StatusCodes.Status500InternalServerError)]
public async Task<IActionResult> GetPlayer(string playerId)
{
    // Implementation
}
```

**For endpoints with multiple error scenarios:**
```csharp
[HttpPost("player")]
[ProducesResponseType(typeof(CreatePlayerResponse), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]      // Validation errors
[ProducesResponseType(StatusCodes.Status401Unauthorized)]    // Authentication failed
[ProducesResponseType(StatusCodes.Status409Conflict)]        // Player already exists
[ProducesResponseType(StatusCodes.Status500InternalServerError)]
public async Task<IActionResult> CreatePlayer([FromBody] CreatePlayerRequest request)
{
    // Implementation
}
```

---

## Quick Reference Table

| Scenario | Status Code | Helper Method | Example |
|----------|-------------|---------------|---------|
| Success | 200 | `Ok()` | `return Ok(result);` |
| Missing field | 400 | `ValidationError()` | `ValidationError("Field required", _logger, "Method")` |
| Invalid format | 400 | `ValidationError()` | `ValidationError("Invalid format", _logger, "Method")` |
| Wrong password | 401 | `AuthenticationError()` | `AuthenticationError("Invalid password", _logger, "Method")` |
| Expired token | 401 | `AuthenticationError()` | `AuthenticationError("Token expired", _logger, "Method")` |
| Not admin | 403 | `AuthorizationError()` | `AuthorizationError("Admin required", _logger, "Method")` |
| Feature disabled | 403 | `AuthorizationError()` | `AuthorizationError("Feature disabled", _logger, "Method")` |
| Player not found | 404 | `NotFoundError()` | `NotFoundError("Player", id, _logger, "Method")` |
| Record not found | 404 | `NotFoundError()` | `NotFoundError("Record", id, _logger, "Method")` |
| Duplicate player | 409 | `ConflictError()` | `ConflictError("Player exists", _logger, "Method")` |
| Address in use | 409 | `ConflictError()` | `ConflictError("Address in use", _logger, "Method")` |
| Unexpected error | 500 | `HandleSafe` catches | Automatic via `HandleSafe` wrapper |

---

## Summary

- **Use `ApiResponseHelper.HandleSafe`** for all async operations
- **Use specific error helpers** for common scenarios (404, 400, 401, 403, 409)
- **Follow HTTP semantics** - use the correct status code for each scenario
- **Maintain consistency** - all errors follow the same JSON structure
- **Log everything** - helpers provide automatic structured logging
- **Keep it DRY** - don't repeat error handling logic

For questions or clarifications, refer to the `ApiResponseHelper.cs` implementation or consult the development team.

---

**Last Updated:** 2025-12-01
**Version:** 1.0