# Self-contained, single-file, trimmed linux-musl-x64 build of mssql-mcp.
# Linux uses the managed SNI implementation, so redistribution under MIT is
# clean per ADR-0002.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/mssql-mcp/mssql-mcp.csproj -r linux-musl-x64 \
        -p:SelfContained=true -p:PublishSingleFile=true -p:PublishTrimmed=true \
        --lock-file-path packages.linux-musl-x64.lock.json --locked-mode && \
    dotnet publish src/mssql-mcp -c Release -r linux-musl-x64 --self-contained true --no-restore \
        -p:PublishSingleFile=true -p:PublishTrimmed=true -o /app

# SqlClient requires non-invariant globalization; the extra image supplies ICU.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine-extra-amd64@sha256:93d2654c3612fd279106fd7d824742e1bb634b6ebe6bd967129d677309b69c46
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
