using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
namespace sts2mcp;

[ModInitializer("Initialize")]
public static class MCPInitializer
{
	public static void Initialize()
	{
		Log.Warn("MCP ACTIVATED super duper");
		var harmony = new Harmony("com.sts2mcp.sts2");
		harmony.PatchAll(typeof(MCPInitializer).Assembly);

	}
}


/// <summary>
/// READ ALL CARDS
/// </summary>
[HarmonyPatch(typeof(Player), "PopulateStartingDeck")]
public static class PatchUpgradeStartingDeck
{
	[HarmonyPostfix]
	public static void Postfix(Player __instance)
	{
		Log.Warn("Got to populate");
		foreach (CardModel card in ModelDb.AllCards)
		{
			Log.Warn(card.Title);
		}
	}
}
