# unraid-discord-doker-games-server-manager
[![Build & Publish](https://img.shields.io/github/actions/workflow/status/alexandru360/unraid-discord-doker-games-server-manager/docker-publish.yml?branch=main&label=CI%2FCD)](https://github.com/alexandru360/unraid-discord-doker-games-server-manager/actions/workflows/docker-publish.yml)
[![Docker Hub](https://img.shields.io/docker/v/alex360/unraid-discord-docker-manager?logo=docker&label=image)](https://hub.docker.com/repository/docker/alex360/unraid-discord-docker-manager/general)
[![Docker Pulls](https://img.shields.io/docker/pulls/alex360/unraid-discord-docker-manager?logo=docker)](https://hub.docker.com/repository/docker/alex360/unraid-discord-docker-manager/general)

Docker games server manager: a Discord bot that can start/stop game containers and monitor player events over Docker logs.

## How the Docker pipeline works
- Build: verifies and compiles the .NET 8 solution (Release).
- Test: runs unit tests on the build output.
- Publish: if the push is on `main` and the tests have passed, the image is built from the Dockerfile (without including the test project) and pushed to Docker Hub as `latest` and `sha-<commit>`.

## Cum rulezi imaginea
1) Creează un fișier `appsettings.Production.json` sau folosește variabile de mediu pentru configurare.
2) Montează socket-ul Docker și un volum la `/data` pentru baza de date/config.
3) Pornește containerul folosind tag-ul `latest` (sau `sha-...` pentru o versiune fixă).

Quick example:
```bash
docker run -d \
	--name discord-docker-manager \
	--restart unless-stopped \
	-v /var/run/docker.sock:/var/run/docker.sock \
	-v /opt/discord-docker-manager/data:/data \
	-e Discord__Token="YOUR_DISCORD_TOKEN" \
	-e Discord__GuildId=123456789012345678 \
	-e Database__ConnectionString="Data Source=/data/gamemanager.db" \
	alex360/unraid-discord-docker-manager:latest
```

Useful environment variables (prefixed per .NET options convention):
- `Discord__Token`: bot token (required).
- `Discord__GuildId`: optional, for instant slash-command registration.
- `Docker__Endpoint`: default `unix:///var/run/docker.sock`.
- `Database__ConnectionString`: default `Data Source=/data/gamemanager.db` (în volumul montat; poți schimba după nevoie).
- `Ollama__Enabled`, `Ollama__BaseUrl`, `Ollama__Model`: pentru integrarea Ollama (opțional).

## Installing on Unraid
- In the Unraid UI, go to Apps → Add Container → New Template.
- Image: `alex360/unraid-discord-docker-manager:latest`.
- Volume mappings:
	- `/var/run/docker.sock` → `/var/run/docker.sock` (read/write) pentru a controla containerele.
	- `/mnt/user/appdata/discord-docker-manager` → `/data` pentru DB și config.
- Environment vars: setează cel puțin `Discord__Token`; opțional `Discord__GuildId`, `Docker__Endpoint`, `Database__ConnectionString`, `Ollama__*`.
- Network: `bridge` e suficient; dacă ai containere pe altă rețea, adaptează după nevoie.
- Salvează și pornește containerul; verifică log-urile pentru confirmare și pentru eventuale erori de conectare la Discord sau Docker.
