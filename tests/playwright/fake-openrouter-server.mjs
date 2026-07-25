import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const port = Number.parseInt(process.env.FAKE_OPENROUTER_PORT ?? '5124', 10);
const delayMs = Number.parseInt(process.env.FAKE_OPENROUTER_DELAY_MS ?? '20', 10);
const fixturePath = fileURLToPath(
  new URL('./fixtures/openrouter-reasoning-stream.sse', import.meta.url),
);
const reasoningStream = await readFile(fixturePath, 'utf8');

const models = {
  data: [
    {
      id: 'test/reasoner',
      name: 'Test Reasoner',
      context_length: 32768,
      created: 1753488000,
      architecture: { modality: 'text->text' },
      pricing: { prompt: '0', completion: '0' },
      supported_parameters: ['reasoning'],
    },
    {
      id: 'test/basic',
      name: 'Test Basic',
      context_length: 8192,
      created: 1753488000,
      architecture: { modality: 'text->text' },
      pricing: { prompt: '0', completion: '0' },
      supported_parameters: [],
    },
  ],
};

function json(response, statusCode, body) {
  response.writeHead(statusCode, { 'content-type': 'application/json' });
  response.end(JSON.stringify(body));
}

async function readJson(request) {
  let body = '';
  for await (const chunk of request) {
    body += chunk;
  }
  return JSON.parse(body);
}

async function writeSse(response) {
  response.writeHead(200, {
    'cache-control': 'no-cache',
    connection: 'keep-alive',
    'content-type': 'text/event-stream; charset=utf-8',
  });

  for (const event of reasoningStream.split(/(?<=\n\n)/)) {
    if (!event) continue;
    response.write(event);
    await new Promise((resolve) => setTimeout(resolve, delayMs));
  }
  response.end();
}

const server = createServer(async (request, response) => {
  const url = new URL(request.url, `http://${request.headers.host}`);

  if (request.method === 'GET' && url.pathname === '/health') {
    response.writeHead(204);
    response.end();
    return;
  }

  if (request.method === 'GET' && url.pathname === '/models') {
    json(response, 200, models);
    return;
  }

  if (request.method === 'POST' && url.pathname === '/chat/completions') {
    const body = await readJson(request);
    const userMessages = body.messages?.filter((message) => message.role === 'user') ?? [];
    const latestUserMessage = userMessages.at(-1)?.content;

    if (latestUserMessage === 'trigger rate limit') {
      json(response, 429, { error: { message: 'Fake rate limit for E2E test' } });
      return;
    }

    if (latestUserMessage === 'second turn' && userMessages.length < 2) {
      json(response, 400, { error: { message: 'Missing prior user turn' } });
      return;
    }

    await writeSse(response);
    return;
  }

  json(response, 404, { error: { message: 'Not found' } });
});

server.listen(port, '127.0.0.1', () => {
  console.log(`Fake OpenRouter listening on http://127.0.0.1:${port}`);
});
