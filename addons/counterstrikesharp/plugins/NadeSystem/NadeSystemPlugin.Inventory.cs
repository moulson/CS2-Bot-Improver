using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NadeSystem;

public partial class NadeSystemPlugin : BasePlugin
{
    // ═══════════════════════════════════════════════════════════
    //  Bot grenade inventory (buy / pickup / consume)
    // ═══════════════════════════════════════════════════════════

    private static string NormalizeGrenadeType(string gtype)
    {
        gtype = gtype.ToLowerInvariant();
        return gtype is "incgrenade" ? "molotov" : gtype;
    }

    private static IEnumerable<string> GetWeaponNamesForType(string gtype, bool isCT)
    {
        return NormalizeGrenadeType(gtype) switch
        {
            "flash"   => new[] { "weapon_flashbang" },
            "smoke"   => new[] { "weapon_smokegrenade" },
            "he"      => new[] { "weapon_hegrenade" },
            "molotov" => isCT ? new[] { "weapon_incgrenade" } : new[] { "weapon_molotov" },
            _         => Array.Empty<string>(),
        };
    }

    private int CountBotGrenades(CCSPlayerController bot, string gtype)
    {
        return CountAllBotGrenades(bot).GetValueOrDefault(NormalizeGrenadeType(gtype));
    }

    private Dictionary<string, int> CountAllBotGrenades(CCSPlayerController bot)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["flash"] = 0, ["smoke"] = 0, ["he"] = 0, ["molotov"] = 0,
        };

        var pawn = bot.PlayerPawn?.Value;
        if (pawn?.WeaponServices == null) return counts;

        foreach (var wHandle in pawn.WeaponServices.MyWeapons)
        {
            var weapon = wHandle.Value;
            if (weapon == null) continue;
            string name = weapon.DesignerName;
            if (name == "weapon_flashbang") counts["flash"]++;
            else if (name == "weapon_smokegrenade") counts["smoke"]++;
            else if (name == "weapon_hegrenade") counts["he"]++;
            else if (name is "weapon_molotov" or "weapon_incgrenade") counts["molotov"]++;
        }

        return counts;
    }

    private int GetMaxGrenades(string gtype) =>
        MaxGrenadesPerBot.TryGetValue(NormalizeGrenadeType(gtype), out int max) ? max : 0;

    private bool BotHasGrenade(CCSPlayerController bot, string gtype) =>
        CountBotGrenades(bot, gtype) > 0;

    private bool ConsumeBotGrenade(CCSPlayerController bot, string gtype)
    {
        bool isCT = bot.TeamNum == (int)CsTeam.CounterTerrorist;
        foreach (var weaponName in GetWeaponNamesForType(gtype, isCT))
        {
            if (bot.RemoveItemByDesignerName(weaponName))
                return true;
        }
        return false;
    }

    private void StripAllBotGrenades(CCSPlayerController bot)
    {
        var pawn = bot.PlayerPawn?.Value;
        if (pawn?.WeaponServices == null) return;

        var grenadeNames = new HashSet<string>(AllGrenadeWeaponNames, StringComparer.OrdinalIgnoreCase);
        var toRemove = new List<CBasePlayerWeapon>();
        foreach (var wHandle in pawn.WeaponServices.MyWeapons)
        {
            var weapon = wHandle.Value;
            if (weapon == null || !weapon.IsValid) continue;
            if (grenadeNames.Contains(weapon.DesignerName))
                toRemove.Add(weapon);
        }
        foreach (var weapon in toRemove)
            pawn.RemovePlayerItem(weapon);
    }

    private void EnforceGrenadeLimits(CCSPlayerController bot)
    {
        foreach (var entry in MaxGrenadesPerBot)
        {
            while (CountBotGrenades(bot, entry.Key) > entry.Value)
                ConsumeBotGrenade(bot, entry.Key);
        }
    }

    private void EnforceGrenadeLimitsForAll()
    {
        if (_grenadeBuyBotsRemaining > 0) return;

        var rules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        if (rules?.GameRules?.FreezePeriod == true) return;

        foreach (var bot in Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller"))
        {
            if (!bot.IsValid || !bot.IsBot || !bot.PawnIsAlive) continue;
            EnforceGrenadeLimits(bot);
        }
    }

    private void BuyBotGrenades(CCSPlayerController bot)
    {
        if (_botNadesMode == "off") return;
        if (bot.HasBeenControlledByPlayerThisRound) return;

        var money = bot.InGameMoneyServices;
        if (money == null) return;

        bool isCT     = bot.TeamNum == (int)CsTeam.CounterTerrorist;
        bool isPoor   = _poorBots.Contains((uint)bot.Index);
        var costTable = isCT ? CostCT : CostT;
        int spendCap  = GetRoundSpendCap(isCT, isPoor);
        uint botIdx   = (uint)bot.Index;
        int spent     = _roundSpendPerBot.TryGetValue(botIdx, out int existingSpend) ? existingSpend : 0;

        string[] buyOrder = isPoor && !IsPistolRound()
            ? new[] { "flash", "flash", "smoke" }
            : new[] { "flash", "flash", "smoke", "he", "molotov" };

        var owned = CountAllBotGrenades(bot);

        foreach (var rawType in buyOrder)
        {
            string gtype = NormalizeGrenadeType(rawType);
            if (owned.GetValueOrDefault(gtype) >= GetMaxGrenades(gtype)) continue;
            if (!costTable.TryGetValue(gtype, out int cost)) continue;
            if (spent + cost > spendCap) continue;
            if (money.Account < cost) continue;

            string weaponName = GetWeaponNamesForType(gtype, isCT).First();
            bot.GiveNamedItem(weaponName);
            money.Account -= cost;
            Utilities.SetStateChanged(bot, "CCSPlayerController", "m_pInGameMoneyServices");
            spent += cost;
            owned[gtype]++;
        }

        _roundSpendPerBot[botIdx] = spent;
    }

    private void ScheduleBotGrenadeLoadouts()
    {
        if (_botNadesMode == "off") return;

        var bots = Utilities
            .FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller")
            .Where(b => b.IsValid && b.IsBot && b.PawnIsAlive)
            .ToList();

        _grenadeBuyBotsRemaining = bots.Count;
        if (bots.Count == 0) return;

        // Stagger per-bot work so buy phase does not stall the server thread.
        const float staggerSec = 0.08f;
        for (int i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];
            float delay = i * staggerSec;
            AddTimer(delay, () => PrepareSingleBotGrenadeLoadout(bot));
        }
    }

    private void PrepareSingleBotGrenadeLoadout(CCSPlayerController bot)
    {
        try
        {
            if (_botNadesMode == "off") return;
            if (!bot.IsValid || !bot.IsBot || !bot.PawnIsAlive) return;
            StripAllBotGrenades(bot);
            BuyBotGrenades(bot);
        }
        finally
        {
            if (_grenadeBuyBotsRemaining > 0)
                _grenadeBuyBotsRemaining--;
        }
    }
}
