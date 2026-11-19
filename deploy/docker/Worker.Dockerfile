# Section 15.2. Build from the repository root: docker build -f deploy/docker/Worker.Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY .editorconfig Directory.Build.props Directory.Packages.props global.json ./
COPY src/*/*.csproj ./
RUN for f in *.csproj; do d=${f%.csproj}; mkdir -p src/$d && mv $f src/$d/; done
RUN dotnet restore src/Marketplace.Worker/Marketplace.Worker.csproj
COPY src/ src/
COPY docs/sql/ docs/sql/
RUN dotnet publish src/Marketplace.Worker/Marketplace.Worker.csproj \
    -c Release -o /app --no-restore

# The ASP.NET Core runtime image is Ubuntu 24.04 with ICU, which string normalization needs (InvariantGlobalization is
# off). It ships without curl, so the health check installs it.
# The upgrade applies Ubuntu security updates published since the base tag was last rebased. Without it the image
# inherits whatever fixed advisories are outstanding against :11.0, and the Trivy gate in CI fails on them.
FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS runtime
# Ubuntu's mirrors intermittently refuse connections from CI runners, and apt treats a failed fetch as fatal, so
# the build dies on this layer for reasons unrelated to the change under test. Retry each fetch and cap the wait
# per connection, so an unreachable mirror IP is abandoned quickly and retried against another instead of
# stalling for minutes. This cannot survive a full archive.ubuntu.com outage; it covers the common case of a
# single bad mirror or a transient reset.
RUN printf 'Acquire::Retries "3";\nAcquire::http::Timeout "20";\nAcquire::https::Timeout "20";\n' \
      > /etc/apt/apt.conf.d/99-retries \
    && apt-get update \
    && apt-get upgrade -y \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system appgroup \
    && useradd --system --gid appgroup --no-create-home appuser
WORKDIR /app
COPY --from=build /app .
USER appuser
EXPOSE 8081
HEALTHCHECK --interval=30s --timeout=3s --retries=3 \
  CMD curl -f http://localhost:8081/health/live || exit 1
ENTRYPOINT ["dotnet", "Marketplace.Worker.dll"]
