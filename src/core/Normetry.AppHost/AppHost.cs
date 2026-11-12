var builder = DistributedApplication.CreateBuilder(args);

// Probed over http so the API reports healthy even before the HTTPS dev certificate is trusted.
builder
    .AddProject<Projects.Normetry_Api>("api")
    .WithHttpHealthCheck("/health", endpointName: "http");

builder.Build().Run();
