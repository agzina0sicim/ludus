using System.Net;
using Ludus.Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ludus.Api.Security;

public sealed class TrustedProxyMiddleware(RequestDelegate next, ILoggerFactory logger)
{
    private static readonly System.Net.IPNetwork[] PrivateNetworks = [System.Net.IPNetwork.Parse("10.0.0.0/8"), System.Net.IPNetwork.Parse("172.16.0.0/12"),
        System.Net.IPNetwork.Parse("192.168.0.0/16"), System.Net.IPNetwork.Parse("127.0.0.0/8"), System.Net.IPNetwork.Parse("::1/128")];

    public static System.Net.IPNetwork Validate(string value)
    {
        if (!System.Net.IPNetwork.TryParse(value, out var network)
            || !PrivateNetworks.Any(parent => parent.BaseAddress.AddressFamily == network.BaseAddress.AddressFamily
                && network.PrefixLength >= parent.PrefixLength && parent.Contains(network.BaseAddress)))
            throw new ArgumentException("Trusted proxy networks must be RFC1918 or loopback CIDRs.");
        return network;
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext database)
    {
        var settings = await database.SecuritySettings.AsNoTracking().SingleAsync(context.RequestAborted);
        if (!settings.TrustForwardedHeaders) { await next(context); return; }
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = settings.ForwardLimit, RequireHeaderSymmetry = true
        };
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var network in settings.TrustedProxyNetworks) options.KnownIPNetworks.Add(Validate(network));
        if (options.KnownIPNetworks.Count == 0) { await next(context); return; }
        await new ForwardedHeadersMiddleware(next, logger, Options.Create(options)).Invoke(context);
    }
}
