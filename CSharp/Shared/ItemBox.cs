using Barotrauma;
using HarmonyLib;
using System.Reflection;
using System.Linq;
using Barotrauma.Items.Components;
using System.Collections.Generic;
using System;
using Microsoft.Xna.Framework;
using System.ComponentModel;
using Barotrauma.Networking;

namespace BaroMod_sjx
{
	partial class ItemBoxImpl : ACsMod
	{
		const string harmony_id = "com.sjx.ItemIOFramework";
		/*
		const string box_identifier = "ItemBox";
		const float max_condition = 1.0f;
		const int item_count = 1024;
		const float increment = max_condition / item_count;
		*/
		private readonly Harmony harmony;

		public ItemBoxImpl()
		{
			harmony = new Harmony(harmony_id);
			harmony.PatchAll(Assembly.GetExecutingAssembly());
			Barotrauma.DebugConsole.AddWarning("Loaded ItemBox Impl");
		}

		public override void Stop()
		{
			harmony.UnpatchAll(harmony_id);
		}



		static Dictionary<Type, ItemComponent> get_componentsByType(Item item)
		{
			return (AccessTools.Field(typeof(Item), "componentsByType").GetValue(item)! as Dictionary<Type, ItemComponent>)!;
		}

		[HarmonyPatch(typeof(Inventory))]
		class Patch_PutItem
		{
			static MethodBase TargetMethod()
			{
				Barotrauma.DebugConsole.AddWarning("Patch_PutItem TargetMethod");
				return AccessTools.Method(typeof(Inventory), "PutItem");
			}

			public class context
			{
				public Character user;
				public ConditionStorage target;
				public context(Character user, ConditionStorage target)
				{
					this.user = user;
					this.target = target;
				}
			}

			public static bool Prefix(Inventory __instance, Character user, int i, out context? __state)
			{
				__state = null;
				ConditionStorage? target = ConditionStorage.GetFromInventory(__instance);
				if (target != null && i == target.slotIndex)
				{
					__state = new context(user, target);
				}
				return true;
			}
			public static void Postfix(context? __state)
			{
				if (__state != null)
				{
					__state.target.OnPutItemDone(__state.user);
				}
			}
		}

