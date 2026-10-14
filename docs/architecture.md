# Architecture

## Components

```
┌──────────────────────── Windows PC ────────────────────────┐        ┌──────── Phone ────────┐
│ Fluxio.Desktop (WPF, MVVM)                                  │        │ Browser               │
│   Views ─ ViewModels ─ Services (settings, theme, i18n,     │        │  Fluxio.Web (React)   │
│   notifications, firewall/network diagnostics, logging)    │        │  - pairing            │
│                 │ uses                                      │  HTTP  │  - long-poll state    │
│ Fluxio.Transfer (class library)                             │◀──────▶│  - chunked uploads    │
│   SessionManager ─ sessions, pairing, authorization         │  LAN   │  - verified downloads │
│   LocalTransferServer (Kestrel, one LAN address, per session)│       └───────────────────────┘
│   ApiEndpoints, RequestGuardMiddleware, WebUi (embedded)    │
│   IncomingStore (.part files, finalize), HistoryStore (SQLite)
└─────────────────────────────────────────────────────────────┘
Fluxio.Landing: static website, deployed separately (Vercel). Not involved in transfers.
```

- **Fluxio.Transfer** has no UI dependency, so the whole protocol is covered by integration tests that run a real Kestrel server on 127.0.0.1 (`tests/`).
- **Fluxio.Web** is built by Vite and embedded into `Fluxio.Transfer.dll` as resources. The page and the API share one origin, so there is no CORS at all.
- **Fluxio.Desktop** hosts the engine in-process. Engine events arrive on background threads and are marshalled to the WPF dispatcher; progress is refreshed 4×/s from the engine's counters.

## Session lifecycle

```
Preparing → AwaitingScan ──scan──▶ AwaitingApproval ──approve──▶ Connected ──▶ Closed
               │ (QR window)          │ (approval window)            │ (idle timeout, user, phone left,
               └──────▶ Expired ◀─────┘                              │  new code, network lost, app exit)
```

- Choosing **Send Files** or **Receive Files** creates a session if none is open. A new QR code always ends the previous session.
- The HTTP service starts with the session and stops when it ends. Before stopping, waiting phones get the final status (long poll).
- One session = one phone. The UI shows "transfer in progress" while batches run; there is no separate session state for it.

## Network selection

`NetworkSelector` lists up interfaces with private IPv4 addresses (RFC 1918), ranks physical adapters with a gateway first and VPN/virtual adapters last, and the user can pin an adapter in Settings. Kestrel binds only that address (never `0.0.0.0`). Ports 47800–47819 are tried in order.

## Transfers

### Phone → computer

1. `POST /uploads` with names and sizes → a batch awaiting approval (or accepted immediately if the user disabled incoming approval). Free disk space is checked.
2. The computer user accepts (choosing the folder and the conflict policy) or declines.
3. The phone reads each file in 8 MB chunks, hashes it with SHA-256 (pure JS, since `crypto.subtle` is unavailable over HTTP) while uploading the previous chunk, and sends `PUT …?offset=N`.
4. The server appends to `<folder>\.fluxio-incoming\<id>.part`, hashing exactly the bytes it wrote. After a network error the phone asks for the stored offset and resumes.
5. `POST …/complete` with the phone's hash. Only if it matches the server's hash is the part file moved to its final name (same volume → atomic rename), the conflict policy applied and the Mark of the Web added.

### Computer → phone

1. The computer offers selected files; SHA-256 of each source is computed in the background.
2. The phone accepts or declines.
3. Files up to 1 GB (512 MB on iOS) are fetched by the page with `fetch` streaming (resumable with `Range`), hashed while arriving and collected in 16 MB Blob segments. `POST …/confirm` sends the hash; the computer compares it with the source hash. Then the page hands the Blob to the browser to save.
4. Larger files use a single-use 2-minute link (`/dl/<ticket>`) so the browser's download manager can save them without loading them into page memory. Progress comes from the computer; the page cannot verify these files and says so.
5. If a source file changes after it was offered (size or modification time), the transfer fails with `sourceChanged` instead of sending inconsistent data.

### Cancellation

Either side can cancel one file or the whole batch. Unfinished files stop, partial data is deleted, finished files are kept. A file in its final verification step cannot be cancelled any more.

## Storage

- Settings: `%APPDATA%\Fluxio\settings.json`
- History: `%LOCALAPPDATA%\Fluxio\history.db` (SQLite; names, sizes, dates, outcomes, phone description — no contents or paths)
- Logs: `%LOCALAPPDATA%\Fluxio\logs` (7 days; no tokens, secrets, contents or paths)
- Phone: optional history in that browser's `localStorage`; the session token in `sessionStorage` so a page reload can continue the session.

## Future work (not implemented)

Transfers across networks (signalling server + WebRTC, optional end-to-end-encrypted relay with clear disclosure), native mobile apps, code signing and a verified auto-update channel. The engine keeps transport concerns in `LocalTransferServer`/`ApiEndpoints` so another transport can be added beside it.
