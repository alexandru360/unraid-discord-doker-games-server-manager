# unraid-discord-doker-games-server-manager
[![Build & Publish](https://img.shields.io/github/actions/workflow/status/alex360/unraid-discord-doker-games-server-manager/docker-publish.yml?branch=main&label=CI%2FCD)](https://github.com/alex360/unraid-discord-doker-games-server-manager/actions/workflows/docker-publish.yml)
[![Docker Hub](https://img.shields.io/docker/v/alex360/unraid-discord-docker-manager?logo=docker&label=image)](https://hub.docker.com/r/alex360/unraid-discord-docker-manager)
[![Docker Pulls](https://img.shields.io/docker/pulls/alex360/unraid-discord-docker-manager?logo=docker)](https://hub.docker.com/r/alex360/unraid-discord-docker-manager)

Doker games server manager: a Discord bot that can start/stop game containers and monitor player events over Docker logs.

## Cum funcționează pipeline-ul Docker
- Build: verifică și compilează soluția .NET 8 (Release).
- Test: rulează testele unitare pe output-ul de build.
- Publish: dacă push-ul este pe `main` și testele au trecut, imaginea se construiește din Dockerfile (fără a include proiectul de teste) și se împinge în Docker Hub ca `latest` și `sha-<commit>`.

## Cum rulezi imaginea
1) Creează un fișier `appsettings.Production.json` sau folosește variabile de mediu pentru configurare.
2) Montează socket-ul Docker și un volum pentru date (DB/config).
3) Pornește containerul folosind tag-ul `latest` (sau `sha-...` pentru o versiune fixă).

Exemplu rapid:
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

Variabile utile (prefixate conform opțiunilor .NET):
- `Discord__Token`: token-ul botului (obligatoriu).
- `Discord__GuildId`: opțional, pentru înregistrarea instantă a slash-commands.
- `Docker__Endpoint`: default `unix:///var/run/docker.sock`.
- `Database__ConnectionString`: default `Data Source=gamemanager.db` (poți indica un fișier într-un volum montat).
- `Ollama__Enabled`, `Ollama__BaseUrl`, `Ollama__Model`: pentru integrarea Ollama (opțional).

## Instalare pe Unraid
- În UI-ul Unraid, Apps → Add Container → Template nou.
- Image: `alex360/unraid-discord-docker-manager:latest`.
- Volume mappings:
	- `/var/run/docker.sock` → `/var/run/docker.sock` (read/write) pentru a controla containerele.
	- `/mnt/user/appdata/discord-docker-manager` → `/app/data` pentru DB și config.
- Environment vars: setează cel puțin `Discord__Token`; opțional `Discord__GuildId`, `Docker__Endpoint`, `Database__ConnectionString`, `Ollama__*`.
- Network: `bridge` e suficient; dacă ai containere pe altă rețea, adaptează după nevoie.
- Salvează și pornește containerul; verifică log-urile pentru confirmare și pentru eventuale erori de conectare la Discord sau Docker.
