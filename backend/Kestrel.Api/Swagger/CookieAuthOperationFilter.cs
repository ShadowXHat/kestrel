using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Kestrel.Api.Swagger;

/// <summary>
/// Attaches the cookie security requirement to every operation that is not
/// [AllowAnonymous], so the OpenAPI document mirrors the actual Phase 1
/// fallback policy (authenticate or get 401).
/// </summary>
public class CookieAuthOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var allowAnonymous =
            context.MethodInfo.GetCustomAttributes(true).OfType<AllowAnonymousAttribute>().Any() ||
            (context.MethodInfo.DeclaringType?.GetCustomAttributes(true).OfType<AllowAnonymousAttribute>().Any() ?? false);

        if (allowAnonymous)
        {
            return;
        }

        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "cookie"
                    }
                }
            ] = new List<string>()
        });
    }
}
