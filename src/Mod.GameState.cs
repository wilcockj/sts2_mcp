using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace STS2MCP;

public static partial class Mod
{
	private static object GetEnemyInfo()
	{
		var combatState = CombatManager.Instance.DebugOnlyGetState();
		var enemiesData = new List<object>();

		if (combatState?.Enemies == null || combatState.Enemies.Count == 0)
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
	private static bool TryGetTargetCreature(int index,out Creature creature)
	{
		var combatState = CombatManager.Instance.DebugOnlyGetState();
		var enemies = combatState.Enemies.ToList();
		if (index < 0 || enemies.Count >= index)
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
}