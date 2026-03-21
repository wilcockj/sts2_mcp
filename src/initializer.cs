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
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace STS2MCP;

[Serializable]
public class CardData
{
	public required string Title { get; set; }
	public required string Description { get; set; }
	public required string EnergyCost { get; set; }
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
		harmony.PatchAll(typeof(MCPInitializer).Assembly);

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
			else if (request.HttpMethod == "GET" && path == "/map")
			{

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
							).GetAwaiter().GetResult();

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
				
				case "enemies":
					if (method == "GET")
					{
						try
						{
							var enemyData = RunOnMainThread(GetEnemyInfo).GetAwaiter().GetResult();
							
							SendJson(response, enemyData);
						}
						catch (Exception e) {
							Log.Error($"[MCP] Failed to get enemies: {e}");
							SendJson(response, new { message = "Enemy data is not available yet" });
						}
					}
					break;
				case "map":
					if (method == "GET")
					{
						try
						{
							var mapData = RunOnMainThread(GetCurrentActMap).GetAwaiter().GetResult();;
							SendJson(response, mapData);
						}
						catch (Exception e) {
							Log.Error($"[MCP] Failed to get map paths: {e}");
							SendJson(response, new { message = "Map data is not available yet" });
						}
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

	private static object GetEnemyInfo()
	{
		var combatState = CombatManager.Instance.DebugOnlyGetState();
		var enemiesData = new List<object>();

		if (combatState?.Enemies == null || combatState.Enemies.Count == 0)
			return enemiesData; // empty list if no enemies

		foreach (var enemy in combatState.Enemies)
		{
			// Gather all intents for this enemy
			var intents = new List<object>();
			var powers = new List<object>();
			if (enemy.Monster?.NextMove?.Intents != null)
			{
				foreach (var intent in enemy.Monster.NextMove.Intents)
				{
					intents.Add(new
					{
						Label = intent
							.GetIntentLabel(enemy.CombatState.PlayerCreatures, enemy)
							.GetFormattedText(),
						Type = intent.IntentType.ToString(),
					});
				}
				
				foreach (var power in enemy.Powers)
				{
					powers.Add(new
					{
						Type = power.Type.ToString(),
						Amount = power.Amount,
						Desc = LocManager.Instance.GetTable(power.Description.LocTable).GetRawText(power.Description.LocEntryKey),
					});
				}
			}

			enemiesData.Add(new
			{
				Name = enemy.Name,
				CurrentHP = enemy.CurrentHp,
				MaxHp = enemy.MaxHp,
				CurrentBlock = enemy.Block,
				IsStunned = enemy.IsStunned,
				Intents = intents,
				Powers = powers,
			});
		}

		return enemiesData;
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

	/// <summary>
	/// Recursive DFS helper to find all paths from current point to the Boss.
	/// </summary>
	/// <param name="current">Current map point.</param>
	/// <param name="currentPath">Accumulated path so far.</param>
	/// <param name="allPaths">Output list to collect all complete paths.</param>
	static void FindAllPathsToBoss(MapPoint current, List<MapPoint> currentPath, List<List<MapPoint>> allPaths)
	{
		if (current == null) return;

		// Add current point to path
		currentPath.Add(current);

		// Check if we reached the Boss
		if (current.PointType == MapPointType.Boss)
		{
			// Found a path to Boss, save a copy
			allPaths.Add(new List<MapPoint>(currentPath));
		}
		else if (current.Children != null && current.Children.Count > 0)
		{
			// Continue DFS on children
			foreach (var child in current.Children)
			{
				FindAllPathsToBoss(child, currentPath, allPaths);
			}
		}

		// Backtrack: remove current point from path
		currentPath.RemoveAt(currentPath.Count - 1);
	}
	
	/// <summary>
	/// Danger score for the safety path: higher = safer.
	/// Counts fights (Monster=1, Unknown=1, Elite=3).
	/// </summary>
	private static int ScoreSafety(List<MapPoint> path)
	{
		int score = 0;
		foreach (var p in path)
		{
			switch (p.PointType)
			{
				case MapPointType.RestSite: score += 1; break;
				case MapPointType.Treasure: score += 1; break;
				case MapPointType.Shop: score += 1; break;
				case MapPointType.Monster: score -= 1; break;
				case MapPointType.Elite:   score -= 3; break;
				case MapPointType.Unknown: score += 0; break;
			}
		}
		return score;
	}

	/// <summary>
	/// Reward score for the aggressive path: higher = more rewards.
	/// Weights: Elite=5, Treasure=3, Monster=2, Unknown=2.
	/// </summary>
	private static int ScoreAggressive(List<MapPoint> path)
	{
		int score = 0;
		foreach (var p in path)
		{
			switch (p.PointType)
			{
				case MapPointType.RestSite: score += 1; break;
				case MapPointType.Treasure: score += 1; break;
				case MapPointType.Shop: score += 1; break;
				case MapPointType.Monster: score += 2; break;
				case MapPointType.Elite:   score += 3; break;
				case MapPointType.Unknown: score += 2; break;
			}
		}
		return score;
	}

	private static object FormatPath(List<MapPoint> path, string label)
	{
		return new
		{
			Label = label,
			Length = path.Count,
			Nodes = path.Select(p => new { Type = p.PointType.ToString(), Coord = p.coord }).ToList(),
		};
	}

	private static object GetCurrentActMap()
	{
		ComputePaths();
		if (_cachedSafestPath == null || _cachedAggressivePath == null)
			return new { Error = "No map available" };

		RequestHighlightOnMapOpen();

		return new
		{
			SafestPath = FormatPath(_cachedSafestPath.Skip(1).ToList(), "Safest (fewest fights)"),
			MostAggressivePath = FormatPath(_cachedAggressivePath.Skip(1).ToList(), "Most aggressive (max rewards)"),
		};
	}

	private static void ComputePaths()
	{
		var run = RunManager.Instance.DebugOnlyGetState();
		var startPoint = run.CurrentMapPoint ?? run.Map?.StartingMapPoint;
		if (startPoint == null)
		{
			Log.Info("[MCP] No start point in map search");
			return;
		}

		var currentPath = new List<MapPoint>();
		var allPaths = new List<List<MapPoint>>();
		FindAllPathsToBoss(startPoint, currentPath, allPaths);

		if (allPaths.Count == 0) return;

		_cachedSafestPath = allPaths.OrderByDescending(ScoreSafety).First();
		_cachedAggressivePath = allPaths.OrderByDescending(ScoreAggressive).First();

		Log.Info($"[MCP] Total paths: {allPaths.Count}");
		Log.Info($"[MCP] Safest: {string.Join("->", _cachedSafestPath.Skip(1).Select(p => p.PointType.ToString()))}");
		Log.Info($"[MCP] Aggressive: {string.Join("->", _cachedAggressivePath.Skip(1).Select(p => p.PointType.ToString()))}");
	}

	private static void InitializeReflection()
	{
		try
		{
			_pathsField = typeof(NMapScreen).GetField("_paths", BindingFlags.NonPublic | BindingFlags.Instance);
			_reflectionInitialized = _pathsField != null;
			Log.Info(_reflectionInitialized ? "[MCP] Reflection initialized" : "[MCP] Reflection failed: _paths not found");
		}
		catch (Exception ex)
		{
			Log.Error($"[MCP] Reflection error: {ex.Message}");
		}
	}

	private static void UpdateAndRequestHighlight()
	{
		ComputePaths();
		RequestHighlightOnMapOpen();
	}

	private static void RequestHighlightOnMapOpen()
	{
		var mapScreen = NMapScreen.Instance;
		if (mapScreen != null && mapScreen.IsOpen)
		{
			HighlightPaths();
			return;
		}

		_pendingHighlight = true;
		if (mapScreen != null)
			mapScreen.Opened += OnMapScreenOpened;
	}

	private static void OnMapScreenOpened()
	{
		if (!_pendingHighlight) return;
		_pendingHighlight = false;
		var mapScreen = NMapScreen.Instance;
		if (mapScreen != null)
			mapScreen.Opened -= OnMapScreenOpened;
		HighlightPaths();
	}

	private static HashSet<(MapCoord, MapCoord)> GetPathSegments(List<MapPoint>? path)
	{
		var segments = new HashSet<(MapCoord, MapCoord)>();
		if (path == null || path.Count < 2) return segments;
		for (int i = 0; i < path.Count - 1; i++)
			segments.Add((path[i].coord, path[i + 1].coord));
		return segments;
	}

	private static void HighlightPaths()
	{
		if (!_reflectionInitialized) return;
		var mapScreen = NMapScreen.Instance;
		if (mapScreen == null || !mapScreen.IsOpen) return;

		ClearPathHighlighting();

		var paths = _pathsField!.GetValue(mapScreen) as IDictionary;
		if (paths == null) return;

		var safestSegs = GetPathSegments(_cachedSafestPath);
		var aggressiveSegs = GetPathSegments(_cachedAggressivePath);
		var sharedSegs = new HashSet<(MapCoord, MapCoord)>(safestSegs);
		sharedSegs.IntersectWith(aggressiveSegs);

		foreach (var seg in aggressiveSegs.Except(sharedSegs))
			ApplySegmentHighlight(paths, seg, AggressiveColor);
		foreach (var seg in safestSegs.Except(sharedSegs))
			ApplySegmentHighlight(paths, seg, SafestColor);
		foreach (var seg in sharedSegs)
			ApplySegmentHighlight(paths, seg, SharedColor);
	}

	private static void ApplySegmentHighlight(IDictionary paths, (MapCoord, MapCoord) seg, Color color)
	{
		TryHighlightSegment(paths, seg, color);
		TryHighlightSegment(paths, (seg.Item2, seg.Item1), color);
	}

	private static void TryHighlightSegment(IDictionary paths, (MapCoord, MapCoord) key, Color color)
	{
		if (!paths.Contains(key)) return;
		if (paths[key] is not IReadOnlyList<TextureRect> ticks) return;
		foreach (var tick in ticks)
		{
			if (tick == null || !GodotObject.IsInstanceValid(tick)) continue;
			if (!_originalTickProps.ContainsKey(tick))
				_originalTickProps[tick] = (tick.Modulate, tick.Scale);
			tick.Modulate = color;
			tick.Scale = new Vector2(1.4f, 1.4f);
		}
	}

	private static void ClearPathHighlighting()
	{
		var toRemove = new List<TextureRect>();
		foreach (var kvp in _originalTickProps)
		{
			if (kvp.Key != null && GodotObject.IsInstanceValid(kvp.Key))
			{
				kvp.Key.Modulate = kvp.Value.color;
				kvp.Key.Scale = kvp.Value.scale;
			}
			else
			{
				toRemove.Add(kvp.Key);
			}
		}
		foreach (var tick in toRemove)
			_originalTickProps.Remove(tick);
		_originalTickProps.Clear();
	}
	
	
	private static Dictionary<string, List<CardData>> GetPlayerCards()
	{
		var run = RunManager.Instance.DebugOnlyGetState();
		var player = LocalContext.GetMe(run);

		//                             pile    card list
		var cardsData = new Dictionary<string, List<CardData>>();
		
		foreach (CardPile pile in player.Piles)
		{
			if (pile?.Cards == null) continue;

			foreach (CardModel model in pile.Cards)
			{

				var cardData = new CardData()
				{
					Description = model.GetDescriptionForPile(pile.Type),
					Title = model.Title,
					EnergyCost = model.EnergyCost.CostsX ? "X" : model.EnergyCost.Canonical.ToString(),
				};
				Log.Info($"[MCP] [{pile.Type.ToString()}] {cardData.Title} {cardData.EnergyCost}: {cardData.Description}");

				var key = pile.Type.ToString();
				if (!cardsData.ContainsKey(key))
				{
					cardsData[key] = new List<CardData>();
				}
				cardsData[key].Add(cardData);
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
