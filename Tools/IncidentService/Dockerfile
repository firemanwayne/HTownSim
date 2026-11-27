# The incident service (owns the incident lifecycle: created, assigned, handled, cleared) for the city server container.
# Build context is Tools/IncidentService (see Tools/CityServer/compose.yaml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Api/IncidentService.Api.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "IncidentService.Api.dll"]
