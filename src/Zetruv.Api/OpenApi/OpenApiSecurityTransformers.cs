using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Zetruv.Api.OpenApi;

internal sealed class BearerSecuritySchemeTransformer(
    IAuthenticationSchemeProvider authenticationSchemeProvider) : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var authenticationSchemes = await authenticationSchemeProvider.GetAllSchemesAsync();
        if (!authenticationSchemes.Any(x =>
                string.Equals(
                    x.Name,
                    JwtBearerDefaults.AuthenticationScheme,
                    StringComparison.Ordinal)))
        {
            return;
        }

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[JwtBearerDefaults.AuthenticationScheme] =
            new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                In = ParameterLocation.Header,
                BearerFormat = "JWT",
                Description = "CMS administrator JWT. Obtain it from POST /api/v1/cms/auth/login."
            };
        document.Components.SecuritySchemes[Zetruv.Api.Features.Auth.CustomerAuthConstants.Scheme] =
            new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                In = ParameterLocation.Header,
                BearerFormat = "JWT",
                Description = "Customer JWT. Obtain it from POST /api/v1/auth/login or verify-email."
            };
    }
}

internal sealed class BearerSecurityRequirementTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var requiresAuthorization = metadata.OfType<IAuthorizeData>().Any();
        var allowsAnonymous = metadata.OfType<IAllowAnonymous>().Any();

        if (!requiresAuthorization || allowsAnonymous || context.Document is null)
        {
            return Task.CompletedTask;
        }

        var customerEndpoint = metadata.OfType<IAuthorizeData>()
            .Any(x => (x.AuthenticationSchemes ?? "").Split(',')
                .Any(s => string.Equals(s.Trim(),
                    Zetruv.Api.Features.Auth.CustomerAuthConstants.Scheme,
                    StringComparison.Ordinal)));
        var scheme = customerEndpoint
            ? Zetruv.Api.Features.Auth.CustomerAuthConstants.Scheme
            : JwtBearerDefaults.AuthenticationScheme;
        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(scheme, context.Document)] = []
        });

        return Task.CompletedTask;
    }
}
