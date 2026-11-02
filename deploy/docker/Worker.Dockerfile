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

# The ASP.NET Core runtime image is Debian with ICU, which string normalization needs (InvariantGlobalization is
# off). It ships without curl, so the health check installs it.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
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
