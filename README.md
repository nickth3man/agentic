# Agentic Chat

A Blazor chat client for OpenRouter with two deployment modes:

- `Agentic.Chat` is the existing Blazor Server app for local and Cloudflare
  tunnel use.
- `Agentic.Chat.Client` is a static Blazor WebAssembly app for GitHub Pages.
  It stores conversations in the browser and calls OpenRouter directly.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [cloudflared](https://github.com/cloudflare/cloudflared) on PATH
- Git Bash (Windows) or any bash shell (macOS/Linux)
- Environment variable `OPENROUTER_API_KEY` (never commit this; can be set as a Windows User env var — the script auto-loads it from the registry)

## Run the server app

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

## GitHub Pages

The Pages deployment uses Option A:

- Visitors can use a shared OpenRouter key with `openrouter/free` and explicit
  `:free` model variants.
- A visitor can enter a personal OpenRouter key to unlock the full model
  catalog. The default is session storage; “Remember on this device” opts into
  local storage.
- Personal keys are validated with OpenRouter before they are stored and are
  sent directly from the browser to OpenRouter.
- Conversations are stored locally in IndexedDB. No conversation or personal
  key is sent to GitHub.

### Required repository setup

1. Create a dedicated OpenRouter key for the public site. Do not reuse an
   account-management key or a key used by the server app.
2. On that key, try a `$0` [credit limit](https://openrouter.ai/docs/api_reference/limits)
   and configure an OpenRouter [guardrail](https://openrouter.ai/docs/guides/features/guardrails)
   whose model allowlist contains only `openrouter/free` and/or the explicit
   `:free` models you intend to offer. OpenRouter documents numeric key limits,
   but does not currently guarantee that an exact-zero limit permits
   zero-priced requests.
3. Smoke-test both sides before deployment: a free-model request must succeed
   and a paid-model request must be rejected. If the free request returns 402,
   the exact-zero limit is not usable for this account; the provider-side model
   allowlist remains the hard paid-model boundary. The client-side model filter
   is only user experience and can be bypassed.
4. In GitHub, open **Settings → Secrets and variables → Actions** and add the
   repository secret `OPENROUTER_FREE_API_KEY`.
5. Keep **Settings → Pages → Build and deployment → Source** set to
   **GitHub Actions**.
6. Push to `main`, or manually run the **Deploy GitHub Pages** workflow.

The workflow publishes the WebAssembly app, applies GitHub’s repository base
path, injects the shared key into the deployment artifact, and deploys it. The
key is not written to the repository, logs, or source maps.

> A GitHub Pages app is public static content. The shared key in the deployed
> `app-config.json` is therefore public and extractable by design. A spending
> limit alone is not sufficient protection; keep the OpenRouter model allowlist
> in place and treat rate-limit exhaustion as possible.

Run the Pages client locally (without a shared key):

```bash
dotnet run --project Agentic.Chat.Client
```

For a local shared-key test, temporarily put the dedicated key in
`Agentic.Chat.Client/wwwroot/app-config.json`. Never commit that value; restore
the checked-in empty string immediately afterward.

### Pages routing

The client includes `.nojekyll` and a `404.html` fallback so direct links such
as `/agentic/chat` return to the Blazor router. The Actions workflow derives the
correct `<base href>` from GitHub Pages, so the same workflow supports a
project site or a custom domain.

## Memory

The server app keeps the current transcript in its Blazor circuit, so history
is lost when the process or circuit restarts.

The Pages client stores conversations in IndexedDB on the current device.
Assistant reasoning streams into a collapsible **Thinking** panel in both
modes. There is no vector store or cross-device synchronization.
