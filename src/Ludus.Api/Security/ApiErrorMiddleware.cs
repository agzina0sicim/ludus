using System.Security.Cryptography;
using System.Text.Json;
using Ludus.Application.Downloads;
using Ludus.Application.Indexers;
using Ludus.Application.Metadata;
using Ludus.Infrastructure.Imports;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Api.Security;

public sealed class ApiErrorMiddleware(RequestDelegate next, ILogger<ApiErrorMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            var (status, code) = exception switch
            {
                KeyNotFoundException or FileNotFoundException => (404, "NotFound"),
                CryptographicException => (400, "InvalidToken"),
                ArgumentException or JsonException or InvalidDataException => (400, "InvalidRequest"),
                DbUpdateConcurrencyException => (409, "ConcurrentModification"),
                DbUpdateException => (409, "DatabaseConstraint"),
                MetadataException error => (502, error.Code),
                IndexerException error => (502, error.Code),
                DownloadClientException error => (502, error.Code),
                ImportException error => (409, error.Code),
                IOException or UnauthorizedAccessException => (503, "StorageUnavailable"),
                InvalidOperationException => (409, "OperationConflict"),
                _ => (500, "InternalError")
            };
            logger.LogWarning("API request failed with {Code} ({ExceptionType}).", code, exception.GetType().Name);
            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new { status, code, traceId = context.TraceIdentifier }, context.RequestAborted);
        }
    }
}
