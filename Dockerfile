# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY src/DiscordDockerManager/DiscordDockerManager.csproj src/DiscordDockerManager/
RUN dotnet restore src/DiscordDockerManager/DiscordDockerManager.csproj

COPY src/ src/
RUN dotnet publish src/DiscordDockerManager/DiscordDockerManager.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "DiscordDockerManager.dll"]
