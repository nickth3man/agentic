# Agentic Chat

Minimal Blazor chat agent that talks to OpenRouter with an in-memory rolling conversation transcript.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Environment variable `OPENROUTER_API_KEY` (never commit this)

## Run

```bash
export OPENROUTER_API_KEY=sk-or-v1-...   # Windows Git Bash / macOS / Linux
# or: set OPENROUTER_API_KEY=sk-or-v1-...  # Windows cmd
# or: $env:OPENROUTER_API_KEY="sk-or-v1-..."  # PowerShell

dotnet run --project Agentic.Chat
```

Open the URL shown in the console (typically `https://localhost:7xxx`) and use the chat page.

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

1. In PowerShell, set your API key and start the chat on HTTP (leave this window open):

   ```powershell
   $env:OPENROUTER_API_KEY="sk-or-v1-..."
   dotnet run --project Agentic.Chat --launch-profile http
   ```

   Wait until it is listening on `http://localhost:5123`.

2. Open a **second** PowerShell window and start the tunnel:

   ```powershell
   cloudflared tunnel --url http://localhost:5123
   ```

3. Copy the printed URL that looks like `https://….trycloudflare.com`.

### On the phone

1. Use any internet connection (cellular or Wi‑Fi — **not** required to match the PC’s network).
2. Open Safari (iPhone) or Chrome (Android).
3. Paste the `https://….trycloudflare.com` URL and go.
4. Use the chat as usual.
5. If it fails to load or disconnects: confirm both PC windows are still running, then refresh. If you restarted the tunnel, use the **new** URL from the tunnel window.

### When you are done

1. In the tunnel window, press `Ctrl+C` so the phone link stops working.
2. Optionally stop the chat app the same way in the first window.
3. Do not leave the tunnel running unattended if you care about API spend.

## Config

Model and OpenRouter settings live in [`Agentic.Chat/appsettings.json`](Agentic.Chat/appsettings.json):

- **Model:** `openai/gpt-oss-120b`
- **Base URL:** `https://openrouter.ai/api/v1`
- **API key:** read only from `OPENROUTER_API_KEY`

## Memory (v1)

The full message list for the current Blazor circuit is sent with each LLM call. Assistant reasoning is streamed into a collapsible **Thinking** panel; the reply streams below it. History is lost when the process or circuit restarts. No vector store, SQLite, or durable cross-session memory.
