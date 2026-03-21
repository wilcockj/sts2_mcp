using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace STS2MCP;

public static partial class Mod
{
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
		var run = RunManager.Instance.DebugOnlyGetState();
		var startPoint = run.CurrentMapPoint ?? run.Map?.StartingMapPoint;
		if (startPoint == null)
		{
			Log.Info("[MCP] No start point in map search");
			return new { Error = "No map available" };
		}

		var currentPath = new List<MapPoint>();
		var allPaths = new List<List<MapPoint>>();
		FindAllPathsToBoss(startPoint, currentPath, allPaths);

		if (allPaths.Count == 0)
			return new { Error = "No paths found to boss" };

		var safestPath = allPaths.OrderByDescending(ScoreSafety).First();
		var aggressivePath = allPaths.OrderByDescending(ScoreAggressive).First();

		// Update cache for highlighting
		_cachedSafestPath = safestPath;
		_cachedAggressivePath = aggressivePath;
		RequestHighlightOnMapOpen();

		return new
		{
			SafestPath = FormatPath(safestPath.Skip(1).ToList(), "Safest (fewest fights)"),
			MostAggressivePath = FormatPath(aggressivePath.Skip(1).ToList(), "Most aggressive (max rewards)"),
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
}