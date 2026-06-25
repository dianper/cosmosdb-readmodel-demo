using ReadModelDemo.ApiService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.AddAzureCosmosClient("cosmos");
builder.Services.AddScoped<CosmosDirectoryService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    await SeedData.EnsureSeededAsync(app.Services);
}

app.MapGet("/", () => "Read Model API - use GET /readmodel?tenantId={tenantId}&page=1&pageSize=200");

var mapReadModel = async (
    string tenantId,
    int? page,
    int? pageSize,
    string? strategy,
    CosmosDirectoryService service,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(tenantId))
        return Results.BadRequest("tenantId is required.");

    var normalizedPage = Math.Max(1, page ?? 1);
    var normalizedPageSize = Math.Clamp(pageSize ?? 200, 1, 2000);
    var normalizedStrategy = string.IsNullOrWhiteSpace(strategy) ? "fanout" : strategy;

    var result = await service.GetFlatDirectoryAsync(
        tenantId,
        normalizedPage,
        normalizedPageSize,
        normalizedStrategy,
        cancellationToken);

    return Results.Ok(result);
};

app.MapGet("/readmodel", mapReadModel)
.WithName("GetReadModel")
.WithOpenApi();

app.MapGet("/benchmark", async (
    string tenantId,
    int? page,
    int? pageSize,
    int? iterations,
    CosmosDirectoryService service,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(tenantId))
        return Results.BadRequest("tenantId is required.");

    var normalizedPage = Math.Max(1, page ?? 1);
    var normalizedPageSize = Math.Clamp(pageSize ?? 100, 1, 2000);
    var normalizedIterations = Math.Clamp(iterations ?? 5, 1, 20);

    var benchmark = await service.RunBenchmarkAsync(
        tenantId,
        normalizedPage,
        normalizedPageSize,
        normalizedIterations,
        cancellationToken);

    return Results.Ok(benchmark);
})
.WithName("RunDirectoryBenchmark")
.WithOpenApi();

app.MapDefaultEndpoints();

app.Run();
