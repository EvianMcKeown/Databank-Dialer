using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxConcurrentConnections = 200;
    options.Limits.MaxConcurrentUpgradedConnections = 60;
    options.Limits.MaxRequestBodySize = 64 * 1024;
    options.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
});

builder.Services.Configure<AudioLimits>(builder.Configuration.GetSection("Audio"));
builder.Services.AddSingleton<StreamAdmission>();
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 32 * 1024;
    options.HandshakeTimeout = TimeSpan.FromSeconds(5);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        });
    });
}

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

app.UseForwardedHeaders();
if (allowedOrigins.Length > 0)
{
    app.UseCors();
}
app.UseDefaultFiles(); // index.html
app.UseStaticFiles(); // JS/CSS

app.MapHub<AudioHub>("/audioHub"); // audio data

app.Run();
