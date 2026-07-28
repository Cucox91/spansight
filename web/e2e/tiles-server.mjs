// A range-request static server for one directory, for the tiles-mode e2e variant only.
//
//   node e2e/tiles-server.mjs <dir> [port]
//
// Two things it does that a convenience server does not, and both are the point:
//
//   1. It answers HTTP Range with 206 and a Content-Range. `python3 -m http.server` ignores Range
//      and returns 200 with the whole file — and pmtiles v3 has a whole-file fallback under 26 MB,
//      so the fixture archive would render perfectly while the range path production depends on
//      stayed completely untested.
//   2. It runs on its own origin and answers the CORS preflight, because `Range` is not a
//      CORS-safelisted request header: a cross-origin PMTiles read is always preflighted, and
//      getting that wrong is the classic way this breaks in production.
//
// The header set mirrors infra/modules/storage.bicep — keep the two side by side. If the Bicep
// rules change and these do not, this server stops reproducing what the demo actually does.

import { createReadStream, statSync } from 'node:fs'
import { createServer } from 'node:http'
import { join, normalize } from 'node:path'

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

const server = createServer((req, res) => {
  if (req.method === 'OPTIONS') {
    res.writeHead(204, CORS)
    res.end()
    return
  }

  // normalize collapses any ../ before it is joined, so a request cannot walk out of <dir>.
  const path = join(dir, normalize(new URL(req.url, 'http://x').pathname).replace(/^(\.\.[/\\])+/, ''))

  let size
  try {
    size = statSync(path).size
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
    createReadStream(path).pipe(res)
    return
  }

  // An open-ended suffix range (`bytes=-500`) means the last N bytes; pmtiles does not use it,
  // but answering it wrongly would be a silent corruption rather than an error.
  const [, rawStart, rawEnd] = range
  const start = rawStart === '' ? Math.max(0, size - Number(rawEnd)) : Number(rawStart)
  // Clamped, not rejected. pmtiles opens by asking for the first 16 KB of the archive regardless
  // of how big it is, and the fixture archive is 10 KB — RFC 7233 says an end past the last byte
  // is satisfied by the last byte, and Blob storage does exactly that. Returning 416 here made
  // the whole variant fail against a small archive while passing against a large one.
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
  createReadStream(path, { start, end }).pipe(res)
})

server.listen(Number(port), '127.0.0.1', () => {
  console.log(`tiles-server: ${dir} on http://127.0.0.1:${port}`)
})
