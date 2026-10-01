# syntax=docker/dockerfile:1
# CampusSpace API (Task 6.D3). Build context: backend/ (render.yaml: dockerContext ./backend).
#   docker build -t campusspace-api backend
# No secret is baked in: every secret is an environment variable on the host (README, Deploy).

# Build on the build machine's own architecture and cross-compile for the target (no emulation on an arm64 Mac).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /src
# Restore first, so a code change doesn't re-download every package.
COPY CampusSpace.Api/CampusSpace.Api.csproj CampusSpace.Api/
RUN dotnet restore CampusSpace.Api/CampusSpace.Api.csproj -a "$TARGETARCH"
COPY CampusSpace.Api/ CampusSpace.Api/
RUN dotnet publish CampusSpace.Api/CampusSpace.Api.csproj -c Release -a "$TARGETARCH" --no-restore \
        -o /app /p:UseAppHost=false \
    && rm -f /app/appsettings.Development.json

# Chiseled Ubuntu: no shell or package manager, runs as the non-root "app" user (uid 1654) by default. "-extra" adds
# ICU and tzdata (culture-aware formatting in emails and reports); CA certificates are included for SslMode=VerifyFull.
FROM mcr.microsoft.com/dotnet/aspnet:8.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app .
# Render sets PORT (Program.cs listens on http://0.0.0.0:$PORT; TLS ends at Render's proxy). 8080 is for local runs.
# ASPNETCORE_HTTP_PORTS is cleared so the base image's default doesn't compete with PORT.
# Workstation GC: a smaller heap on Render's 512 MB free instance.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS= \
    PORT=8080 \
    DOTNET_gcServer=0
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "CampusSpace.Api.dll"]
