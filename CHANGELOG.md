# Changelog

## 1.3.0 — NadeSystem inventory limits + compiled plugin builds

### NadeSystem (v1.3.0)

- Bots buy grenades during the buy phase (staggered strip/buy after BotBuy) and may only throw grenades they hold.
- Per-bot carry caps: 2 flash, 1 smoke, 1 HE, 1 molotov/incendiary (pickups allowed but capped).
- Money is deducted at buy time; throw paths consume a matching inventory item instead of charging again.
- Decoy lineups still work without a held decoy.
- Limit enforcement skips freeze time and active buy queues to avoid buy-phase stalls.

### Build / releases

- `scripts/bootstrap-raytrace.sh` fetches/builds `RayTraceApi.dll` into plugin `libs/` for compile.
- `scripts/build-plugins.sh` builds active CounterStrikeSharp plugins.
- GitHub Actions workflow `.github/workflows/build-plugins.yml` uploads `NadeSystem-net10.zip` and `CS2-Bot-Improver-plugins.zip`.
- NadeSystem / BotAimImprover csproj: `RayTraceApi` is a private=false reference; local Ray-Trace clones are excluded from compile.

### Docs

- README: build-from-source, deploy compiled plugins, RayTrace runtime layout, upstream sync notes.
