using FluentValidation;
using Npgsql;
using Pgvector.Npgsql;
using Serilog;
using SmartDoc.Api.Endpoints;
using SmartDoc.Api.Infrastructure;
using SmartDoc.Api.Models;
using SmartDoc.Api.Services;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Render injects PORT; fall back to 5001 for local dev
    var port = Environment.GetEnvironmentVariable("PORT") ?? "5001";
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

    // Serilog
    builder.Host.UseSerilog((ctx, services, config) =>
        config
            .ReadFrom.Configuration(ctx.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File("logs/smartdoc-.log", rollingInterval: RollingInterval.Day));

    // CORS — origins read from config so both local dev and deployed frontend work
    var allowedOrigins = builder.Configuration["AllowedOrigins"]?.Split(',', StringSplitOptions.RemoveEmptyEntries)
        ?? ["http://localhost:5173", "http://localhost:3000"];

    builder.Services.AddCors(opts =>
        opts.AddDefaultPolicy(policy =>
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()));

    // PostgreSQL / pgvector data source
    var connStr = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connStr);
    dataSourceBuilder.UseVector();
    var dataSource = dataSourceBuilder.Build();
    builder.Services.AddSingleton(dataSource);

    // Repository
    builder.Services.AddScoped<IVectorRepository, VectorRepository>();

    // Services
    builder.Services.AddSingleton<IIngestionQueue, IngestionQueue>();
    builder.Services.AddScoped<IChunkingService, ChunkingService>();
    builder.Services.AddScoped<IRetrievalService, RetrievalService>();

    builder.Services.AddHttpClient<IEmbeddingService, OpenAiEmbeddingService>();
    builder.Services.AddHttpClient<ILlmService, GroqLlmService>();

    // Background service
    builder.Services.AddHostedService<PdfIngestionBackgroundService>();

    // FluentValidation
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();

    // Swagger / OpenAPI
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(opts =>
    {
        opts.SwaggerDoc("v1", new() { Title = "SmartDoc API", Version = "v1" });
    });

    var app = builder.Build();

    app.UseSerilogRequestLogging();
    app.UseCors();

    app.UseSwagger();
    app.UseSwaggerUI();

    app.MapDocumentEndpoints();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "SmartDoc.Api terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}
