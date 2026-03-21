using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Logging;

namespace STS2MCP;

public class PlayCardRequest
{
	public int card_index { get; set; }
	public int? target_index { get; set; }
}

public class SelectCharacterRequest
{
	public string character { get; set; } = "";
}

public static partial class Mod
{
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
				HandleApiRequest(segments, method, request, response);
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

	private static void HandleApiRequest(string[] segments, string method, HttpListenerRequest request, HttpListenerResponse response)
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
				case "playcard":
					if (method == "POST")
					{
						using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
						{
							string body = reader.ReadToEnd();
							Log.Info($"[MCP] Body: {body}");
							var data = JsonSerializer.Deserialize<PlayCardRequest>(body);
							Log.Info($"[MCP] Sending playCard request: card={data.card_index} enemy={data.target_index?.ToString() ?? "none"}");
							var play_card_result = RunOnMainThread(() => PlayCardAction(data)).GetAwaiter().GetResult();
							
							SendJson(response, play_card_result);
						}
					}
					else
					{
						Log.Info("[MCP] Sending invalid method for playCard");
						response.StatusCode = 405; // Method not allowed
						response.Close();
					}
					break;
				case "end_turn":
					if (method == "GET")
					{
						try
						{
							var end_turn_result = RunOnMainThread(() => TryEndTurn());
							SendJson(response, end_turn_result);
						}
						catch (Exception e)
						{
							Log.Error($"[MCP] Failed to end turn: {e}");
							SendJson(response, new { message = "Cannot end turn" });
						}
						
					}

					break;
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
						if (!CombatManager.Instance.IsInProgress)
							SendJson(response, new { message = "Enemy data is not available yet" });
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
				case "select_character":
				if (method == "POST")
				{
					using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
					var body = reader.ReadToEnd();
					var data = JsonSerializer.Deserialize<SelectCharacterRequest>(body);
					var result = RunOnMainThread(() => SelectAndStartCharacter(data!.character)).GetAwaiter().GetResult();
					SendJson(response, result);
				}
				break;
			case "enter_char_select":
					if (method == "GET")
					{
						try
						{
							
							var t = RunOnMainThread(() => StartStandardGame());
							var standard_game = t.GetAwaiter().GetResult();
							SendJson(response, new { message = "Got to character select for standard game"});
						}
						catch (Exception e) {
							Log.Error($"[MCP] Failed to get character select screen: {e}");
							SendJson(response, new { message = "Character screen could not be navigated to" });
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
}