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

## Config

Model and OpenRouter settings live in [`Agentic.Chat/appsettings.json`](Agentic.Chat/appsettings.json):

- **Model:** `openai/gpt-oss-120b`
- **Base URL:** `https://openrouter.ai/api/v1`
- **API key:** read only from `OPENROUTER_API_KEY`

## Memory (v1)

The full message list for the current Blazor circuit is sent with each LLM call. Assistant reasoning is streamed into a collapsible **Thinking** panel; the reply streams below it. History is lost when the process or circuit restarts. No vector store, SQLite, or durable cross-session memory.
