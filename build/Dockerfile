# syntax=docker/dockerfile:1
#
# Build context is the repo root. Two targets, same shape as altered-bga-api's:
#   docker build -f build/Dockerfile -t gameapi .
#   docker build -f build/Dockerfile --target migrations -t gameapi-migrations .
#
# `app`: the consolidation worker + read API. Expects ConnectionStrings__gameapidb,
# AlteredBgaApi__BaseUrl, AlteredBgaApi__ApiKey, ApiKeys__GameApi.
#
# `migrations` is a one-shot container that applies EF Core migrations and
# exits. The compiled-in connection string comes from
# GameApiDbContextDesignFactory (localhost dev default) and is meant to be
# overridden at runtime:
#   docker run --rm gameapi-migrations --connection "Host=postgres;..."

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS restore
WORKDIR /src
COPY GameApi.csproj ./
RUN dotnet restore GameApi.csproj
COPY . .

FROM restore AS publish
RUN dotnet publish GameApi.csproj \
      -c Release \
      -o /app/publish \
      /p:UseAppHost=false

FROM restore AS migrations-build
ENV PATH="${PATH}:/root/.dotnet/tools"
RUN dotnet tool install --global dotnet-ef --version 10.* \
 && dotnet ef migrations bundle \
      --project GameApi.csproj \
      --configuration Release \
      --output /app/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS migrations
WORKDIR /app
COPY --from=migrations-build /app/efbundle .
ENTRYPOINT ["./efbundle"]

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS app
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "GameApi.dll"]
