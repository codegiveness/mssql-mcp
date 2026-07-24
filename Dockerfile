# Self-contained, single-file, trimmed linux-x64 build of mssql-mcp.
# Linux uses the managed SNI implementation, so redistribution under MIT is
# clean per ADR-0002.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/mssql-mcp/mssql-mcp.csproj && \
    dotnet publish src/mssql-mcp -c Release -r linux-x64 --self-contained true \
        -p:PublishSingleFile=true -p:PublishTrimmed=true -o /app

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine-amd64
LABEL maintainer="codegiveness" \
      source="https://github.com/codegiveness/mssql-mcp"
WORKDIR /app
COPY --from=build /app/mssql-mcp .
RUN chmod +x ./mssql-mcp
ENTRYPOINT ["./mssql-mcp"]
