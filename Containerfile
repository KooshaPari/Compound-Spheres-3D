# =============================================================================
# Compound-Spheres-3D — Podman / OCI Containerfile
# =============================================================================
# Multi-stage image for CI builds and SBOM generation.
# Use:  podman build -t compound-spheres-ci .
#       podman run --rm compound-spheres-ci
#
# Builds CompoundMeshes.sln in Release mode.
# Test projects (net8.0 xunit) run as part of the CI pipeline.
# =============================================================================

FROM mcr.microsoft.com/devcontainers/dotnet:8.0 AS builder

WORKDIR /src

# Copy solution and project files first (layer caching)
COPY CompoundMeshes.sln ./
COPY CompoundMeshes/CompoundMeshes.csproj CompoundMeshes/
COPY Tests/CompoundMeshes.Tests/CompoundMeshes.Tests.csproj Tests/CompoundMeshes.Tests/

# Restore dependencies
RUN dotnet restore CompoundMeshes.sln

# Copy source
COPY . .

# Build
RUN dotnet build CompoundMeshes.sln -c Release --no-restore

# Test stage
FROM builder AS test
RUN dotnet test Tests/CompoundMeshes.Tests/ -c Release --no-build --verbosity normal

# Final stage — just the built artifacts
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS final
WORKDIR /app
COPY --from=builder /src/CompoundMeshes/bin/Release/net4.8/ ./
