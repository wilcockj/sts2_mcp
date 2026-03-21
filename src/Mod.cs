#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;


namespace STS2MCP;

[Serializable]
public class CardData
{
	public required string Title { get; set; }
	public required string Description { get; set; }
	public required string EnergyCost { get; set; }
	public required int CardIndex { get; set; }
}

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
public static partial class Mod
{
	private const string HarmonyId = "com.sts2mcp.sts2";
	
	private static HttpListener? _listener;
	private static Thread? _serverThread;
	private static readonly ConcurrentQueue<Action> _mainThreadQueue = new();
	private static readonly int _port = 15527;

	// Highlighting
	private static FieldInfo? _pathsField;
	private static bool _reflectionInitialized;
	private static bool _pendingHighlight;
	private static readonly Dictionary<TextureRect, (Color color, Vector2 scale)> _originalTickProps = new();
	private static List<MapPoint>? _cachedSafestPath;
	private static List<MapPoint>? _cachedAggressivePath;
	private static readonly Color SafestColor = new Color(0.2f, 1f, 0.2f);
	private static readonly Color AggressiveColor = new Color(1f, 0.5f, 0.1f);
	private static readonly Color SharedColor = new Color(0.6f, 0.2f, 1f);

	public static void Initialize()
	{
		MCPEntry.Entry();
		Log.Warn("[MCP] Mod entry finished successfully!");

		try
		{
			var tree = (SceneTree)Engine.GetMainLoop();
			tree.Connect(SceneTree.SignalName.ProcessFrame, Callable.From(ProcessMainThreadQueue));

			_listener = new HttpListener();
			_listener.Prefixes.Add($"http://localhost:{_port}/");
			_listener.Start();

			_serverThread = new Thread(ServerLoop)
			{
				IsBackground = true,
				Name = "STS2MCP Server"
			};
			_serverThread.Start();
			
			Log.Info("[MCP] Mod initialized successfully!");
		}
		catch (Exception e)
		{
			Log.Error($"[MCP] Failed to start: {e}");
		}
		
		var harmony = new Harmony(HarmonyId);
		harmony.PatchAll(typeof(Mod).Assembly);

		InitializeReflection();
		RunManager.Instance.ActEntered += delegate { UpdateAndRequestHighlight(); };
		RunManager.Instance.RoomEntered += delegate { UpdateAndRequestHighlight(); };
	}

	private static void ProcessMainThreadQueue()
	{
		int processed = 0;
		while (_mainThreadQueue.TryDequeue(out var action) && processed < 100)
		{
			try { action(); }
			catch (Exception e) { Log.Error($"[MCP] Main thread action error: {e}"); }
			processed++;
		}
	}
	
	internal static Task<T> RunOnMainThread<T>(Func<T> func)
	{
		var tcs = new TaskCompletionSource<T>();
		_mainThreadQueue.Enqueue(() =>
		{
			try { tcs.SetResult(func()); }
			catch (Exception ex) { tcs.SetException(ex); }
		});
		return tcs.Task;
	}
	
	private static object StartStandardGame()
	{
		if (NGame.Instance.MainMenu == null)
		{
			return new {message = "not on main menu"};
		}
		// we are on the main menu
		// select the game screen
		var submenu = NGame.Instance.MainMenu.OpenSingleplayerSubmenu();
		var method = typeof(NSingleplayerSubmenu).GetMethod(
			"OpenCharacterSelect",
			BindingFlags.NonPublic | BindingFlags.Instance);

		method?.Invoke(submenu, new object?[] { null });
	
		Log.Info("got to character select screen");
		return new { message = "opened single player submenu" };
	}
}
