#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace STS2MCP;

public class PlayerCardsData
{
	public List<SerializableCard> Draw { get; set; } = new();
	public List<SerializableCard> Hand { get; set; } = new();
	public List<SerializableCard> Discard { get; set; } = new();
	public List<SerializableCard> Exhaust { get; set; } = new();
	public List<SerializableCard> Play { get; set; } = new();
	public List<SerializableCard> Deck { get; set; } = new();
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
public static class MCPInitializer
{
	private const string HarmonyId = "com.sts2mcp.sts2";
	
	private static HttpListener? _listener;
	private static Thread? _serverThread;
	private static readonly ConcurrentQueue<Action> _mainThreadQueue = new();
	private static readonly int _port = 15527;

	public static void Initialize()
	{
		MCPEntry.Entry();
		Log.Warn("[MCP] Mod initialized successfully!");

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
		harmony.PatchAll(typeof(MCPInitializer).Assembly);
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
	
	private static void ServerLoop()
	{
		while (_listener!.IsListening)
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

			string path = request.Url?.AbsolutePath.TrimEnd('/') ?? "/";
			string method = request.HttpMethod;

			Log.Info($"[MCP API] {method} {path}");
			
			string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
			
			if (segments.Length == 0)
			{
				SendJson(response, new { message = "Slay the Spire 2 API v1" });
			}
			else if (segments[0].Equals("api", StringComparison.OrdinalIgnoreCase))
			{
				HandleApiRequest(segments, method, response);
			}
			else
			{
				response.StatusCode = 404;
				response.Close();
			}
		} catch (Exception e)
		{
			Log.Error($"[MCP] HTTP Server failed: {e}");
		}
	}
	
	private static void SendJson(HttpListenerResponse response, object obj)
	{
		response.ContentType = "application/json";
		// Serialize with indentation
		string json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
		byte[] buffer = System.Text.Encoding.UTF8.GetBytes(json);
		response.ContentLength64 = buffer.Length;
		response.OutputStream.Write(buffer, 0, buffer.Length);
		response.OutputStream.Close();
	}

	private static void HandleApiRequest(string[] segments, string method, HttpListenerResponse response)
	{
		// Expect at least: /api/v1/...
		if (segments.Length < 2)
		{
			response.StatusCode = 404;
			response.Close();
			return;
		}

		string version = segments[1];
		if (!version.Equals("v1", StringComparison.OrdinalIgnoreCase))
		{
			response.StatusCode = 404;
			response.Close();
			return;
		}
		
		// Handle resources after /api/v1/
		if (segments.Length >= 3)
		{
			string resource = segments[2].ToLower();

			switch (resource)
			{
				case "player":
					if (method == "GET")
					{
						try
						{
							// Gather player info
							var playerData = RunOnMainThread(() => new
								{
									Health = GetPlayerHealth(),
									Cards = GetPlayerCards(),
									Gold = GetPlayerGold(),
									Energy = GetPlayerEnergy(),
									Relics = GetPlayerRelics(),
								}
							);

							SendJson(response, playerData);
						}
						catch (Exception e)
						{
							Log.Error($"[MCP] Failed to get player: {e}");
							
							SendJson(response, new { message = "Player data is not available yet" });
						}
					}
					else
					{
						response.StatusCode = 405; // Method not allowed
						response.Close();
					}
					break;
				
				default:
					response.StatusCode = 404;
					response.Close();
					break;
			}
		}
		else
		{
			response.StatusCode = 404;
			response.Close();
		}
	}

	private static object GetPlayerHealth()
	{
		var run = RunManager.Instance.DebugOnlyGetState();
		var player = LocalContext.GetMe(run);
		return new
		{
			CurrentHP = player.Creature.CurrentHp,
			MaxHP = player.Creature.MaxHp,
			CurrentBlock = player.Creature.Block,
		};
	}
	
	private static object GetPlayerRelics()
	{
		var run = RunManager.Instance.DebugOnlyGetState();
		var player = LocalContext.GetMe(run);
		return new
		{
			Relics = player.ToSerializable().Relics,
		};
	}

	private static object GetPlayerEnergy()
	{
		var run = RunManager.Instance.DebugOnlyGetState();
		var player = LocalContext.GetMe(run);

		return new
		{
			CurrentEnergy = player.PlayerCombatState.Energy,
			MaxEnergy = player.PlayerCombatState.MaxEnergy,
		};
	}
	
	private static PlayerCardsData GetPlayerCards()
	{
		var run = RunManager.Instance.DebugOnlyGetState();
		var player = LocalContext.GetMe(run);
		
		var cardsData = new PlayerCardsData();
		
		foreach (CardPile pile in player.Piles)
		{
			if (pile?.Cards == null) continue;

			foreach (CardModel model in pile.Cards)
			{
				var serializableCard = model.ToSerializable();

				switch (pile.Type)
				{
					case PileType.Hand:
						cardsData.Hand.Add(serializableCard);
						break;
					case PileType.Draw:
						cardsData.Draw.Add(serializableCard);
						break;
					case PileType.Discard:
						cardsData.Discard.Add(serializableCard);
						break;
					case PileType.Exhaust:
						cardsData.Exhaust.Add(serializableCard);
						break;
					case PileType.Play:
						cardsData.Play.Add(serializableCard);
						break;
					case PileType.Deck:
						cardsData.Deck.Add(serializableCard);
						break;
				}
			}
		}
		
		return cardsData;
	}

	private static int GetPlayerGold()
	{
		var run = RunManager.Instance.DebugOnlyGetState();
		var player = LocalContext.GetMe(run);
		var serializablePlayer = player!.ToSerializable();
		return serializablePlayer.Gold;
	}
}
