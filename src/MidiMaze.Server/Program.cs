using MidiMaze.Server.Game;
using MidiMaze.Server.Hubs;

// `dotnet MidiMaze.Server.dll --healthcheck` is what the Docker HEALTHCHECK runs: the aspnet base
// image has neither curl nor wget, so the app probes itself and reports through its exit code.
if (args.Contains("--healthcheck"))
    return await HealthCheckAsync();

// A published build carries its wwwroot next to the dll. Anchor the content root there so the game
// also works when started from another working directory (`dotnet path/to/MidiMaze.Server.dll`).
var appDir = AppContext.BaseDirectory;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = Directory.Exists(Path.Combine(appDir, "wwwroot")) ? appDir : null,
});

// Listen on localhost:5080 unless --urls / ASPNETCORE_URLS say otherwise. (Not in appsettings.json:
// a "Urls" entry there would win over the ASPNETCORE_URLS environment variable Docker sets.)
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
    builder.WebHost.UseUrls("http://localhost:5080");

// No-ops unless the process is actually started by the Windows Service Control Manager / systemd,
// so the same published binary runs standalone, as a Windows service or as a systemd unit.
builder.Host.UseWindowsService(options => options.ServiceName = "MidiMaze");
builder.Host.UseSystemd();

builder.Services.AddSignalR();
builder.Services.AddSingleton(_ => new RoomManager());
builder.Services.AddHostedService<GameLoopService>();

var app = builder.Build();

// Optional sub-path when a reverse proxy publishes the game under e.g. /midimaze. The client only
// uses relative URLs, so nothing else needs to change.
var pathBase = app.Configuration["PathBase"]?.Trim().Trim('/');
if (!string.IsNullOrEmpty(pathBase))
    app.UsePathBase("/" + pathBase);

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<GameHub>("/hub");
app.MapGet("/health", () => "ok");

app.Run();
return 0;

static async Task<int> HealthCheckAsync()
{
    var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5080";
    var first = urls.Split(';')[0]
        .Replace("+", "localhost")
        .Replace("*", "localhost")
        .Replace("0.0.0.0", "localhost")
        .TrimEnd('/');

    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var response = await http.GetAsync(first + "/health");
        return response.IsSuccessStatusCode ? 0 : 1;
    }
    catch
    {
        return 1;
    }
}
