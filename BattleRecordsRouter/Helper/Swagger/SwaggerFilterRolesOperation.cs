namespace BattleRecordsRouter.Helper;

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

/// <summary>
/// Swagger operation filter that adds authentication and role requirement information to endpoint descriptions.
/// </summary>
public sealed class SwaggerFilterRolesOperation : IOperationFilter
{
    /// <summary>
    /// Applies authentication and role requirement notes to the Swagger operation description.
    /// </summary>
    /// <param name="op">The OpenAPI operation to modify.</param>
    /// <param name="ctx">The operation filter context containing method information.</param>
    public void Apply(OpenApiOperation op, OperationFilterContext ctx)
    {
        // 1) Find the [Authorize] attribute (if any)
        var authAttr = ctx.MethodInfo.GetCustomAttributes(true)
            .Concat(ctx.MethodInfo.DeclaringType!.GetCustomAttributes(true))
            .OfType<AuthorizeAttribute>()
            .FirstOrDefault();

        if (authAttr is null) return;            // endpoint is anonymous → nothing to do

        // 2) Determine roles (may be empty)
        var roles = (authAttr.Roles ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(r => r.Trim())
            .ToArray();

        // 3) Craft a note
        string note = roles.Length == 0
            ? "**Requires authentication** (any valid JWT)."
            : $"**Required roles:** `{string.Join("`, `", roles)}`.";

        // 4) Append the note to the existing description
        op.Description = string.IsNullOrWhiteSpace(op.Description)
            ? note
            : $"{op.Description}<br/><br/>{note}";
    }
}
