namespace BattleRecordsRouter.Helper.Swagger;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

/// <summary>
/// Automatically adds ProducesResponseType documentation to Swagger based on endpoint patterns and attributes.
/// This eliminates the need to manually add [ProducesResponseType] attributes to every endpoint.
/// </summary>
public sealed class AutoResponseTypeOperationFilter : IOperationFilter
{
    /// <summary>
    /// Applies automatic response type documentation to the Swagger operation based on endpoint patterns, attributes, and HTTP methods.
    /// </summary>
    /// <param name="operation">The OpenAPI operation to modify.</param>
    /// <param name="context">The operation filter context containing method and API description information.</param>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var methodInfo = context.MethodInfo;
        var methodName = methodInfo.Name;

        // ADDITIVE MODE: We add missing response types even if manual attributes exist
        // This allows developers to specify 200 OK with custom types (e.g., typeof(PlayDataResponse))
        // while the filter automatically adds error response types (400, 401, 403, 404, 409, 500)

        // 1. Always add 500 Internal Server Error (all endpoints can throw exceptions)
        if (!operation.Responses.ContainsKey("500"))
        {
            operation.Responses.Add("500", new OpenApiResponse
            {
                Description = "Internal Server Error - Unexpected error occurred"
            });
        }

        // 2. Add 401 Unauthorized for endpoints with [Authorize] attribute
        var hasAuthorize = methodInfo.GetCustomAttributes(true)
            .Concat(methodInfo.DeclaringType!.GetCustomAttributes(true))
            .OfType<AuthorizeAttribute>()
            .Any();

        if (hasAuthorize && !operation.Responses.ContainsKey("401"))
        {
            operation.Responses.Add("401", new OpenApiResponse
            {
                Description = "Unauthorized - Authentication failed (invalid or missing JWT token)"
            });
        }

        // 3. Add 403 Forbidden for endpoints with role requirements
        var authAttr = methodInfo.GetCustomAttributes(true)
            .Concat(methodInfo.DeclaringType!.GetCustomAttributes(true))
            .OfType<AuthorizeAttribute>()
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(authAttr?.Roles) && !operation.Responses.ContainsKey("403"))
        {
            operation.Responses.Add("403", new OpenApiResponse
            {
                Description = "Forbidden - Insufficient permissions (requires specific role)"
            });
        }

        // 4. Add 400 Bad Request for endpoints with [FromBody] parameters (validation errors)
        var hasBodyParam = context.ApiDescription.ParameterDescriptions
            .Any(p => p.Source.Id == "Body");

        if (hasBodyParam && !operation.Responses.ContainsKey("400"))
        {
            operation.Responses.Add("400", new OpenApiResponse
            {
                Description = "Bad Request - Validation failed (missing or invalid fields)"
            });
        }

        // 5. Add 404 Not Found for GET/lookup endpoints (resource might not exist)
        var isGetOrLookup = methodName.StartsWith("Get", StringComparison.OrdinalIgnoreCase) ||
                            methodName.Contains("Find", StringComparison.OrdinalIgnoreCase) ||
                            methodName.Contains("Lookup", StringComparison.OrdinalIgnoreCase);

        var httpMethod = context.ApiDescription.HttpMethod?.ToUpperInvariant();
        var isHttpGet = httpMethod == "GET";

        if ((isGetOrLookup || isHttpGet) && !operation.Responses.ContainsKey("404"))
        {
            // Only add 404 if the endpoint has path parameters (e.g., /player/{id})
            var hasPathParams = context.ApiDescription.ParameterDescriptions
                .Any(p => p.Source.Id == "Path");

            if (hasPathParams)
            {
                operation.Responses.Add("404", new OpenApiResponse
                {
                    Description = "Not Found - Resource does not exist"
                });
            }
        }

        // 6. Add 409 Conflict for Create/Add endpoints (duplicate resource)
        var isCreateOrAdd = methodName.StartsWith("Create", StringComparison.OrdinalIgnoreCase) ||
                            methodName.StartsWith("Add", StringComparison.OrdinalIgnoreCase) ||
                            methodName.Contains("Register", StringComparison.OrdinalIgnoreCase);

        var isHttpPost = httpMethod == "POST";

        if ((isCreateOrAdd || isHttpPost) && hasBodyParam && !operation.Responses.ContainsKey("409"))
        {
            // Only add 409 for creation endpoints, not for all POST endpoints
            if (isCreateOrAdd)
            {
                operation.Responses.Add("409", new OpenApiResponse
                {
                    Description = "Conflict - Resource already exists (duplicate entry)"
                });
            }
        }

        // 7. Ensure 200 OK is always present (if no other success codes exist)
        var hasSuccessResponse = operation.Responses.Keys.Any(k => k.StartsWith("2"));
        if (!hasSuccessResponse)
        {
            operation.Responses.Add("200", new OpenApiResponse
            {
                Description = "Success"
            });
        }
    }
}

