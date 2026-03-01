# unraid-discord-doker-games-server-manager
[![Build & Publish](https://img.shields.io/github/actions/workflow/status/alexandru360/unraid-discord-doker-games-server-manager/docker-publish.yml?branch=main&label=CI%2FCD)](https://github.com/alexandru360/unraid-discord-doker-games-server-manager/actions/workflows/docker-publish.yml)
[![Docker Hub](https://img.shields.io/docker/v/alex360/unraid-discord-docker-manager?logo=docker&label=image)](https://hub.docker.com/repository/docker/alex360/unraid-discord-docker-manager/general)
[![Docker Pulls](https://img.shields.io/docker/pulls/alex360/unraid-discord-docker-manager?logo=docker)](https://hub.docker.com/repository/docker/alex360/unraid-discord-docker-manager/general)

Docker games server manager: a Discord bot that can start/stop game containers and monitor player events over Docker logs.

## How the Docker pipeline works
- Build: verifies and compiles the .NET 8 solution (Release).
- Test: runs unit tests on the build output.
- Publish: if the push is on `main` and the tests have passed, the image is built from the Dockerfile (without including the test project) and pushed to Docker Hub as `latest` and `sha-<commit>`.

## How to run the image
1) Create an `appsettings.Production.json` file or use environment variables for configuration.
2) Mount the Docker socket and a volume for data (DB/config).
3) Start the container using the `latest` tag (or `sha-...` for a pinned version).

Quick example:
```bash
docker run -d \
	--name discord-docker-manager \
	--restart unless-stopped \
	-v /var/run/docker.sock:/var/run/docker.sock \
	-v /opt/discord-docker-manager/data:/app/data \
	-e Discord__Token="YOUR_DISCORD_TOKEN" \
	-e Discord__GuildId=123456789012345678 \
	-e Database__ConnectionString="Data Source=/app/data/gamemanager.db" \
	alex360/unraid-discord-docker-manager:latest
```

Useful environment variables (prefixed per .NET options convention):
- `Discord__Token`: bot token (required).
- `Discord__GuildId`: optional, for instant slash-command registration.
- `Docker__Endpoint`: default `unix:///var/run/docker.sock`.
- `Database__ConnectionString`: default `Data Source=gamemanager.db` (you can point this to a file inside a mounted volume).
- `Ollama__Enabled`, `Ollama__BaseUrl`, `Ollama__Model`: for Ollama integration (optional).

## Installing on Unraid
- In the Unraid UI, go to Apps → Add Container → New Template.
- Image: `alex360/unraid-discord-docker-manager:latest`.
- Volume mappings:
	- `/var/run/docker.sock` → `/var/run/docker.sock` (read/write) to control containers.
	- `/mnt/user/appdata/discord-docker-manager` → `/app/data` for DB and config.
- Environment vars: set at least `Discord__Token`; optionally `Discord__GuildId`, `Docker__Endpoint`, `Database__ConnectionString`, `Ollama__*`.
- Network: `bridge` is sufficient; if your game containers are on a different network, adjust accordingly.
- Save and start the container; check the logs for confirmation and any connection errors to Discord or Docker.
