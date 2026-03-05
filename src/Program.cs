var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(origin => true).AllowCredentials();
    });
});

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles(); // index.html
app.UseStaticFiles(); // JS/CSS
app.MapHub<AudioHub>("/audioHub"); // audio data

app.Run();
