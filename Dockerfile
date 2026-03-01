# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY DiscordDockerManager.slnx ./
COPY src/DiscordDockerManager/DiscordDockerManager.csproj src/DiscordDockerManager/
COPY tests/DiscordDockerManager.Tests/DiscordDockerManager.Tests.csproj tests/DiscordDockerManager.Tests/
RUN dotnet restore

COPY . .
RUN dotnet publish src/DiscordDockerManager/DiscordDockerManager.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "DiscordDockerManager.dll"]