		[HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem))]
		class Patch_RemoveItem
		{
			public static bool Prefix(Inventory __instance, out ConditionStorage? __state, Item item)
			{
				__state = null;
				// do not add items if sub is unloading or if removed for overflow.
				if (!Submarine.Unloading)
				{
					ConditionStorage? target = ConditionStorage.GetFromInventory(__instance);
					if (target != null)
					{
						if (target.GetSlot()?.Contains(item) ?? false)
						{
							if (target.flag_remove_no_spawn)
							{
								target.flag_remove_no_spawn = false;
							}
							else
							{
								target.QualityStacked = item.Quality;
								target.ConditionStacked = item.Condition;
								target.item_type = item.Prefab;
								__state = target;
							}
						}
					}
				}
				return true;
			}
			public static void Postfix(ConditionStorage? __state)
			{
				if (__state != null)
				{
					__state.OnRemoveItemDone();
				}
			}
		}

		[HarmonyPatch(typeof(Inventory))]
		class Patch_TrySwapping
		{
			static MethodBase TargetMethod()
			{
				return AccessTools.Method(typeof(Inventory), "TrySwapping");
			}

			public static bool Prefix(Inventory __instance, Item item, ref bool __result)
			{
				if (ConditionStorage.GetFromInventory(__instance) != null ||
					(item != null && item.ParentInventory != null && ConditionStorage.GetFromInventory(item.ParentInventory) != null))
				{
					__result = false;
					return false;
				}
				return true;
			}
		}

		[HarmonyPatch(typeof(Inventory))]
		class Patch_CreateNetworkEvent
		{
			static MethodBase TargetMethod()
			{
				return AccessTools.Method(typeof(Inventory), "CreateNetworkEvent");
			}

			public static bool Prefix(Inventory __instance, out ConditionStorage? __state)
			{
				__state = null;
				if (GameMain.NetworkMember != null)
				{
					__state = ConditionStorage.GetFromInventory(__instance);
				}
				return true;
			}

			public static void Postfix(ConditionStorage? __state)
			{
				if (__state != null)
				{
					__state.SyncItemCount();
				}
			}
		}
	}

	partial class ConditionStorage : ItemComponent
	{
		private readonly struct EventData : IEventData
		{
			public readonly int ItemCount;

			public EventData(int ItemCount)
			{
				this.ItemCount = ItemCount;
			}
		}

		[Serialize(0, IsPropertySaveable.No, description: "Index of the stacking slot in same item's ItemContainer component")]
		public int slotIndex { get; private set; }

		[Serialize(true, IsPropertySaveable.No, description: "Shows count and percentage of stacking item")]
		public bool showCount { get; private set; }

		[Serialize(1024, IsPropertySaveable.No, description: "Maximum number of items stacked within")]
		public int maxItemCount { get; private set; }

		[Serialize(true, IsPropertySaveable.No, description: "Shows icon of stacking item")]
		public bool showIcon { get; private set; }

		[Serialize(0.6f, IsPropertySaveable.No, description: "icon scale compared to full")]
		public float iconScale { get; private set; }

		[Serialize(0.0f, IsPropertySaveable.No, description: "shift x of icon")]
		public float iconShiftX { get; private set; }

		[Serialize(0.1f, IsPropertySaveable.No, description: "shift y of icon, down is positive")]
		public float iconShiftY { get; private set; }

		[Editable(minValue: 0, maxValue: int.MaxValue), Serialize(0, IsPropertySaveable.Yes, description: "Current item count")]
		// camel case needed for save compatibility
		public int currentItemCount
		{
			get => _currentItemCount;
			// assume set by 
			set
			{
				SetItemCount(value, false);
			}
		}

		void SetItemCount(int value, bool is_network_event = false)
		{
			if (is_network_event || GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
			// authoritative number. will need to send to client later if server.
			{
				if (value != _currentItemCount)
				{
					OnCountActualChanged();
				}
			}
			// predicted number. need to be reset later
			else
			{
				if (value != _currentItemCount)
				{
					OnCountPredictionChanged();
				}
			}
			IsActive = true;
			_currentItemCount = value;
		}

		public ItemInventory itemInventory => Item.OwnInventory;
		public ItemContainer itemContainer => Item.GetComponent<ItemContainer>();

		public int _currentItemCount;

		// replace setting parent container hack, so that harpoon guns work correctly
		public bool flag_remove_no_spawn;

		partial void OnCountActualChanged();
		partial void OnCountPredictionChanged();


		[Editable, Serialize("", IsPropertySaveable.Yes, description: "current stacked item")]
		public Identifier ItemIdentifier
		{
			get
			{
				return item_type?.Identifier ?? "";
			}
			set
			{
				if (value.IsEmpty)
				{
					item_type = null;
				}
				else
				{
					item_type = ItemPrefab.Find("", value.ToIdentifier());
				}
			}
		}

		public ItemPrefab? item_type;

		[Editable(MinValueInt = 0, MaxValueInt = Quality.MaxQuality), Serialize(0, IsPropertySaveable.Yes, description: "current stacked item quality")]
		public int QualityStacked { get; set; }

		[Editable, Serialize(float.NaN, IsPropertySaveable.Yes, description: "current stacked item condition")]
		public float ConditionStacked { get; set; }



		public ConditionStorage(Item item, ContentXElement element) : base(item, element) { }

		public bool IsFull => currentItemCount >= maxItemCount;
		public bool IsEmpty() => currentItemCount <= 0;

		public void SyncItemCount()
		{
#if SERVER
			Item.CreateServerEvent(this, new EventData(currentItemCount));
#endif
		}

		public override void Update(float deltaTime, Camera cam)
		{
			base.Update(deltaTime, cam);
			SyncItemCount();
			IsActive = false;
		}

		public static int SlotPreserveCount(ItemPrefab prefab, Inventory inventory, ItemContainer container, int slot_index)
		{
			int resolved_stack_size = Math.Min(Math.Min(prefab.GetMaxStackSize(inventory), container.GetMaxStackSize(slot_index)), Inventory.MaxPossibleStackSize);
			if (resolved_stack_size <= 1)
			{
				return 1;
			}
			else
			{
				return resolved_stack_size - 1;
			}
		}

		public static ConditionStorage? GetFromInventory(Inventory inventory)
		{
			if (inventory.Owner is Item parentItem)
			{
				return parentItem.GetComponent<ConditionStorage>();
			}
			else
			{
				return null;
			}
		}

		public Inventory.ItemSlot? GetSlot()
		{
			Inventory.ItemSlot[] slots = (AccessTools.Field(typeof(Inventory), "slots").GetValue(itemInventory)! as Inventory.ItemSlot[])!;
			if (slotIndex >= slots.Length)
			{
				DebugConsole.LogError($"ConditionStorage of {Item.Prefab.Identifier} specified index {slotIndex} out of {slots.Length}!");
				return null;
			}
			return slots[slotIndex];
		}

		public void OnPutItemDone(Character user)
		{
			ItemContainer container = itemContainer;
			Inventory.ItemSlot target_slot;
			{
				Inventory.ItemSlot[] slots = (AccessTools.Field(typeof(Inventory), "slots").GetValue(itemInventory)! as Inventory.ItemSlot[])!;
				if (slotIndex >= slots.Length)
				{
					DebugConsole.LogError($"ConditionStorage of {Item.Prefab.Identifier} specified index {slotIndex} out of {slots.Length}!");
					return;
				}
				target_slot = slots[slotIndex];
			}

			if (target_slot.Items.Any())
			{
				QualityStacked = target_slot.Items.First().Quality;
				ConditionStacked = target_slot.Items.First().Condition;
				item_type = target_slot.Items.First().Prefab;
				if (!IsFull)
				{
					//bool edited = false;	
					int preserve = SlotPreserveCount(target_slot.Items.First().Prefab, itemInventory, container, slotIndex);
					var it = target_slot.Items.ToArray().AsEnumerable().GetEnumerator();
					while (it.MoveNext() && !IsFull)
					{
						if (preserve > 0)
						{
							preserve--;
						}
						else if (Entity.Spawner != null)
						{
							// client cannot despawn items, single player needs to despawn
							Entity.Spawner.AddItemToRemoveQueue(it.Current);
							SetItemCount(currentItemCount + 1);
							flag_remove_no_spawn = true;
							itemInventory.RemoveItem(it.Current);
							break;
						}
					}
				}
			}
		}

		public void OnRemoveItemDone()
		{
			Inventory.ItemSlot target_slot;
			{
				Inventory.ItemSlot[] slots = (AccessTools.Field(typeof(Inventory), "slots").GetValue(itemInventory)! as Inventory.ItemSlot[])!;
				if (slotIndex >= slots.Length)
				{
					DebugConsole.LogError($"ConditionStorage of {(itemInventory.Owner as Item)!.Prefab.Identifier} specified index {slotIndex} out of {slots.Length}!");
					return;
				}
				target_slot = slots[slotIndex];
			}

			int preserve = SlotPreserveCount(item_type!, itemInventory, itemContainer, slotIndex);
			int spawn_count = preserve - target_slot.Items.Count;
			int can_spawn = Math.Min(spawn_count, currentItemCount);

			// other may be queued, so spawn only one
			if (can_spawn > 0)
			{
				if (Entity.Spawner != null)
				{
					SetItemCount(currentItemCount - 1);

					Item.Spawner.AddItemToSpawnQueue(item_type, itemInventory,
							ConditionStacked, QualityStacked, spawnIfInventoryFull: true);
				}
			}
		}
	}
}
