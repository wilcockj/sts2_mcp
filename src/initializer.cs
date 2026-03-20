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
		MCPEntry.Entry();
		Log.Warn("[MCP] Mod initialized successfully!");
		
		CreateHelloUI();
		
		var harmony = new Harmony(HarmonyId);
		harmony.PatchAll(typeof(MCPInitializer).Assembly);
	}
	
	private static void CreateHelloUI()
	{
		var mainLoop = Engine.GetMainLoop();
		if (mainLoop is not SceneTree sceneTree) return;
		
		var canvasLayer = new CanvasLayer();
		canvasLayer.Layer = 9999;
		
		var panel = new Panel();
		panel.SetAnchorsPreset(Control.LayoutPreset.Center);
		panel.OffsetLeft = -250;
		panel.OffsetRight = 250;
		panel.OffsetTop = -50;
		panel.OffsetBottom = 50;
		panel.SelfModulate = new Color(0, 0, 0, 0.8f);
		
		var label = new Label();
		label.SetAnchorsPreset(Control.LayoutPreset.Center);
		label.OffsetLeft = -250;
		label.OffsetRight = 250;
		label.OffsetTop = -50;
		label.OffsetBottom = 50;
		label.Text = "Hello From Mod";
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.VerticalAlignment = VerticalAlignment.Center;
		label.AddThemeFontSizeOverride("font_size", 48);
		label.Modulate = new Color(1f, 1f, 0f);
		
		canvasLayer.AddChild(panel);
		canvasLayer.AddChild(label);
		sceneTree.Root.AddChild(canvasLayer);
		
		Log.Warn("[MCP] Hello UI created!");
	}
}

[HarmonyPatch(typeof(Player), "PopulateStartingDeck")]
public static class PatchPopulateStartingDeck
{
	[HarmonyPostfix]
	public static void Postfix(Player __instance)
	{
		Log.Warn("[MCP] Deck populated!");
	}
}
