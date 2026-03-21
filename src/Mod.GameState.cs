using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;

namespace STS2MCP;

public static partial class Mod
{
	private static object GetEnemyInfo()
	{
		var combatState = CombatManager.Instance.DebugOnlyGetState();
		var enemiesData = new List<object>();

		if (CombatManager.Instance.IsOverOrEnding || combatState?.Enemies == null || combatState.Enemies.Count == 0)
			return enemiesData; // empty list if no enemies

		var targetIndex = 0;
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
				Index = targetIndex++,
			});
		}

		return enemiesData;
	}

	private static object TryEndTurn()
	{
		if (CombatManager.Instance.IsOverOrEnding)
		{
			return new { Success = false, Message = "tried to end turn after combat" };
		}
		var run = RunManager.Instance.DebugOnlyGetState();
		var player = LocalContext.GetMe(run);
		if (!CombatManager.Instance.AllPlayersReadyToEndTurn())
		{
			return new { Success = false, Message = "was not ready to end turn" };
		}
		CombatManager.Instance.SetReadyToEndTurn(player,false);
		return new { Success = true, Message = "successfully signalled end of turn" };
	}

	private static object PlayCardAction(PlayCardRequest pcr)
	{
		Creature? creature = null;
		if (pcr.target_index != null)
		{
			if (!TryGetTargetCreature(pcr.target_index ?? 0, out creature))
			{
				return new { Success = false, Message = "target creature index out of bounds" };
			}
		}


		var run = RunManager.Instance.DebugOnlyGetState();
		var player = LocalContext.GetMe(run);
		foreach (CardPile pile in player.Piles)
		{
			if (pile.Type != PileType.Hand)
			{
				continue;
			}


			if (pcr.card_index < 0 || pcr.card_index >= pile.Cards.Count)
			{
				return new { Success = false, Message = "card index out of bounds" };
			}
			// Play the card via the action queue (same path as the game UI)
			RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(pile.Cards[pcr.card_index], creature));
			break;
		}
		return new { Success = true };
	}
	private static bool TryGetTargetCreature(int index,out Creature creature)
	{
		var combatState = CombatManager.Instance.DebugOnlyGetState();
		var enemies = combatState.Enemies.ToList();
		if (index < 0 || enemies.Count <= index)
		{
			creature = null!;
			return false;
		}
		creature = enemies[index];
		return true;
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
	
	private static Dictionary<string, List<CardData>> GetPlayerCards()
	{
		var run = RunManager.Instance.DebugOnlyGetState();
		var player = LocalContext.GetMe(run);

		//                             pile    card list
		var cardsData = new Dictionary<string, List<CardData>>();
		
		foreach (CardPile pile in player.Piles)
		{
			if (pile?.Cards == null) continue;

			var cardIndex = 0;
			foreach (CardModel model in pile.Cards)
			{

				var cardData = new CardData()
				{
					Description = model.GetDescriptionForPile(pile.Type),
					Title = model.Title,
					EnergyCost = model.EnergyCost.CostsX ? "X" : model.EnergyCost.Canonical.ToString(),
					CardIndex = cardIndex++,
				};

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

	private static object SelectAndStartCharacter(string characterName)
	{
		// Match against full id (e.g. "CHARACTER.IRONCLAD") or just the suffix (e.g. "IRONCLAD")
		var normalizedInput = characterName.Contains('.')
			? characterName.Split('.').Last()
			: characterName;
		var character = ModelDb.AllCharacters.FirstOrDefault(c =>
		{
			var id = c.Id.ToString();
			var idSuffix = id.Contains('.') ? id.Split('.').Last() : id;
			return string.Equals(id, characterName, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(idSuffix, normalizedInput, StringComparison.OrdinalIgnoreCase);
		});

		if (character == null)
		{
			var available = string.Join(", ", ModelDb.AllCharacters.Select(c => c.Id.ToString()));
			return new { Success = false, Message = $"Character '{characterName}' not found. Available: {available}" };
		}

		if (NGame.Instance.MainMenu == null)
			return new { Success = false, Message = "Not on main menu" };

		// Navigate to character select (same flow as enter_char_select)
		var submenu = NGame.Instance.MainMenu.OpenSingleplayerSubmenu();
		if (submenu == null)
			return new { Success = false, Message = "Could not open singleplayer submenu" };

		var openCharSelect = typeof(NSingleplayerSubmenu).GetMethod(
			"OpenCharacterSelect", BindingFlags.NonPublic | BindingFlags.Instance);
		openCharSelect?.Invoke(submenu, new object?[] { null });

		// Walk up the type hierarchy to find _stack (defined on NSubmenu base class)
		FieldInfo? stackField = null;
		for (var t = submenu.GetType(); t != null && stackField == null; t = t.BaseType)
			stackField = t.GetField("_stack", BindingFlags.NonPublic | BindingFlags.Instance);

		var stack = stackField?.GetValue(submenu);
		if (stack == null)
			return new { Success = false, Message = "Could not access submenu stack" };

		// GetSubmenuType<NCharacterSelectScreen>() to get the screen from the stack
		var getSubmenuType = stack.GetType()
			.GetMethods(BindingFlags.Public | BindingFlags.Instance)
			.FirstOrDefault(m => m.Name == "GetSubmenuType"
				&& m.IsGenericMethodDefinition
				&& m.GetGenericArguments().Length == 1
				&& m.GetParameters().Length == 0)
			?.MakeGenericMethod(typeof(NCharacterSelectScreen));
		var screen = getSubmenuType?.Invoke(stack, null) as NCharacterSelectScreen;
		if (screen == null)
			return new { Success = false, Message = "Could not get character select screen from stack" };

		// Find the button for this character by reflecting into _charButtonContainer
		NCharacterSelectButton? matchingButton = null;
		var containerField = typeof(NCharacterSelectScreen).GetField(
			"_charButtonContainer", BindingFlags.NonPublic | BindingFlags.Instance);
		if (containerField?.GetValue(screen) is Godot.Node container)
		{
			foreach (var btn in container.GetChildren().OfType<NCharacterSelectButton>())
			{
				// Find any field/property on the button that holds a CharacterModel
				var charMember = btn.GetType()
					.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
					.FirstOrDefault(f => f.FieldType == typeof(CharacterModel));
				if (charMember?.GetValue(btn) as CharacterModel == character)
				{
					matchingButton = btn;
					break;
				}
			}
		}

		if (matchingButton == null)
			return new { Success = false, Message = $"Could not find button for character {character.Id}" };

		screen.SelectCharacter(matchingButton, character);

		var embark = typeof(NCharacterSelectScreen).GetMethod(
			"OnEmbarkPressed", BindingFlags.NonPublic | BindingFlags.Instance);
		embark?.Invoke(screen, new object?[] { null });

		return new { Success = true, Message = $"Started game as {character.Id}" };
	}
}