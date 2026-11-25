# syntax=docker/dockerfile:1
FROM node:24-bookworm-slim AS frontend
WORKDIR /build/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS backend
WORKDIR /build
COPY global.json Directory.Build.props Directory.Packages.props NuGet.Config ./
COPY src/Ludus.Domain/Ludus.Domain.csproj src/Ludus.Domain/packages.lock.json src/Ludus.Domain/
COPY src/Ludus.Application/Ludus.Application.csproj src/Ludus.Application/packages.lock.json src/Ludus.Application/
COPY src/Ludus.Infrastructure/Ludus.Infrastructure.csproj src/Ludus.Infrastructure/packages.lock.json src/Ludus.Infrastructure/
COPY src/Ludus.Api/Ludus.Api.csproj src/Ludus.Api/packages.lock.json src/Ludus.Api/
RUN dotnet restore src/Ludus.Api/Ludus.Api.csproj --locked-mode
COPY src/ src/
COPY --from=frontend /build/frontend/dist/ src/Ludus.Api/wwwroot/
RUN dotnet publish src/Ludus.Api/Ludus.Api.csproj -c Release --no-restore -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl util-linux \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=backend /publish/ ./
COPY --chmod=755 docker/entrypoint.sh /usr/local/bin/ludus-entrypoint
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0 \
    Ludus__ConfigDirectory=/config \
    Ludus__DownloadDirectory=/downloads \
    Ludus__RomDirectory=/roms \
    PUID=1000 \
    PGID=1000 \
    UMASK=002
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=120s --retries=3 \
    CMD curl --fail --silent --show-error --max-time 4 http://127.0.0.1:8080/api/v1/system/status || exit 1
ENTRYPOINT ["/usr/local/bin/ludus-entrypoint"]
CMD ["dotnet", "Ludus.Api.dll"]
