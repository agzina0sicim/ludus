using Ludus.Infrastructure;
using Ludus.Infrastructure.Configuration;
using Ludus.Infrastructure.Persistence;
using Serilog;
using Serilog.Events;
using Ludus.Api.Hubs;
using Ludus.Application.Downloads;
using Ludus.Infrastructure.Workers;
using Ludus.Api.Security;
using Ludus.Api.Features;
using Ludus.Application.Security;
using Ludus.Infrastructure.Operations;
using Ludus.Infrastructure.Imports;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Ludus.Domain.Common;
using Ludus.Api;

var accountCommand = AccountCommand.IsCommand(args);
var builder = WebApplication.CreateBuilder(accountCommand ? [] : args);
var paths = LudusPaths.FromConfiguration(builder.Configuration);
paths.EnsureConfigDirectories();

builder.Host.UseSerilog((_, configuration) => configuration
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information)
    .WriteTo.File(Path.Combine(paths.LogsDirectory, "ludus.txt"),
        restrictedToMinimumLevel: LogEventLevel.Information, fileSizeLimitBytes: 10 * 1024 * 1024,
        rollOnFileSizeLimit: true, retainedFileCountLimit: 10)
    .WriteTo.File(Path.Combine(paths.LogsDirectory, "ludus.debug.txt"),
        fileSizeLimitBytes: 20 * 1024 * 1024, rollOnFileSizeLimit: true, retainedFileCountLimit: 5),
    preserveStaticLogger: true);

builder.Services.AddInfrastructure(paths);
builder.Services.AddSingleton<InstanceLock>();
builder.Services.AddProblemDetails();
builder.Services.AddSignalR();
builder.Services.AddSingleton<ReleaseTickets>();
builder.Services.AddSingleton<BasicPasswordAttemptLimiter>();
builder.Services.AddSingleton<IRealtimePublisher, SignalRPublisher>();
builder.Services.AddHostedService<AutomationWorker>();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
builder.Services.AddAuthentication(ApiAuthentication.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiAuthentication>(ApiAuthentication.SchemeName, _ => { })
    .AddCookie(ApiAuthentication.CookieScheme, options =>
    {
        options.Cookie.Name = "Ludus.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = false;
        options.Events.OnValidatePrincipal = ApiAuthentication.ValidateCookieAsync;
        options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-Token";
    options.Cookie.Name = "Ludus.Csrf";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

await using var app = builder.Build();

if (accountCommand)
{
    Environment.ExitCode = args.Length == 2 && string.Equals(args[1], "set", StringComparison.OrdinalIgnoreCase)
        ? await AccountCommand.RunAsync(app.Services, paths)
        : 2;
    if (Environment.ExitCode == 2 && (args.Length != 2 || !string.Equals(args[1], "set", StringComparison.OrdinalIgnoreCase)))
    {
        Console.Error.WriteLine("Verwendung / Usage: Ludus.Api account set");
    }
    return;
}

try
{
    var instanceLock = app.Services.GetRequiredService<InstanceLock>();
    using var lockRelease = app.Lifetime.ApplicationStopped.Register(instanceLock.Dispose);
    await BackupService.ApplyPendingAsync(paths, CancellationToken.None);
    using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
    using (var scope = app.Services.CreateScope())
    {
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(timeout.Token);
        var security = await scope.ServiceProvider.GetRequiredService<AppDbContext>().SecuritySettings.SingleAsync(timeout.Token);
        var bootstrap = Path.Combine(paths.ConfigDirectory, "bootstrap-api-key");
        SafePaths.RejectLinks(bootstrap);
        if (security.AccountSetupMode != AccountSetupMode.Complete)
        {
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using var file = new FileStream(bootstrap, options);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(bootstrap, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            await using var writer = new StreamWriter(file);
            await writer.WriteAsync(scope.ServiceProvider.GetRequiredService<ISecretProtector>().Unprotect(security.ApiKey));
        }
        else File.Delete(bootstrap);
    }

    app.UseMiddleware<ApiErrorMiddleware>();
    app.UseMiddleware<TrustedProxyMiddleware>();
    app.Use(async (context, next) =>
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
        await next(context);
    });

    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.UseRouting();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseMiddleware<CsrfMiddleware>();
    app.MapGet("/api/v1/system/status", (HttpContext context) =>
    {
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { status = "ok" });
    }).AllowAnonymous();
    app.MapFallback("/api/{**path}", () => Results.NotFound());
    app.MapHub<ActivityHub>("/hubs/activity", options => options.CloseOnAuthenticationExpiration = true).RequireAuthorization();
    app.MapControllers();
    if (Directory.Exists(app.Environment.WebRootPath))
    {
        app.MapFallbackToFile("index.html").AllowAnonymous();
    }

    app.Logger.LogInformation("Ludus is starting.");
    await app.RunAsync();
}
catch (Exception exception)
{
    app.Logger.LogCritical(exception, "Ludus stopped unexpectedly.");
    throw;
}

public partial class Program;
