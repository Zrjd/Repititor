# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Repetitor.Api/Repetitor.Api.csproj src/Repetitor.Api/
RUN dotnet restore src/Repetitor.Api/Repetitor.Api.csproj

COPY src/ src/
RUN dotnet publish src/Repetitor.Api/Repetitor.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1 \
    DOTNET_GLOBALIZATION_INVARIANT=1 \
    APP_UID=1001

RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

RUN mkdir -p /app/media && chown -R ${APP_UID}:${APP_UID} /app
USER ${APP_UID}

EXPOSE 8080
VOLUME ["/app/media"]

HEALTHCHECK --interval=30s --timeout=5s --start-period=45s --retries=3 \
  CMD curl -fsS http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "Repetitor.Api.dll"]
