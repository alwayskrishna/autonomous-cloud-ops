# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/AutonomousCloudOps.Core/AutonomousCloudOps.Core.csproj src/AutonomousCloudOps.Core/
COPY src/AutonomousCloudOps.Api/AutonomousCloudOps.Api.csproj src/AutonomousCloudOps.Api/
RUN dotnet restore src/AutonomousCloudOps.Api/AutonomousCloudOps.Api.csproj
COPY src/ src/
RUN dotnet publish src/AutonomousCloudOps.Api/AutonomousCloudOps.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/data && chown -R app:app /app
USER app
ENV ASPNETCORE_URLS=http://127.0.0.1:8080
ENV IncidentDatabasePath=/app/data/incidents.db
EXPOSE 8080
ENTRYPOINT ["dotnet", "AutonomousCloudOps.Api.dll"]
