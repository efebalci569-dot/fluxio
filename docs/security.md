# Security model

Fluxio assumes the local network may contain untrusted devices. Goals: only the phone the user approves can transfer anything; it can only access what the user chose to share; nothing is written outside the chosen folder; the user always knows the result.

## Threats and controls

| Threat | Control | Where |
|---|---|---|
| Another device guesses or reuses a pairing link | 128-bit session id + 256-bit secret from the OS CSPRNG; constant-time comparison; secret consumed on first pairing; one phone per session; codes expire (2–15 min); new code ends the old session | `Security/Tokens.cs`, `SessionManager.Pair` |
| Someone photographs the QR and pairs first | Explicit approval on the computer with a 4-digit code shown on both screens; approval window; rejection ends the session | `SessionManager.ApproveDevice/RejectDeviceAsync` |
| Brute force / flooding | Rate limits per IP (pairing 10/min, API 1200/min), request size limits (JSON 256 KB, chunks 16 MB), max 3 pending incoming requests, Kestrel connection and header limits | `LocalTransferServer`, `ApiEndpoints` |
| Other websites driving the API (CSRF) | Bearer token in `Authorization` header (no cookies); `Origin` must match for writes; `Sec-Fetch-Site` must be same-origin; no CORS headers | `RequestGuardMiddleware` |
| DNS rebinding | `Host` header must equal `<LAN IP>:<port>` | `RequestGuardMiddleware` |
| Access from outside the LAN (e.g. port forwarding) | Only private, link-local and loopback client addresses are accepted; Kestrel binds one private address only | `NetworkRules`, `NetworkSelector` |
| Service exposed when idle | The service runs only while a session is open | `SessionManager` |
| Path traversal / dangerous names | Names reduced to one segment; `..`, `/`, `\`, `:`, control chars, bidi overrides, reserved device names (CON, LPT1, …), trailing dots/spaces removed; length limited; final path re-checked to be directly inside the folder | `FileNameSanitizer`, `SafePath` |
| Writing into system folders | Windows, Program Files, ProgramData, the app folder and Fluxio's data folders are refused as download folders | `FolderPolicy` |
| Silent overwrite | Default policy "keep both" (rename); overwrite only when chosen; folders are never replaced | `IncomingStore.Finalize` |
| Corrupted or truncated data | Server hashes exactly the bytes written; final name only after SHA-256 match; downloads confirmed with the source hash; size/mtime check against source changes | `SessionManager.Incoming/Outgoing` |
| Partial files left behind | Data goes to hidden `.part` files; deleted on cancel/failure/session end; leftovers removed at startup and from Settings | `IncomingStore` |
| Running received malware | Fluxio never opens/runs files; Mark of the Web (`Zone.Identifier`, ZoneId=3) on received files | `IncomingStore.MarkAsDownloaded` |
| Leaking computer data to the phone | Phone sees only names/sizes/ids of offered files and the computer name; never paths | `Protocol/FluxioWire` |
| Leaking via logs/history | No tokens, secrets, contents or paths are logged; exceptions logged as type+message only; history has no paths | `AppLog`, `HistoryStore` |
| XSS / clickjacking in the phone page | CSP `default-src 'none'` with self-hosted scripts only; `frame-ancestors 'none'`; `nosniff`; downloads served as `attachment` + `application/octet-stream`; file names rendered as text | `RequestGuardMiddleware`, React |
| Secret leaking via URL | Pairing data is in the URL fragment (not sent to servers or in `Referer`); the page removes it from the address bar immediately; `Referrer-Policy: no-referrer` | `Fluxio.Web/src/state/app.ts` |

## What is not protected

- **Transport encryption inside the LAN.** The phone talks to the computer over HTTP. An attacker who can read or modify traffic on the network (e.g. an open or hostile public Wi-Fi, a compromised router, ARP spoofing on the LAN) could observe file contents and the session token, or tamper with the page. WPA2/WPA3 Wi-Fi encryption protects traffic over the air against outsiders, not against other members of the same network with the key. Fluxio warns when Windows classifies the network as Public. Recommendation: use trusted networks.
  - Why no HTTPS: a phone browser only trusts certificates chained to a public CA, which cannot be issued for private IP addresses; a self-signed certificate means a warning the user must click through, which trains unsafe behaviour. Possible future options: an app-layer encryption with a key carried in the QR fragment (protects against passive sniffing only, since the page code itself comes over HTTP), or native mobile apps that pin the computer's key from the QR code.
- A malicious user who **approves** an attacker's phone grants it the same rights as their own phone.
- Large downloads through the browser's download manager are not verified on the phone.
- The SHA-256 implementation on the phone is JavaScript (`@noble/hashes`, audited, MIT).

## Windows Firewall

Windows asks the first time Fluxio listens. The prompt creates rules for the profiles the user ticks. If the network profile later changes (e.g. Public → Private), those rules no longer apply and Windows does **not** ask again; Fluxio detects this (`NotAllowedOnThisNetwork`) and offers to add a rule limited to private/domain profiles, the local subnet and ports 47800–47819. Adding it runs `netsh` through a UAC prompt; Fluxio itself never needs administrator rights.

## Reporting

Please report vulnerabilities privately via GitHub Security Advisories ("Report a vulnerability").
