using System;
using System.Net;
using System.Text.Json;
using System.Threading;
using MegaCrit.Sts2.Core.Logging;

namespace STS2MCP;

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
}