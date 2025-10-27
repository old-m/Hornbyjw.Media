using Azure.Identity;
using Microsoft.Extensions.Azure;
using Hornbyjw.Media.Services;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Configuration.AddEnvironmentVariables();

// Response caching middleware - honors [ResponseCache] attributes and cache-control headers
builder.Services.AddResponseCaching();

// File-based blob cache for faster repeated reads
builder.Services.AddSingleton<IFileBlobCache, FileBlobCache>();

builder.Services.Configure<Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration>(config =>
{
config.SetAzureTokenCredential(new DefaultAzureCredential());
});

builder.Services.AddApplicationInsightsTelemetry(new Microsoft.ApplicationInsights.AspNetCore.Extensions.ApplicationInsightsServiceOptions
{
    ConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
});

// Get keys
var kvUrl = builder.Configuration["KeyVaultUrl"];
var storageUrl = builder.Configuration["StorageUrl"];

// Add Azure service clients
builder.Services.AddAzureClients(async clientBuilder =>
{
    // Register clients for each service
    clientBuilder.AddSecretClient(new Uri(kvUrl));
    clientBuilder.AddBlobServiceClient(new Uri(storageUrl));

    // Set a credential for all clients to use by default
    DefaultAzureCredential credential = new();
    clientBuilder.UseCredential(credential);

});

var app = builder.Build();

app.UseHttpsRedirection();

// Enable server-side response caching (honours [ResponseCache] on controllers)
app.UseResponseCaching();

app.UseAuthorization();

app.MapControllers();

app.Run();
