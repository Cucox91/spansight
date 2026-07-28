// A range-request static server for one directory, for the tiles-mode e2e variant only.
//
//   node e2e/tiles-server.mjs <dir> [port]
//
// Two things it does that a convenience server does not, and both are the point:
//
//   1. It answers HTTP Range with 206 and a Content-Range. `python3 -m http.server` ignores Range
//      and returns 200 with the whole file; pmtiles 3.2.1 then aborts and throws — its
//      FetchSource.getBytes rejects a 200 whose Content-Length exceeds the requested length with
//      "Check that your storage backend supports HTTP Byte Serving" — so the map renders nothing
//      at all. Byte serving is not an optimisation here; without it there is no map.
//   2. It runs on its own origin and answers the CORS preflight, because `Range` is not a
//      CORS-safelisted request header: a cross-origin PMTiles read is always preflighted, and
//      getting that wrong is the classic way this breaks in production.
//
// The header set mirrors infra/modules/storage.bicep — keep the two side by side. If the Bicep
// rules change and these do not, this server stops reproducing what the demo actually does.

import { createReadStream, realpathSync, statSync } from 'node:fs'
import { createServer } from 'node:http'
import { join, normalize, sep } from 'node:path'

const [dir, port = '8081'] = process.argv.slice(2)
if (!dir) {
  console.error('usage: node e2e/tiles-server.mjs <dir> [port]')
  process.exit(2)
}

// infra/modules/storage.bicep corsRules
const CORS = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'GET, HEAD, OPTIONS',
  'Access-Control-Allow-Headers': 'Range, If-Match, If-None-Match',
  'Access-Control-Expose-Headers': 'Content-Range, Content-Length, Accept-Ranges, ETag',
  'Access-Control-Max-Age': '3600',
}

/** Pipe a file stream, and destroy the response rather than the process if the read fails. */
function send(stream, res) {
  stream.on('error', () => res.destroy())
  stream.pipe(res)
}

const server = createServer((req, res) => {
  if (req.method === 'OPTIONS') {
    res.writeHead(204, CORS)
    res.end()
    return
  }

  // A request target beginning with `//` is protocol-relative, and `new URL('//', base)` throws.
  // The throw would be synchronous inside this listener, which Node does not catch — it would take
  // the whole server down mid-suite rather than return a status.
  let pathname
  try {
    pathname = new URL(req.url, 'http://localhost').pathname
  } catch {
    res.writeHead(400, CORS)
    res.end('bad request target')
    return
  }

  const requested = join(dir, normalize(pathname).replace(/^(\.\.[/\\])+/, ''))

  // normalize() handles textual `../`, but not a symlink pointing out of the directory, so the
  // resolved path is compared against the resolved root. And only a regular file is served:
  // statSync succeeds on a directory, and piping a createReadStream over one emits EISDIR — an
  // unhandled 'error' event, which ends the process.
  let path
  let size
  try {
    const root = realpathSync(dir)
    path = realpathSync(requested)
    if (path !== root && !path.startsWith(root + sep)) {
      res.writeHead(403, CORS)
      res.end('outside the served directory')
      return
    }
    const stat = statSync(path)
    if (!stat.isFile()) {
      res.writeHead(404, CORS)
      res.end('not a file')
      return
    }
    size = stat.size
  } catch {
    res.writeHead(404, CORS)
    res.end('not found')
    return
  }

  const base = { ...CORS, 'Accept-Ranges': 'bytes', 'Content-Type': 'application/octet-stream' }
  const range = /^bytes=(\d*)-(\d*)$/.exec(req.headers.range ?? '')

  if (!range) {
    res.writeHead(200, { ...base, 'Content-Length': size })
    if (req.method === 'HEAD') {
      res.end()
      return
    }
    send(createReadStream(path), res)
    return
  }

  // An open-ended suffix range (`bytes=-500`) means the last N bytes; pmtiles does not use it,
  // but answering it wrongly would be a silent corruption rather than an error.
  const [, rawStart, rawEnd] = range
  const start = rawStart === '' ? Math.max(0, size - Number(rawEnd)) : Number(rawStart)
  // Clamped, not rejected. pmtiles opens by asking for the first 16 KB of the archive regardless
  // of how big it is, and the fixture archive is 10 KB — RFC 7233 says an end past the last byte
  // is satisfied by the last byte, and Blob storage does exactly that. (pmtiles does recover from
  // a 416 at offset 0 by re-reading the true length out of `Content-Range`, so the app survived
  // the first draft of this server; what failed was the mode guard's 206 assertion, which is how
  // it was caught.)
  const end = rawEnd === '' || rawStart === '' ? size - 1 : Math.min(Number(rawEnd), size - 1)

  if (start >= size || start < 0 || start > end) {
    res.writeHead(416, { ...base, 'Content-Range': `bytes */${size}` })
    res.end()
    return
  }

  res.writeHead(206, {
    ...base,
    'Content-Range': `bytes ${start}-${end}/${size}`,
    'Content-Length': end - start + 1,
  })
  if (req.method === 'HEAD') {
    res.end()
    return
  }
  send(createReadStream(path, { start, end }), res)
})

server.listen(Number(port), '127.0.0.1', () => {
  console.log(`tiles-server: ${dir} on http://127.0.0.1:${port}`)
})
