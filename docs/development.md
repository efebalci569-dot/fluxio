# Development

## Requirements

- Windows 10/11 (the desktop app is WPF; the engine and its tests also run on Linux/macOS)
- .NET 10 SDK
- Node.js 20.19+ (for `src/Fluxio.Web`)

## Layout

```
src/Fluxio.Transfer     engine: sessions, API, storage (net10.0)
src/Fluxio.Desktop      Windows app (net10.0-windows, WPF)
src/Fluxio.Web          phone page (React + TypeScript + Vite) → embedded into Fluxio.Transfer
src/Fluxio.Landing      website (static) → deployed to Vercel
tests/Fluxio.Transfer.Tests   protocol/integration tests (real Kestrel on 127.0.0.1)
tests/Fluxio.Security.Tests   input validation and API security tests
scripts/                icons, static preview server
docs/                   architecture, security, API
```

## Build and test

```powershell
cd src/Fluxio.Web
npm ci
npm test          # Vitest
npm run build     # type-check + production bundle in dist/
cd ../..
dotnet test Fluxio.sln
dotnet run --project src/Fluxio.Desktop
```

`Fluxio.Transfer.csproj` runs `npm ci && npm run build` automatically when `src/Fluxio.Web/dist/index.html` is missing. Use `-p:ForceWebUiBuild=true` to rebuild it, or `-p:SkipWebUiBuild=true` to build without Node.js (the service then serves a "web UI not built" page).

## Working on the phone page

Start Fluxio, create a session, then run Vite against it:

```powershell
$env:FLUXIO_TARGET = "http://192.168.1.20:47800"   # the address shown in Fluxio
npm run dev -- --host
```

Open the Vite URL on the phone and append the `#s=…&k=…` part of the pairing link (copy it with the copy button next to the address). The dev proxy rewrites Host/Origin so the security checks still pass.

## Website

```powershell
node scripts/serve-static.mjs src/Fluxio.Landing 5175
```

The site is plain HTML/CSS/JS. The download page reads the latest release from the GitHub API (`assets/js/config.js` holds the repository name). Turkish text lives in `assets/js/site.js`.

## Icons

`powershell -File scripts/generate-icons.ps1` renders the logo to `fluxio.ico`, PNGs for the app and the website.

## Localization

- Desktop: `src/Fluxio.Desktop/Resources/Strings/{tr,en}.json` (same keys), used via `{l:Tr Key}` in XAML and `Loc.Instance[...]` in code.
- Phone: `src/Fluxio.Web/src/i18n/strings.ts` (the `tr` table is typed against `en`; a test checks completeness).

## Manual test checklist (real devices)

Run on a Windows PC and at least one real Android phone and one iPhone, same Wi-Fi:

- [ ] QR code scanned with the built-in camera app opens the page
- [ ] Approval code matches; Deny ends the session on the phone
- [ ] PC → phone: several files, one > 1 GB, cancel one file, cancel all
- [ ] Phone → PC: several files incl. a large video; conflict with an existing name (keep both / replace / skip); decline
- [ ] Turn Wi-Fi off on the phone during an upload, turn it back on → resumes
- [ ] Lock the phone during a transfer → interruption is reported, partial data removed
- [ ] QR code expiry; new code invalidates the old one; idle timeout
- [ ] Network profile Public vs Private; firewall rule from Settings
- [ ] Closing Fluxio during a transfer asks for confirmation and cleans up
