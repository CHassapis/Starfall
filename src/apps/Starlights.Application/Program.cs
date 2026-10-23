using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Security.Cryptography;
using System.Text;
using Starlights.Modules.Characters.Services.Players;
using Scalar.AspNetCore;
using Starlights.Modules.Characters.Data.EntityFramework;
using Starlights.Modules.Characters.Data.EntityFramework.EventProcessing;
using Starlights.Modules.Characters.Endpoints.Characters.CreateCharacter;
using Starlights.Modules.Characters.Services.Processing;
using Starlights.Modules.Elements;
using Starlights.Modules.Elements.Data.EntityFramework;
using Starlights.Modules.Elements.Data.EntityFramework.EventProcessing;
using Starlights.Modules.Elements.Endpoints.Installation;
using Starlights.Platform.Components.FastEndpoints;
using Starlights.Platform.Components.Serilog;
using Starlights.Platform.Eventing.EventPublisher;
using Starlights.Platform.Hosting;

namespace Starlights.Application;

public sealed partial class Program
{
    /// <summary>
    /// The character a request under /api/characters is about: the route's parsed characterId when an endpoint
    /// matched, else the path's next segment if it reads as an id. Routes ignore letter case and accept every way of
    /// writing a GUID, so the check must too (it fails closed: an id-like segment is checked even without a match).
    /// </summary>
    internal static Guid? LockedCharacterId(HttpContext context)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api/characters", StringComparison.OrdinalIgnoreCase, out var rest))
        {
            return null;
        }
        if (context.GetRouteValue("characterId") is { } routed && Guid.TryParse(routed.ToString(), out var id))
        {
            return id;
        }
        var segment = rest.Value?.TrimStart('/').Split('/', 2)[0];
        return Guid.TryParse(Uri.UnescapeDataString(segment ?? string.Empty), out id) ? id : null;
    }


    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();

        builder.Services.AddAuthorization();
        builder.Services.AddOpenApi(options =>
        {
            // schemas are named by class name; the content administration has a few classes named like the
            // characters' (GetAbilityScoresResponse, SkillDataModel, …), so those get an "Elements" prefix instead of
            // one silently describing the other
            var characterTypeNames = typeof(CreateCharacterEndpoint).Assembly.GetTypes().Select(t => t.Name).ToHashSet();
            options.CreateSchemaReferenceId = type =>
            {
                var id = Microsoft.AspNetCore.OpenApi.OpenApiOptions.CreateDefaultSchemaReferenceId(type);
                return id is not null && characterTypeNames.Contains(type.Type.Name)
                    && type.Type.Namespace?.StartsWith("Starlights.Modules.Elements", StringComparison.Ordinal) == true ? "Elements" + id : id;
            };
            // the published description (docs/api/openapi.json): no server address, each installation has its own
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new Microsoft.OpenApi.OpenApiInfo
                {
                    Title = "Starfall API",
                    Version = "v1",
                    Description = "The HTTP API of a Starfall server (the web app uses it too). Guide: docs/api/README.md in the repository.",
                };
                document.Servers = [];
                return Task.CompletedTask;
            });
            // query parameters ([QueryParam]) are not request bodies: a GET or DELETE never has one
            options.AddOperationTransformer((operation, context, _) =>
            {
                if (context.Description.HttpMethod is "GET" or "DELETE")
                {
                    operation.RequestBody = null;
                }
                return Task.CompletedTask;
            });
        });

        // other origins only in development (the Vite dev server): the web app and the API share one origin behind
        // nginx, and outside development another site must not use a player's sign-in cookie (Cloudflare Access)
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                if (builder.Environment.IsDevelopment())
                {
                    policy.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowCredentials().AllowAnyMethod();
                }
            });
        });

        // add the platform services and its modules
        builder.AddStarlightsPlatform(options =>
        {
            // characters module
            options.AdditionalAssemblies.Add(typeof(CharactersContext).Assembly);
            options.AdditionalAssemblies.Add(typeof(CreateCharacterEndpoint).Assembly);
            options.AdditionalAssemblies.Add(typeof(RegistrationProcessor).Assembly);
            options.AddEventProcessingComponent();

            // elements module
            options.AdditionalAssemblies.Add(typeof(ElementsModule).Assembly);
            options.AdditionalAssemblies.Add(typeof(ElementsContext).Assembly);
            options.AdditionalAssemblies.Add(typeof(InitializationEndpoint).Assembly);
            options.AddElementsEventProcessingComponent();

            // platform components
            options.AddFastEndpointsComponent();
            options.AddSerilogComponent();
            options.AdditionalAssemblies.Add(typeof(EventPublisherComponent).Assembly);
        });

        // the Content page's job: brings Aurora Legacy and 5etools in from their GitHub links (self-hosting kit)
        builder.Services.AddSingleton<Starlights.Application.Content.ContentSync>();

        var app = builder.Build();

        app.MapDefaultEndpoints();
        if (!app.Environment.IsDevelopment())
        {
            // liveness for the container healthcheck: answers while the process serves requests and reports nothing
            // else (the full /health report stays development-only); outside /api, so nginx never exposes it
            app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
        }

        // the API description: in development, and in the integration tests that write docs/api/openapi.json from it
        // (never in production; the published copy lives in the repository)
        if (app.Environment.IsDevelopment() || app.Environment.IsIntegration())
        {
            app.MapOpenApi();
        }
        if (app.Environment.IsDevelopment())
        {
            app.MapScalarApiReference(options =>
            {
                options.Servers = [];

                options.WithTitle("Starfall API")
                    .HideClientButton()
                    .WithClassicLayout()
                    .WithTheme(ScalarTheme.Alternate)
                    .EnableDarkMode()
                    .HideModels()
                    .WithDefaultHttpClient(ScalarTarget.JavaScript, ScalarClient.Fetch);
            });
        }

        // content administration (initialize, the Aurora import, creating/editing/deleting elements) needs the
        // admin key; players only ever read elements. There are no accounts, so without this any player could
        // wipe the content. With no key configured these requests are refused.
        var adminKey = Encoding.UTF8.GetBytes(app.Configuration["Admin:Key"] ?? string.Empty);
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            var needsAdmin = (path.StartsWithSegments("/api/elements") &&
                (!HttpMethods.IsGet(context.Request.Method) || path.StartsWithSegments("/api/elements/initialize")) &&
                !path.StartsWithSegments("/api/elements/aurora-lookup")) || // read-only, POST only for the id list
                // campaigns are not here: anyone may start one, and each endpoint checks that the reader is that
                // campaign's DM (its DM token or the admin token), or a player acting on their own character
                (path.StartsWithSegments("/api/admin") && !path.StartsWithSegments("/api/admin/unlock"));

            var given = Encoding.UTF8.GetBytes(context.Request.Headers["X-Admin-Key"].ToString());
            var keyOk = adminKey.Length > 0 && CryptographicOperations.FixedTimeEquals(given, adminKey);
            if (needsAdmin && !keyOk && !context.RequestServices.GetRequiredService<PlayerAccess>().HasAdminToken(context.Request.Headers[PlayerAccess.TokenHeader]))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("This needs the admin key (X-Admin-Key header).");
                return;
            }

            await next();
        });

        // everything under /api/characters/{id} of a password-locked player needs that player's unlock token
        // (lists and creation check it in their endpoints)
        app.Use(async (context, next) =>
        {
            if (LockedCharacterId(context) is { } characterId)
            {
                var access = context.RequestServices.GetRequiredService<PlayerAccess>();
                var player = await access.GetCharacterPlayerAsync(characterId);
                if (!string.IsNullOrEmpty(player) && !await access.CanActAsAsync(context.Request.Headers[PlayerAccess.TokenHeader], player))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new { locked = player });
                    return;
                }
            }

            await next();
        });

        // configure the platform and its modules
        app.UseStarlightsPlatform();
        app.UseCors();

        // only when this process serves HTTPS itself (the "https" launch profile): behind the reverse proxy
        // (deploy/: plain HTTP on 8080) there is no HTTPS port to redirect to, and the middleware only logged
        // "Failed to determine the https port for redirect"
        if (ServesHttps(app.Configuration))
        {
            app.UseHttpsRedirection();
        }
        app.UseAuthorization();

        app.Run();
    }

    private static bool ServesHttps(IConfiguration configuration) =>
        (configuration["urls"] ?? string.Empty).Contains("https://", StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrEmpty(configuration["https_ports"])
        || !string.IsNullOrEmpty(configuration["https_port"])
        || configuration.GetSection("Kestrel:Endpoints").GetChildren()
            .Any(e => (e["Url"] ?? string.Empty).StartsWith("https://", StringComparison.OrdinalIgnoreCase));
}
