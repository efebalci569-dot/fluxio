// Minimal static file server for previewing the website (src/Fluxio.Landing).
// Usage: node scripts/serve-static.mjs [folder] [port]
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { extname, join, normalize, resolve, sep } from 'node:path';

const root = resolve(process.argv[2] ?? 'src/Fluxio.Landing');
const port = Number(process.argv[3] ?? 5175);
const types = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.txt': 'text/plain; charset=utf-8',
  '.json': 'application/json',
  '.woff2': 'font/woff2',
};

createServer(async (req, res) => {
  try {
    const url = new URL(req.url ?? '/', 'http://localhost');
    let path = normalize(join(root, decodeURIComponent(url.pathname)));
    if (!path.startsWith(root + sep) && path !== root) {
      res.writeHead(403).end();
      return;
    }

    if ((await stat(path).catch(() => null))?.isDirectory()) {
      path = join(path, 'index.html');
    }

    const body = await readFile(path).catch(() => null);
    if (!body) {
      res.writeHead(404, { 'Content-Type': types['.html'] }).end(await readFile(join(root, '404.html')).catch(() => 'Not found'));
      return;
    }

    res.writeHead(200, { 'Content-Type': types[extname(path)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' }).end(body);
  } catch {
    res.writeHead(500).end();
  }
}).listen(port, '127.0.0.1', () => console.log(`Serving ${root} on http://localhost:${port}`));
