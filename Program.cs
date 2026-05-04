using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RfidSyncApi.Application.DTOs;
using RfidSyncApi.Application.Services;
using RfidSyncApi.Application.Validators;
using RfidSyncApi.Infrastructure.Configuration;
using RfidSyncApi.Infrastructure.Middleware;
using RfidSyncApi.Infrastructure.Persistence;
using RfidSyncApi.Infrastructure.Repositories;
using Serilog;
using Serilog.Events;

// ══════════════════════════════════════════════════════════════════════════════
//  Bootstrap Serilog early so startup errors are captured
// ══════════════════════════════════════════════════════════════════════════════
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithThreadId()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    Log.Information("RFID Sync API starting up…");

    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog (full config from appsettings) ─────────────────────────────
    builder.Host.UseSerilog((ctx, services, config) =>
        config
            .ReadFrom.Configuration(ctx.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithEnvironmentName()
            .Enrich.WithThreadId()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    );

    // ── Configuration ──────────────────────────────────────────────────────
    builder.Services.Configure<ApiSettings>(
        builder.Configuration.GetSection(ApiSettings.SectionName));

    // ── Database (Azure SQL via EF Core) ───────────────────────────────────
    //    Local dev  → appsettings.Development.json
    //    Azure      → App Service > Configuration > Connection Strings (DefaultConnection / SQLAzure)
    //                 which surfaces as env var: ConnectionStrings__DefaultConnection
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException(
            "Connection string 'DefaultConnection' is not configured.\n" +
            "  • Local dev : set it in appsettings.Development.json\n" +
            "  • Azure     : App Service → Configuration → Connection Strings → DefaultConnection (SQLAzure)");

    Log.Information("Database target: {Server}",
        System.Text.RegularExpressions.Regex.Match(connectionString, @"Server=tcp:([^,;]+)").Groups[1].Value);

    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlServer(connectionString, sql =>
        {
            sql.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
            sql.CommandTimeout(60);
            sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
        }));

    // ── Application Services (DI) ──────────────────────────────────────────
    builder.Services.AddScoped<IRfidLogRepository, RfidLogRepository>();
    builder.Services.AddScoped<ISyncService, SyncService>();
    builder.Services.AddScoped<ISessionService, SessionService>();

    // ── FluentValidation ───────────────────────────────────────────────────
    builder.Services.AddScoped<IValidator<SyncRequest>, SyncRequestValidator>();

    // ── Controllers + JSON ─────────────────────────────────────────────────
    builder.Services.AddControllers()
        .AddJsonOptions(opts =>
        {
            opts.JsonSerializerOptions.DefaultIgnoreCondition =
                System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            opts.JsonSerializerOptions.PropertyNamingPolicy =
                System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
        });

    // ── Swagger / OpenAPI ──────────────────────────────────────────────────
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new()
        {
            Title = "RFID Sync API",
            Version = "v1",
            Description = "Offline-first RFID scan data synchronisation API for industrial Android deployments."
        });

        // Document the X-API-TOKEN header requirement
        c.AddSecurityDefinition("ApiToken", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Name = "X-API-TOKEN",
            Description = "Static API token required on all /api/* requests."
        });
        c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "ApiToken"
                    }
                },
                Array.Empty<string>()
            }
        });

        // Include XML comments if generated
        var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath)) c.IncludeXmlComments(xmlPath);
    });

    // ── CORS (configure per environment as needed) ─────────────────────────
    builder.Services.AddCors(options =>
        options.AddDefaultPolicy(policy =>
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

    // ── Response compression ───────────────────────────────────────────────
    builder.Services.AddResponseCompression(opts => opts.EnableForHttps = true);

    // ══════════════════════════════════════════════════════════════════════════
    //  Build + configure pipeline
    // ══════════════════════════════════════════════════════════════════════════
    var app = builder.Build();

    // ── DB initialisation: connectivity check → migrations → sample seed ─────
    //    seedSampleData=true only in Development so demo data never lands in prod
    var isDev = app.Environment.IsDevelopment();
    await DbInitializer.InitializeAsync(app.Services, app.Logger, seedSampleData: isDev);

    // ── Middleware pipeline (order matters) ────────────────────────────────
    app.UseResponseCompression();
    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
        opts.EnrichDiagnosticContext = (diag, ctx) =>
        {
            diag.Set("RequestSize", ctx.Request.ContentLength);
            diag.Set("UserAgent", ctx.Request.Headers["User-Agent"].FirstOrDefault());
            diag.Set("DeviceId", ctx.Request.Headers["X-Device-ID"].FirstOrDefault());
        };
    });

    // Swagger — always enabled (restrict to Development only before going to prod)
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "RFID Sync API v1");
        c.RoutePrefix = "swagger";
    });

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        // Production: generic error responses (no stack traces leaked)
        app.UseExceptionHandler(errApp =>
        {
            errApp.Run(async ctx =>
            {
                ctx.Response.StatusCode = 500;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsJsonAsync(new
                {
                    error = "InternalServerError",
                    message = "An unexpected error occurred. Please try again."
                });
            });
        });
    }

    app.UseCors();
    app.UseApiTokenAuthentication(); // ← Static token gate
    app.MapControllers();

    Log.Information("RFID Sync API started and ready to accept requests.");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application startup failed.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
