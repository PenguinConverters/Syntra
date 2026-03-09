using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.Identity.Web;
using PenguinConverters.Syntra.Api.Settings;

namespace PenguinConverters.Syntra.Api;

/// <summary>
/// Entry point for the Syntra REST API.
/// Authentication:
///   - Windows Integrated (Negotiate/Kerberos): Pass-through from frontend client.
///     The authenticated user's Windows identity is forwarded to SQL Server for
///     per-record authorization via database views and functions.
///   - JWT Bearer with OBO (On-Behalf-Of) OAuth2 flow: For Azure-hosted API.
///     Exchanges the user's Azure AD token for a SQL access token, enabling
///     Azure SQL integrated authentication under the user's identity.
/// Also configures authorization policies, Swagger/OpenAPI, CORS, and controller routing.
/// </summary>
public static class Program
{
    /// <summary>
    /// Application entry point.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // ---------------------------------------------------------------------------
        // Configuration
        // ---------------------------------------------------------------------------
        builder.Services.Configure<DatabaseSettings>(
            builder.Configuration.GetSection(DatabaseSettings.SectionName));

        // ---------------------------------------------------------------------------
        // Authentication: dual scheme — Windows Negotiate + JWT Bearer (OBO)
        // ---------------------------------------------------------------------------
        builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = "SyntraAuth";
                options.DefaultChallengeScheme = "SyntraAuth";
            })
            .AddNegotiate()
            .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("Authentication:AzureAd"))
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddInMemoryTokenCaches();

        // Policy scheme: try Bearer first, fall back to Negotiate
        builder.Services.AddAuthentication()
            .AddPolicyScheme("SyntraAuth", "Syntra Authentication", options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    string? authHeader = context.Request.Headers.Authorization.FirstOrDefault();
                    if (authHeader is not null &&
                        authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        return "Bearer";
                    }
                    return NegotiateDefaults.AuthenticationScheme;
                };
            });

        // ---------------------------------------------------------------------------
        // Authorization policies
        // ---------------------------------------------------------------------------
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireAuthenticated", policy =>
                policy.RequireAuthenticatedUser());

            options.AddPolicy("RequireOboScope", policy =>
                policy.RequireClaim("scp", "user_impersonation"));

            options.AddPolicy("RequireAppRole", policy =>
                policy.RequireRole("Syntra.AccessAsApp"));

            options.AddPolicy("RequireOboOrAppRole", policy =>
                policy.RequireAssertion(context =>
                    context.User.HasClaim("scp", "user_impersonation") ||
                    context.User.IsInRole("Syntra.AccessAsApp")));

            // Default policy: must be authenticated
            options.FallbackPolicy = options.GetPolicy("RequireAuthenticated");
        });

        // ---------------------------------------------------------------------------
        // CORS
        // ---------------------------------------------------------------------------
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                string[] origins = builder.Configuration.GetSection("Cors:AllowedOrigins")
                    .Get<string[]>() ?? ["https://localhost:3000"];

                policy.WithOrigins(origins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        // ---------------------------------------------------------------------------
        // Controllers + Swagger
        // ---------------------------------------------------------------------------
        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.DefaultIgnoreCondition =
                    System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            });

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "Syntra API",
                Version = "v1",
                Description = "Syntra IAM/Automation REST API by Penguin Converters AG",
                Contact = new Microsoft.OpenApi.Models.OpenApiContact
                {
                    Name = "Syntra Team"
                }
            });

            // Include XML documentation if available
            string xmlFile = $"{typeof(Program).Assembly.GetName().Name}.xml";
            string xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
                options.IncludeXmlComments(xmlPath);
        });

        // ---------------------------------------------------------------------------
        // Build & Configure Pipeline
        // ---------------------------------------------------------------------------
        WebApplication app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "Syntra API v1 (static)");
                options.SwaggerEndpoint("/api/openapi.json", "Syntra API v1 (dynamic)");
            });
        }

        app.UseHttpsRedirection();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        // Dynamic $metadata endpoint (also mapped via MetadataController)
        app.MapGet("api/$metadata", async context =>
        {
            // Redirect to the MetadataController action
            context.Response.Redirect("/api/$metadata", permanent: false);
        }).ExcludeFromDescription();

        // Dynamic OpenAPI endpoint (also mapped via OpenApiController)
        app.MapGet("api/openapi.json", async context =>
        {
            // Redirect to the OpenApiController action
            context.Response.Redirect("/api/openapi.json", permanent: false);
        }).ExcludeFromDescription();

        app.Run();
    }
}
