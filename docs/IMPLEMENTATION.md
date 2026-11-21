# BSS architecture implementation ledger — 2026-11-21
Spec: user-approved plan in this chat.
- [ ] 1. Contracts, Core, Server, auth, additive migration.
- [ ] 2. Local MQTT, telemetry, safety, acknowledgements and recovery.
- [ ] 3. Durable swap sessions, reservations, wallet and outbox.
- [ ] 4. Desktop and simulator use API only.
- [ ] 5. Web3D integration, physical model and interactions.
- [ ] 6. Offline launcher, documentation, integration and UI verification, final review.
Ruling: initialize Git in existing source directory, use implementation branch and ZIP backup; no existing repository to isolate.
Ruling: local Mosquitto binary is a prepared dependency; runtime does not fetch network assets.
Ruling: retained source-of-truth SQL procedures remain server-only; no client fallback writes.
Pre-flight: command acknowledgement and safety resolution must agree on fresh telemetry; wallet holds and all swap mutations must share one transaction boundary.
