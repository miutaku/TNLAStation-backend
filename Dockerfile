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
ARG VERSION=1.0.0
COPY . .
# The migrator ships in the same image so schema changes and the code that
# depends on them are released together, while staying a separate process.
RUN dotnet publish src/TNLAStation.Api/TNLAStation.Api.csproj \
    --configuration "$BUILD_CONFIGURATION" \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false \
    /p:Version="$VERSION" \
    && dotnet publish src/TNLAStation.Migrator/TNLAStation.Migrator.csproj \
    --configuration "$BUILD_CONFIGURATION" \
    --no-restore \
    --output /app/publish-migrator \
    /p:UseAppHost=false \
    /p:Version="$VERSION"

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
ARG APP_UID=1654

RUN apt-get update \
    && apt-get install --yes --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir --parents /var/lib/tnlastation /recorded \
    && chown "$APP_UID:$APP_UID" /var/lib/tnlastation /recorded

WORKDIR /app
COPY --from=publish --chown=$APP_UID:$APP_UID /app/publish/ ./
COPY --from=publish --chown=$APP_UID:$APP_UID /app/publish-migrator/ ./migrator/

# これ以外のアプリ設定 (Mirakurun/Storage/Kodi/FfmpegWorker 等) は焼き込まず、
# config/appsettings.Production.json (bind mount) から読む。ここはコンテナ内部の実装詳細
# (起動ポート・作業ディレクトリ) だけに留める。ffmpeg/ffprobe は別コンテナ (ffmpeg-worker)
# にしか無い。
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    Streaming__WorkDirectory=/var/lib/tnlastation/streamfiles

EXPOSE 8080
USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD ["curl", "--fail", "--silent", "--show-error", "http://127.0.0.1:8080/api/version"]

ENTRYPOINT ["dotnet", "TNLAStation.Api.dll"]
