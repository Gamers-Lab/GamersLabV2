using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;
using System.Text.Json.Serialization;

namespace BattleRecordsRouter.Helper;

/// <summary>
/// Swagger schema filter that converts enums with JsonStringEnumConverter to string type in OpenAPI documentation.
/// </summary>
public class SwaggerFilterJsonStringEnum : ISchemaFilter
{
    /// <summary>
    /// Applies string representation to enum schemas that use JsonStringEnumConverter.
    /// </summary>
    /// <param name="schema">The OpenAPI schema to modify.</param>
    /// <param name="context">The schema filter context containing type information.</param>
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        var type = context.Type;

        if (type.IsEnum &&
            type.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType == typeof(JsonStringEnumConverter))
        {
            schema.Type = "string";
            schema.Format = null;
            schema.Enum = type
                .GetEnumNames()
                .Select(name => new OpenApiString(name))
                .Cast<IOpenApiAny>()
                .ToList();
        }
    }
}