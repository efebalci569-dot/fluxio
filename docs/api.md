# Local HTTP API (v1)

Served by the Windows app on `http://<LAN IP>:<port>` (default port 47800) only while a session is open. JSON uses camelCase; enums are camelCase strings. Implemented in `src/Fluxio.Transfer/Hosting/ApiEndpoints.cs`; TypeScript types in `src/Fluxio.Web/src/types/api.ts`.

## Conventions

- **Authentication:** `Authorization: Bearer <token>` (token from `/pair`). No cookies.
- **Same origin only:** `Host` must be the session address; writes with a foreign `Origin` and requests with `Sec-Fetch-Site` other than `same-origin`/`none` are rejected.
- **Errors:** `{ "error": { "code": "...", "message": "...", "received": 123?, "reason": "...?" } }`

| HTTP | codes |
|---|---|
| 400 | `badRequest`, `invalidLink` |
| 401 | `unauthorized` |
| 403 | `deviceNotApproved`, `forbiddenOrigin`, `permissionDenied` |
| 404 | `notFound`, `sessionNotFound` |
| 409 | `invalidState`, `offsetMismatch` (+`received`), `alreadyPaired`, `busy`, `incomplete` (+`received`), `sourceChanged` |
| 410 | `sessionExpired`, `sessionClosed` (+`reason`), `cancelled`, `declined` |
| 413 | `tooLarge`, `tooManyFiles` |
| 421 | `forbiddenOrigin` (wrong Host) |
| 422 | `integrityFailed` |
| 429 | `rateLimited` |
| 503 | `notReady` |
| 507 | `insufficientStorage` |

`reason` values: `closedByUser, replaced, pairingExpired, approvalExpired, idleTimeout, deviceRejected, deviceLeft, networkLost, appShutdown`.

## Endpoints

| Method & path | Auth | Purpose |
|---|---|---|
| `GET /` | – | Phone page (embedded) |
| `GET /api/v1/ping` | – | `{ app: "fluxio", api: 1 }` |
| `POST /api/v1/pair` | – | `{ sessionId, secret, deviceName }` → `{ token, state }`. Rate-limited. |
| `GET /api/v1/state?since=V&wait=S` | token (approval not required) | Phone state; with `since`+`wait` it long-polls up to 25 s until the version changes. |
| `POST /api/v1/leave` | token | Phone disconnects; session ends. |
| `POST /api/v1/uploads` | approved | `{ files: [{ name, size, type }] }` → batch (`awaitingApproval` or `inProgress`). |
| `GET /api/v1/uploads/{b}/files/{f}` | approved | `{ received, state }` – resume position. |
| `PUT /api/v1/uploads/{b}/files/{f}?offset=N` | approved | Raw bytes (≤ 16 MB, `Content-Length` required); `offset` must equal `received`. |
| `POST /api/v1/uploads/{b}/files/{f}/complete` | approved | `{ sha256 }` → final file DTO (`completed`/`skipped`) or 422. |
| `DELETE /api/v1/uploads/{b}[/files/{f}]` | approved | Cancel batch / file. |
| `POST /api/v1/offers/{b}/accept` · `/decline` | approved | Answer an offer from the computer. |
| `GET /api/v1/offers/{b}/files/{f}/content` | approved | File stream (`attachment`, `Accept-Ranges: bytes`, single `Range` supported). |
| `GET /api/v1/offers/{b}/files/{f}/sha256?wait=S` | approved | `{ ready, sha256 }` of the source. |
| `POST /api/v1/offers/{b}/files/{f}/confirm` | approved | `{ sha256 }` of what the phone received → completed + verified, or 422. |
| `POST /api/v1/offers/{b}/files/{f}/ticket` | approved | `{ url: "/dl/<ticket>" }` single-use, 2 minutes, for the browser's download manager. |
| `DELETE /api/v1/offers/{b}[/files/{f}]` | approved | Cancel batch / file. |
| `GET /dl/{ticket}` | ticket | File stream for the browser's download manager (completes without verification). |

## Phone state

```json
{
  "version": 12,
  "status": "connected",
  "closeReason": null,
  "computerName": "DESKTOP-PC",
  "matchCode": "4821",
  "approved": true,
  "limits": { "maxFileSize": 107374182400, "maxFilesPerBatch": 500, "chunkSize": 8388608, "inPageDownloadLimit": 1073741824 },
  "offers":  [ { "id": "…", "status": "awaitingApproval", "totalBytes": 0, "transferredBytes": 0, "files": [ … ] } ],
  "uploads": [ … ]
}
```

File DTO: `{ id, name, size, state, transferred, failure, verified, savedName }` with `state` ∈ `pending, inProgress, verifying, completed, failed, cancelled, skipped` and batch `status` ∈ `awaitingApproval, inProgress, finished, declined, cancelled`.
