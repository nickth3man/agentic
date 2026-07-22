# Agentic Chat

Minimal Blazor chat agent that talks to OpenRouter with an in-memory rolling conversation transcript.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [cloudflared](https://github.com/cloudflare/cloudflared) on PATH
- Git Bash (Windows) or any bash shell (macOS/Linux)
- Environment variable `OPENROUTER_API_KEY` (never commit this; can be set as a Windows User env var — the script auto-loads it from the registry)

## Run

```bash
bash start-phone.sh
```

The script starts the app on `http://localhost:5123` with hot reload enabled and
brings up a Cloudflare tunnel for phone access. It prints two URLs:
- Local: `http://localhost:5123/chat`
- Phone: `https://*.trycloudflare.com`

Edit any file — both browsers update live (Razor markup, C# method bodies, CSS).
Rude edits (`Program.cs`, new `.razor` file) restart the server; both browsers
auto-reload and the phone URL stays the same.

## Access from your phone (remote)

Your phone does not need to be on the same Wi‑Fi as your PC. This uses an **open-source** tool: [cloudflared](https://github.com/cloudflare/cloudflared) (Apache-2.0), Cloudflare’s Tunnel client.

### Plain English

The chat normally only works on your PC. A **tunnel** creates a temporary link from the internet to that PC. You run `cloudflared` on the PC; it prints a web address ending in `trycloudflare.com`. Open that address in your phone’s browser and you get the same chat.

- Your **PC must stay on**, and both the chat app and the tunnel must keep running.
- The link is like a shared key: **do not post it publicly**. Anyone with it can use the chat and spend your OpenRouter credits.
- When you stop the tunnel, the phone link stops working.

### On the PC (one-time setup)

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) if you have not already.
2. Install `cloudflared` (open source):
   - Windows (PowerShell): `winget install Cloudflare.cloudflared`
   - Or download from the [cloudflared releases](https://github.com/cloudflare/cloudflared/releases) / [install docs](https://developers.cloudflare.com/cloudflare-one/connections/connect-apps/install-and-setup/installation/).

### On the PC (every time you want phone access)

1. In Git Bash, run the single start script (leave the window open):

   ```bash
   bash start-phone.sh
   ```

2. Wait for the `PHONE LINK` line — copy the `https://….trycloudflare.com` URL.
3. Open it on your phone. Both the local browser and the phone update live when you edit files.

### On the phone

1. Use any internet connection (cellular or Wi‑Fi — **not** required to match the PC’s network).
2. Open Safari (iPhone) or Chrome (Android).
3. Paste the `https://….trycloudflare.com` URL and go.
4. Use the chat as usual.
5. If it fails to load or disconnects: confirm both PC windows are still running, then refresh. If you restarted the tunnel, use the **new** URL from the tunnel window.

### When you are done

1. In the Git Bash window, press `Ctrl+C` — stops both the app and the tunnel together.
2. Do not leave the tunnel running unattended if you care about API spend.

## Config

Model and OpenRouter settings live in [`Agentic.Chat/appsettings.json`](Agentic.Chat/appsettings.json):

- **Model:** `openai/gpt-oss-120b`
- **Base URL:** `https://openrouter.ai/api/v1`
- **API key:** read only from `OPENROUTER_API_KEY`

## Memory (v1)

The full message list for the current Blazor circuit is sent with each LLM call. Assistant reasoning is streamed into a collapsible **Thinking** panel; the reply streams below it. History is lost when the process or circuit restarts. No vector store, SQLite, or durable cross-session memory.
