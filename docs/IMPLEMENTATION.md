# BSS architecture implementation ledger — 2026-11-21
Spec: user-approved plan in this chat.
- [x] 1. Contracts, Core, Server, auth, additive migration.
- [x] 2. Local MQTT, telemetry, safety, acknowledgements and recovery.
- [x] 3. Durable swap sessions, reservations, wallet and outbox.
- [x] 4. Desktop and simulator use API only.
- [x] 5. Web3D integration, physical model and interactions.
- [ ] 6. Offline launcher, documentation, integration and UI verification, final review.
Ruling: initialize Git in existing source directory, use implementation branch and ZIP backup; no existing repository to isolate.
Ruling: local Mosquitto binary is a prepared dependency; runtime does not fetch network assets.
Ruling: retained source-of-truth SQL procedures remain server-only; no client fallback writes.
Pre-flight: command acknowledgement and safety resolution must agree on fresh telemetry; wallet holds and all swap mutations must share one transaction boundary.

Ruling (user correction): preserve existing WPF XAML design and views exactly; replace only data/service adapters and workflow coordination. Custom programmatic DesktopWindow is discarded.

Verification: Release build 0 warnings/0 errors; 22 API/MQTT integration checks passed including real backend process restarts before receipt and after charge; PBKDF2 checks passed; BMS Node checks passed; 6 original-XAML STA smoke checks passed.
Fresh whole-branch review identified lease disclosure, incomplete telemetry, premature reservation release, stale ACK, offline queue blocking, and recovery defects; fixes added and integration checks rerun.
UI correction: original WPF layout restored and API adapters retained. Browser automation blocked by Chrome permission; IAB failed to attach. No claim of end-to-end visual WebGL QA or OS-level Internet-disconnected demonstration.
Task 6 remains partially verified: visual browser QA and actual Internet-off demonstration require an available UI surface. Runtime assets/endpoints are local; no production database was reseeded or cleared.

2026-11-22 — WPF workflow organization: preserve styles; staff overview/operation separation; customer-only kiosk and wallet; admin-only customers; session/support list with guarded cancel/resume; station-specific cutoff and restore. Testing via WorkflowCheck avoids locking files currently debugged by user.
