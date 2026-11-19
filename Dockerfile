# Self-contained, single-file, trimmed linux-musl-x64 build of mssql-mcp.
# All platforms use managed SNI per ADR-0002. Restricted Entra broker and native
# Windows SNI assets are excluded from distribution.
FROM mcr.microsoft.com/dotnet/sdk:11.0.100@sha256:65f3c7c6a25a239ab3e6719f2896147e7b68242ff2d8818dd70498bde6ef8bea AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/mssql-mcp/mssql-mcp.csproj -r linux-musl-x64 \
        -p:SelfContained=true -p:PublishSingleFile=true -p:PublishTrimmed=true \
        --lock-file-path packages.linux-musl-x64.lock.json --locked-mode && \
    dotnet publish src/mssql-mcp -c Release -r linux-musl-x64 --self-contained true --no-restore \
        -p:PublishSingleFile=true -p:PublishTrimmed=true -o /app

# SqlClient requires non-invariant globalization; the extra image supplies ICU.
FROM mcr.microsoft.com/dotnet/runtime-deps:11.0.0-alpine3.24-extra-amd64@sha256:f8a5a280f1b741cfc311308fc5968579797008470248489fcc3bdf9e789f1e1c
LABEL maintainer="codegiveness" \
      source="https://github.com/codegiveness/mssql-mcp"
WORKDIR /app
COPY --from=build /app/mssql-mcp .
RUN chmod +x ./mssql-mcp && \
    addgroup --system --gid 1001 mssqlmcp && \
    adduser --system --uid 1001 --ingroup mssqlmcp --no-create-home mssqlmcp && \
    chown -R mssqlmcp:mssqlmcp /app
USER mssqlmcp
ENTRYPOINT ["./mssql-mcp"]
