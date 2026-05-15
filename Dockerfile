# syntax=docker/dockerfile:1.7
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0

# -----------------------------------------------------------------------------
# Restore — copy only csproj/sln first so the restore layer is cache-friendly.
# -----------------------------------------------------------------------------
FROM ${SDK_IMAGE} AS restore
WORKDIR /src
COPY DevHub.slnx Directory.Build.props ./
COPY src/DevHub.Api/DevHub.Api.csproj                                 src/DevHub.Api/
COPY src/DevHub.Contracts/DevHub.Contracts.csproj                     src/DevHub.Contracts/
COPY src/DevHub.Modules.Workspace/DevHub.Modules.Workspace.csproj     src/DevHub.Modules.Workspace/
COPY src/DevHub.Modules.Identity/DevHub.Modules.Identity.csproj       src/DevHub.Modules.Identity/
COPY src/DevHub.Modules.ExecutorRegistry/DevHub.Modules.ExecutorRegistry.csproj  src/DevHub.Modules.ExecutorRegistry/
COPY src/DevHub.Modules.WorkItems/DevHub.Modules.WorkItems.csproj     src/DevHub.Modules.WorkItems/
COPY src/DevHub.Modules.Audit/DevHub.Modules.Audit.csproj             src/DevHub.Modules.Audit/
COPY src/DevHub.Modules.Notifications/DevHub.Modules.Notifications.csproj  src/DevHub.Modules.Notifications/
RUN dotnet restore src/DevHub.Api/DevHub.Api.csproj

# -----------------------------------------------------------------------------
# Build + publish.
# -----------------------------------------------------------------------------
FROM restore AS publish
COPY src/ src/
RUN dotnet publish src/DevHub.Api/DevHub.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# -----------------------------------------------------------------------------
# Runtime — aspnet only, non-root, env-configured.
# -----------------------------------------------------------------------------
FROM ${RUNTIME_IMAGE} AS runtime
WORKDIR /app
# wget for the container HEALTHCHECK (mcr aspnet image ships neither wget nor curl by default).
RUN apt-get update \
 && apt-get install -y --no-install-recommends wget \
 && rm -rf /var/lib/apt/lists/*
COPY --from=publish /app/publish ./
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=false
EXPOSE 8080
# .NET aspnet image ships an `app` user (UID/GID 1654) since .NET 8+.
USER app
ENTRYPOINT ["dotnet", "DevHub.Api.dll"]
