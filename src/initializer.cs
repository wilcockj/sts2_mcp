using System;
using System.Runtime.InteropServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace STS2MCP;

[ModInitializer("Entry")]
public static class MCPEntry
{
	[DllImport("libdl.so.2")]
	static extern IntPtr dlopen(string filename, int flags);

	[DllImport("libdl.so.2")]
	static extern IntPtr dlerror();

	private static IntPtr _holder;

	public static void Entry()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			Log.Warn("[MCP] Running on Linux, manually dlopen libgcc for Harmony");
			_holder = dlopen("libgcc_s.so.1", 2 | 256);
			if (_holder == IntPtr.Zero)
			{
				Log.Warn("[MCP] dlopen failed: " + Marshal.PtrToStringAnsi(dlerror()));
			}
		}
	}
}

[ModInitializer("Initialize")]
public static class MCPInitializer
{
	private const string HarmonyId = "com.sts2mcp.sts2";

	public static void Initialize()
	{
		Log.Warn("[MCP] Mod initialized successfully!");
		
		var harmony = new Harmony(HarmonyId);
		harmony.PatchAll(typeof(MCPInitializer).Assembly);
	}
}

[HarmonyPatch(typeof(Player), "PopulateStartingDeck")]
public static class PatchPopulateStartingDeck
{
	[HarmonyPostfix]
	public static void Postfix(Player instance)
	{
		Log.Warn("[MCP] Deck populated!");
	}
}
