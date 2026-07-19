# AI Player Intel

AI companies in Solar Expanse already run a real trading strategy. You just never got to see it, let alone act on it. AI Player Intel puts that strategy on screen.

![AI Player Intel panel: CNSA offers to buy Helium-3 it can't source quickly, shown beside the Earth [Orbit] body panel](docs/images/aiplayerintel-hero.png)

## What it does

- F10 intel panel: A tree of every body and AI company shows what each one has, wants, and the top price it would pay for a resource. Sortable, filterable, split into Trade and Other tabs. No more guessing what a rival needs before you undercut them.
- Trailing AIs catch up: An AI that's fallen behind on completed contracts starts valuing its own time more: it bids higher and leans on faster (buy-now) options instead of slow self-production. The leader is untouched, so the game doesn't rubber-band; it just keeps the pack from falling permanently behind.
- AIs pay more for what they actually need: An AI company chasing a contract will now outbid a company that's just browsing, instead of both paying the same flat price. This makes scouting who
  _needs_ a resource, and selling to them specifically, a real strategy.
- Click to trade: Spot a resource a rival needs and click it: the Marketplace opens with an offer already filled in, so you never have to reach for the dev console to avoid the clunky Offer form.

<table>
<tr>
<td width="50%"><img src="docs/images/aiplayerintel-ownership.png" alt="The panel's ownership view listing who has and wants each resource" width="100%"></td>
<td width="50%"><img src="docs/images/aiplayerintel-marketplace.png" alt="Hovering a &quot;need&quot; row auto-populates the Marketplace to act on it" width="100%"></td>
</tr>
</table>

- Fair queueing for contested offers: When several AIs want the same thing you're selling, the mod briefly reserves the offer for whichever AI is the best fit, instead of resolving it by which one happened to check first. The reservation always expires on its own, so an offer never gets stuck waiting on an AI that changed its mind.
- More AI buy orders on the market: AI companies advertise standing buy offers sooner instead of quietly self-sourcing everything, giving you more listings to sell into.
- AIs that get stuck, un-stick: If an AI can't complete a step in its plan for too long, the mod forgives the missing resources and cancels the buy order that was blocking it, so it doesn't sit frozen for the rest of the game.

## Before / after

Vanilla AI companies buy and sell at flat prices with no sense of urgency, so a trailing rival can fall behind forever and contested offers resolve on a first-come basis. With this mod, price and priority both respond to how much an AI actually needs something and how far behind it is. The market feels alive instead of static.

## Configuration

Everything lives under the `Gate`, `CatchUp`, `NeedPremium`, `Stuck`, and
`PostBids` sections of the config file. A few worth knowing about:

-
`MasterEnable` (Gate): the kill switch. Turn this off and every behavior patch goes quiet; only the intel panel keeps working.
- `CatchUp` →
  `Enabled`: off by default. Turn it on if you want trailing AIs to actively close the gap on the leader instead of just falling further behind.
- `NeedPremium` → `Enabled` / `Fraction`: on by default; `Fraction` sets how big a premium (e.g.
  `0.25` = 25% extra) a needy AI will pay. Raise it if AI bidding wars still feel too tame.
- `PostBids` →
  `Enabled`: off by default. Turn it on for more AI buy orders to sell into, if you'd rather see more listings than fewer.

The full config file lives at
`BepInEx/config/marr75.solarexpanse.aiplayerintel.cfg`. If you have [Configuration Manager](https://thunderstore.io/) installed, every setting above (plus the F10 panel toggle and refresh rate) is editable live from an in-game menu, with no file editing or restarts needed.

## Requirements

- Solar Expanse + BepInEx 5 (Mono/x64).

## Install

1. Install BepInEx 5.
2. Drop the `AiPlayerIntel` folder into `BepInEx/plugins/`.

## Building (developers)

`dotnet build` deploys the DLL to the game's plugins folder via the post-build target. See `AGENTS.md`.
