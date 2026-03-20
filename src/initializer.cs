using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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
	
	private static HttpListener? _listener;
	private static Thread? _serverThread;
	private static readonly int _port = 15527;

	public static void Initialize()
	{
		MCPEntry.Entry();
		Log.Warn("[MCP] Mod initialized successfully!");

		try
		{
			var tree = (SceneTree)Engine.GetMainLoop();

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
		harmony.PatchAll(typeof(MCPInitializer).Assembly);
	}

	private static void ServerLoop()
	{
		while (_listener.IsListening)
		{
			try
			{
				var context = _listener.GetContext();
				ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
			}
			catch (Exception e)
			{
				Log.Error($"[MCP] Server failed: {e}");
			}
		}
	}

	private static void HandleRequest(HttpListenerContext context)
	{
		try
		{
			var request = context.Request;
			var response = context.Response;
			response.Headers.Add("Access-Control-Allow-Origin", "*");
			response.Headers.Add("Access-Control-Allow-Methods", "GET, POST");
			response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

			string path = request.Url?.AbsolutePath ?? "/";

			Log.Info($"[MCP] Request path: {path}");

			response.Close();
		} catch (Exception e)
		{
			Log.Error($"[MCP] Server failed: {e}");
		}
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
