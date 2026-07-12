# AiPlayerIntel

BepInEx plugin for the Unity game **Solar Expanse** that gives the player visibility into, and
light-touch control over, AI-company market behavior.

An in-game panel (default toggle `F10`) surfaces per-company intel: outstanding deficits, max
viable buy price per resource/market, and standing. Behind the panel, a set of Harmony patches
tune how AI companies participate in the market:

- Buy-eligibility gate (zero-demand gate, buy-quantity clamp) so AI bids stay reservation-sane.
- `OfferArbiter`: contract-tier and sub-order sequencing for contested player-sell offers, with
  fail-open game-time leases so a stalled evaluation can't park other buyers indefinitely.
- `Willingness` service: centralizes need-premium and catch-up (time-cost) pricing multipliers.
- `StuckWatch`: credits the netted deficit to unstick AI companies stalled on a resource need.
- Post-more-bids lever: lowers the vanilla self-source time threshold so AI posts buy orders more
  readily, giving the player more offers to fill.

All behavior is config-gated (`BepInEx/config/marr75.solarexpanse.aiplayerintel.cfg`); see
`Config/Configuration.cs` for the full set of tunables and their defaults.

## Build

Requires .NET SDK (net48 target) and a Solar Expanse install.

```powershell
$env:SOLAR_EXPANSE_DIR = 'C:\path\to\Solar Expanse'
dotnet build
```

`SOLAR_EXPANSE_DIR` (or `-p:GameDir=...` on the CLI) locates the game's managed DLLs and the
BepInEx plugins folder. A post-build target copies the built DLL to
`$(GameDir)\BepInEx\plugins\AiPlayerIntel\`.

## License

MIT (see LICENSE).
