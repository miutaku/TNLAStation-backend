# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS restore
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/TNLAStation.Domain/TNLAStation.Domain.csproj src/TNLAStation.Domain/
COPY src/TNLAStation.Application/TNLAStation.Application.csproj src/TNLAStation.Application/
COPY src/TNLAStation.Infrastructure/TNLAStation.Infrastructure.csproj src/TNLAStation.Infrastructure/
COPY src/TNLAStation.Api/TNLAStation.Api.csproj src/TNLAStation.Api/
COPY src/TNLAStation.Migrator/TNLAStation.Migrator.csproj src/TNLAStation.Migrator/
RUN dotnet restore src/TNLAStation.Api/TNLAStation.Api.csproj \
    && dotnet restore src/TNLAStation.Migrator/TNLAStation.Migrator.csproj

FROM restore AS publish
ARG BUILD_CONFIGURATION=Release
COPY . .
RUN dotnet publish src/TNLAStation.Api/TNLAStation.Api.csproj \
    --configuration "$BUILD_CONFIGURATION" \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false
# The migrator ships in the same image so that schema changes and the code that
# depends on them are always released together, while staying a separate process.
RUN dotnet publish src/TNLAStation.Migrator/TNLAStation.Migrator.csproj \
    --configuration "$BUILD_CONFIGURATION" \
    --no-restore \
    --output /app/publish-migrator \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
ARG APP_UID=1654

RUN apt-get update \
    && apt-get install --yes --no-install-recommends ca-certificates curl ffmpeg \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir --parents /var/lib/tnlastation /recorded \
    && chown "$APP_UID:$APP_UID" /var/lib/tnlastation /recorded

WORKDIR /app
COPY --from=publish --chown=$APP_UID:$APP_UID /app/publish/ ./
COPY --from=publish --chown=$APP_UID:$APP_UID /app/publish-migrator/ ./migrator/

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    Mirakurun__BaseUrl=http://mirakurun:40772 \
    FFmpeg__ExecutablePath=/usr/bin/ffmpeg \
    FFmpeg__ProbeExecutablePath=/usr/bin/ffprobe \
    Streaming__FfmpegPath=/usr/bin/ffmpeg \
    Streaming__WorkDirectory=/var/lib/tnlastation/streamfiles \
    Storage__DataDirectory=/var/lib/tnlastation

EXPOSE 8080
USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD ["curl", "--fail", "--silent", "--show-error", "http://127.0.0.1:8080/api/version"]

ENTRYPOINT ["dotnet", "TNLAStation.Api.dll"]
