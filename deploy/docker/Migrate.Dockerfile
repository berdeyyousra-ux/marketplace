# T-062 (section 15.6): the migration job. The EF Core migration bundle, plus a script that signs in to PostgreSQL
# with the job's managed identity, applies the bundle, and creates the api and worker database roles. It runs as an
# Azure Container Apps job inside the virtual network, because the database has no public endpoint.
# Build from the repository root: docker build -f deploy/docker/Migrate.Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY .editorconfig Directory.Build.props Directory.Packages.props global.json dotnet-tools.json ./
COPY src/*/*.csproj ./
RUN for f in *.csproj; do d=${f%.csproj}; mkdir -p src/$d && mv $f src/$d/; done
RUN dotnet tool restore \
    && dotnet restore src/Marketplace.Infrastructure/Marketplace.Infrastructure.csproj --runtime linux-x64
COPY src/ src/
COPY docs/sql/ docs/sql/
# The bundle step loads the project's compiled assembly, as CI's build step provides, so build it first.
RUN dotnet build src/Marketplace.Infrastructure/Marketplace.Infrastructure.csproj --configuration Release --no-restore
RUN dotnet ef migrations bundle --project src/Marketplace.Infrastructure --startup-project src/Marketplace.Infrastructure \
    --configuration Release --self-contained --runtime linux-x64 --output /out/efbundle --force

# The bundle is self-contained, so the runtime-deps image is enough. psql runs the role setup; curl and jq fetch the
# managed identity's token from the Container Apps identity endpoint.
# The upgrade applies Ubuntu security updates published since the base tag was last rebased. Without it the image
# inherits whatever fixed advisories are outstanding against :10.0, and the Trivy gate in CI fails on them.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS runtime
RUN apt-get update \
    && apt-get upgrade -y \
    && apt-get install -y --no-install-recommends postgresql-client curl jq ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /migrate
COPY --from=build /out/efbundle ./efbundle
COPY deploy/docker/migrate/migrate.sh deploy/docker/migrate/create-roles.sql deploy/docker/migrate/grant-roles.sql ./
RUN chmod 0555 ./efbundle ./migrate.sh
USER app
ENTRYPOINT ["./migrate.sh"]
