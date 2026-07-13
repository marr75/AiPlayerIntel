# AI Player Intel

> See what every AI company is buying, selling, and hunting for — and make them fight for it, because vanilla AI rivals barely compete with you or each other.

<!-- SCREENSHOT: hero shot — the F10 intel panel open over the system map. File: docs/images/aiplayerintel-hero.png -->

## What it does

- **F10 intel panel.** A tree of every body and AI company shows what each one has, wants, and the top price it would pay for a resource — sortable, filterable, split into Trade and Other tabs. No more guessing what a rival needs before you undercut them.
- **Trailing AIs catch up.** An AI that's fallen behind on completed contracts starts valuing its own time more: it bids higher, sells lower, and leans on faster (buy-now) options instead of slow self-production. The leader is untouched, so the game doesn't rubber-band — it just keeps the pack from falling permanently behind.
- **AIs pay more for what they actually need.** A company chasing a contract or a full warehouse slot will now outbid a company that's just browsing, instead of both paying the same flat price. This makes scouting who *needs* a resource — and selling to them specifically — a real strategy.
- **Fair queueing for contested offers.** When several AIs want the same thing you're selling, an "arbiter" briefly reserves the offer for whichever AI is the best fit, instead of resolving it by which one happened to check first. The reservation always expires on its own, so an offer never gets stuck waiting on an AI that changed its mind.
- **More AI buy orders on the market.** AI companies advertise standing buy offers sooner instead of quietly self-sourcing everything, giving you more listings to sell into.
- **AIs that get stuck, un-stick.** If an AI can't complete a step in its plan for too long, the mod forgives the missing resources and cancels the buy order that was blocking it, so it doesn't sit frozen for the rest of the game.

## Before / after

Vanilla AI companies buy and sell at flat prices with no sense of urgency, so a trailing rival can fall behind forever and contested offers resolve on a first-come basis. With this mod, price and priority both respond to how much an AI actually needs something and how far behind it is — the market feels alive instead of static.

## Configuration

Everything lives under the `Gate`, `CatchUp`, `NeedPremium`, `Stuck`, and `PostBids` sections of the config file. A few worth knowing about:

- **`MasterEnable`** (Gate) — the kill switch. Turn this off and every behavior patch goes quiet; only the intel panel keeps working.
- **`CatchUp` → `Enabled`** — off by default. Turn it on if you want trailing AIs to actively close the gap on the leader instead of just falling further behind.
- **`NeedPremium` → `Enabled` / `Fraction`** — on by default; `Fraction` sets how big a premium (e.g. `0.25` = 25% extra) a needy AI will pay. Raise it if AI bidding wars still feel too tame.
- **`PostBids` → `Enabled`** — off by default. Turn it on for more AI buy orders to sell into, if you'd rather see more listings than fewer.

The full config file lives at `BepInEx/config/marr75.solarexpanse.aiplayerintel.cfg`. If you have [Configuration Manager](https://thunderstore.io/) installed, every setting above (plus the F10 panel toggle and refresh rate) is editable live from an in-game menu — no file editing or restarts needed.

<!-- SCREENSHOT: Configuration Manager view of AI Player Intel's settings. File: docs/images/aiplayerintel-config.png -->

## Requirements

- Solar Expanse + BepInEx 5 (Mono/x64).

## Install

1. Install BepInEx 5.
2. Drop the `AiPlayerIntel` folder into `BepInEx/plugins/`.

## Building (developers)

`dotnet build` deploys the DLL to the game's plugins folder via the post-build target. See `AGENTS.md`.
