using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace ItemManager;

[PublicAPI]
public enum CraftingTable
{
	None,
	[InternalName("piece_workbench")] Workbench,
	[InternalName("piece_cauldron")] Cauldron,
	[InternalName("forge")] Forge,
	[InternalName("piece_artisanstation")] ArtisanTable,
	[InternalName("piece_stonecutter")] StoneCutter,
	Custom
}

public class InternalName : Attribute
{
	public readonly string internalName;
	public InternalName(string internalName) => this.internalName = internalName;
}

[PublicAPI]
public class RequiredResourceList
{
	public readonly List<Requirement> Requirements = new();

	public void Add(string itemName, int amount) => Requirements.Add(new Requirement { itemName = itemName, amount = amount });
}

[PublicAPI]
public class CraftingStationList
{
	public readonly List<CraftingStationConfig> Stations = new();

	public void Add(CraftingTable table, int level) => Stations.Add(new CraftingStationConfig { Table = table, level = level });
	public void Add(string customTable, int level) => Stations.Add(new CraftingStationConfig { Table = CraftingTable.Custom, level = level, custom = customTable });
}

public struct Requirement
{
	public string itemName;
	public int amount;
}

public struct CraftingStationConfig
{
	public CraftingTable Table;
	public int level;
	public string? custom;
}

[PublicAPI]
public class Item
{
	private class ItemConfig
	{
		public ConfigEntry<string> craft = null!;
		public ConfigEntry<string>? upgrade;
		public ConfigEntry<CraftingTable> table = null!;
		public ConfigEntry<int> tableLevel = null!;
		public ConfigEntry<string> customTable = null!;
		public ConfigEntry<int>? maximumTableLevel;
	}

	private static readonly List<Item> registeredItems = new();
	private static readonly Dictionary<ItemDrop, Item> itemDropMap = new();
	private static Dictionary<Item, List<Recipe>> activeRecipes = new();
	private static Dictionary<Item, ItemConfig> itemConfigs = new();

	public static bool ConfigurationEnabled = true;

	public readonly GameObject Prefab;

	/// <summary>
	/// Specifies the resources needed to craft the item.
	/// <para>Use .Add to add resources with their internal ID and an amount.</para>
	/// <para>Use one .Add for each resource type the item should need.</para>
	/// </summary>
	public readonly RequiredResourceList RequiredItems = new();

	/// <summary>
	/// Specifies the resources needed to upgrade the item.
	/// <para>Use .Add to add resources with their internal ID and an amount. This amount will be multipled by the item quality level.</para>
	/// <para>Use one .Add for each resource type the item should need.</para>
	/// </summary>
	public readonly RequiredResourceList RequiredUpgradeItems = new();

	/// <summary>
	/// Specifies the crafting station needed to craft the item.
	/// <para>Use .Add to add a crafting station, using the CraftingTable enum and a minimum level for the crafting station.</para>
	/// <para>Use one .Add for each crafting station.</para>
	/// </summary>
	public readonly CraftingStationList Crafting = new();

	/// <summary>
	/// Specifies the number of items that should be given to the player with a single craft of the item.
	/// <para>Defaults to 1.</para>
	/// </summary>
	public int CraftAmount = 1;

	/// <summary>
	/// Specifies the maximum required crafting station level to upgrade and repair the item.
	/// <para>Default is calculated from crafting station level and maximum quality.</para>
	/// </summary>
	public int MaximumRequiredStationLevel = int.MaxValue;

	private LocalizeKey? _name;

	public LocalizeKey Name
	{
		get
		{
			if (_name is { } name)
			{
				return name;
			}

			ItemDrop.ItemData.SharedData data = Prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
			if (data.m_name.StartsWith("$"))
			{
				_name = new LocalizeKey(data.m_name);
			}
			else
			{
				string key = "$item_" + Prefab.name.Replace(" ", "_");
				_name = new LocalizeKey(key).English(data.m_name);
				data.m_name = key;
			}
			return _name;
		}
	}

	private LocalizeKey? _description;

	public LocalizeKey Description
	{
		get
		{
			if (_description is { } description)
			{
				return description;
			}

			ItemDrop.ItemData.SharedData data = Prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
			if (data.m_description.StartsWith("$"))
			{
				_description = new LocalizeKey(data.m_description);
			}
			else
			{
				string key = "$itemdesc_" + Prefab.name.Replace(" ", "_");
				_description = new LocalizeKey(key).English(data.m_description);
				data.m_description = key;
			}
			return _description;
		}
	}

	public Item(string assetBundleFileName, string prefabName, string folderName = "assets") : this(PrefabManager.RegisterAssetBundle(assetBundleFileName, folderName), prefabName)
	{
	}

	public Item(AssetBundle bundle, string prefabName)
	{
		Prefab = PrefabManager.RegisterPrefab(bundle, prefabName, true);
		registeredItems.Add(this);
		itemDropMap[Prefab.GetComponent<ItemDrop>()] = this;
	}

	private class ConfigurationManagerAttributes
	{
		[UsedImplicitly] public int? Order;
		[UsedImplicitly] public bool? Browsable;
		[UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer;
	}

	internal static void Patch_FejdStartup()
	{
		Assembly? bepinexConfigManager = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "ConfigurationManager");

		Type? configManagerType = bepinexConfigManager?.GetType("ConfigurationManager.ConfigurationManager");
		object? configManager = configManagerType == null ? null : BepInEx.Bootstrap.Chainloader.ManagerObject.GetComponent(configManagerType);

		void reloadConfigDisplay() => configManagerType?.GetMethod("BuildSettingList")!.Invoke(configManager, Array.Empty<object>());

		if (ConfigurationEnabled)
		{
			bool SaveOnConfigSet = plugin.Config.SaveOnConfigSet;
			plugin.Config.SaveOnConfigSet = false;

			foreach (Item item in registeredItems)
			{
				ItemConfig cfg = itemConfigs[item] = new ItemConfig();

				if (item.Crafting.Stations.Count > 0)
				{
					string localizedName = new Regex("['[\"\\]]").Replace(english.Localize(item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name), "").Trim();
					int order = 0;

					List<ConfigurationManagerAttributes> hideWhenNoneAttributes = new();

					cfg.table = config(localizedName, "Crafting Station", item.Crafting.Stations.First().Table, new ConfigDescription($"Crafting station where {localizedName} is available.", null, new ConfigurationManagerAttributes { Order = --order }));
					ConfigurationManagerAttributes customTableAttributes = new() { Order = --order, Browsable = cfg.table.Value == CraftingTable.Custom };
					cfg.customTable = config(localizedName, "Custom Crafting Station", item.Crafting.Stations.First().custom ?? "", new ConfigDescription("", null, customTableAttributes));

					void TableConfigChanged(object o, EventArgs e)
					{
						if (activeRecipes.Count > 0)
						{
							if (cfg.table.Value is CraftingTable.None)
							{
								activeRecipes[item].First().m_craftingStation = null;
							}
							else if (cfg.table.Value is CraftingTable.Custom)
							{
								activeRecipes[item].First().m_craftingStation = ZNetScene.instance.GetPrefab(cfg.customTable.Value)?.GetComponent<CraftingStation>();
							}
							else
							{
								activeRecipes[item].First().m_craftingStation = ZNetScene.instance.GetPrefab(((InternalName)typeof(CraftingTable).GetMember(cfg.table.Value.ToString())[0].GetCustomAttributes(typeof(InternalName)).First()).internalName).GetComponent<CraftingStation>();
							}
						}
						customTableAttributes.Browsable = cfg.table.Value == CraftingTable.Custom;
						foreach (ConfigurationManagerAttributes attributes in hideWhenNoneAttributes)
						{
							attributes.Browsable = cfg.table.Value != CraftingTable.None;
						}
						reloadConfigDisplay();
					}
					cfg.table.SettingChanged += TableConfigChanged;
					cfg.customTable.SettingChanged += TableConfigChanged;

					ConfigurationManagerAttributes tableLevelAttributes = new() { Order = --order, Browsable = cfg.table.Value != CraftingTable.None };
					hideWhenNoneAttributes.Add(tableLevelAttributes);
					cfg.tableLevel = config(localizedName, "Crafting Station Level", item.Crafting.Stations.First().level, new ConfigDescription($"Required crafting station level to craft {localizedName}.", null, tableLevelAttributes));
					cfg.tableLevel.SettingChanged += (_, _) =>
					{
						if (activeRecipes.Count > 0)
						{
							activeRecipes[item].First().m_minStationLevel = cfg.tableLevel.Value;
						}
					};
					if (item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxQuality > 1)
					{
						cfg.maximumTableLevel = config(localizedName, "Maximum Crafting Station Level", item.MaximumRequiredStationLevel == int.MaxValue ? item.Crafting.Stations.First().level + item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxQuality - 1 : item.MaximumRequiredStationLevel, new ConfigDescription($"Maximum crafting station level to upgrade and repair {localizedName}.", null, tableLevelAttributes));
					}

					ConfigEntry<string> itemConfig(string name, string value, string desc)
					{
						ConfigurationManagerAttributes attributes = new() { CustomDrawer = drawConfigTable, Order = --order, Browsable = cfg.table.Value != CraftingTable.None };
						hideWhenNoneAttributes.Add(attributes);
						return config(localizedName, name, value, new ConfigDescription(desc, null, attributes));
					}

					cfg.craft = itemConfig("Crafting Costs", new SerializedRequirements(item.RequiredItems.Requirements).ToString(), $"Item costs to craft {localizedName}");
					if (item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxQuality > 1)
					{
						cfg.upgrade = itemConfig("Upgrading Costs", new SerializedRequirements(item.RequiredUpgradeItems.Requirements).ToString(), $"Item costs per level to upgrade {localizedName}");
					}

					void ConfigChanged(object o, EventArgs e)
					{
						if (ObjectDB.instance && activeRecipes.ContainsKey(item))
						{
							foreach (Recipe recipe in activeRecipes[item])
							{
								recipe.m_resources = SerializedRequirements.toPieceReqs(ObjectDB.instance, new SerializedRequirements(cfg.craft.Value), new SerializedRequirements(cfg.upgrade?.Value ?? ""));
							}
						}
					}

					cfg.craft.SettingChanged += ConfigChanged;
					if (cfg.upgrade != null)
					{
						cfg.upgrade.SettingChanged += ConfigChanged;
					}
				}
			}

			if (SaveOnConfigSet)
			{
				plugin.Config.SaveOnConfigSet = true;
				plugin.Config.Save();
			}
		}
	}

	[HarmonyPriority(Priority.VeryHigh)]
	internal static void Patch_ObjectDBInit(ObjectDB __instance)
	{
		if (__instance.GetItemPrefab("Wood") == null)
		{
			return;
		}

		foreach (Item item in registeredItems)
		{
			List<Recipe> recipes = new();

			itemConfigs.TryGetValue(item, out ItemConfig? cfg);
			foreach (CraftingStationConfig station in item.Crafting.Stations)
			{
				Recipe recipe = ScriptableObject.CreateInstance<Recipe>();
				recipe.name = $"{item.Prefab.name}_Recipe_{station.Table.ToString()}";
				recipe.m_amount = item.CraftAmount;
				recipe.m_enabled = true;
				recipe.m_item = item.Prefab.GetComponent<ItemDrop>();
				recipe.m_resources = SerializedRequirements.toPieceReqs(__instance, cfg == null ? new SerializedRequirements(item.RequiredItems.Requirements) : new SerializedRequirements(cfg.craft.Value), cfg == null ? new SerializedRequirements(item.RequiredUpgradeItems.Requirements) : new SerializedRequirements(cfg.upgrade?.Value ?? ""));
				if ((cfg == null || recipes.Count > 0 ? station.Table : cfg.table.Value) is CraftingTable.None)
				{
					recipe.m_craftingStation = null;
				}
				else if ((cfg == null || recipes.Count > 0 ? station.Table : cfg.table.Value) is CraftingTable.Custom)
				{
					if (ZNetScene.instance.GetPrefab(cfg == null || recipes.Count > 0 ? station.custom : cfg.customTable.Value) is { } craftingTable)
					{
						recipe.m_craftingStation = craftingTable.GetComponent<CraftingStation>();
					}
					else
					{
						Debug.LogWarning($"Custom crafting station '{(cfg == null || recipes.Count > 0 ? station.custom : cfg.customTable.Value)}' does not exist");
					}
				}
				else
				{
					recipe.m_craftingStation = ZNetScene.instance.GetPrefab(((InternalName)typeof(CraftingTable).GetMember((cfg == null || recipes.Count > 0 ? station.Table : cfg.table.Value).ToString())[0].GetCustomAttributes(typeof(InternalName)).First()).internalName).GetComponent<CraftingStation>();
				}
				recipe.m_minStationLevel = cfg == null || recipes.Count > 0 ? station.level : cfg.tableLevel.Value;

				recipes.Add(recipe);
			}

			activeRecipes[item] = recipes;

			__instance.m_recipes.AddRange(recipes);
		}
	}

	internal static void Patch_MaximumRequiredStationLevel(Recipe __instance, ref int __result, int quality)
	{
		if (itemDropMap.TryGetValue(__instance.m_item, out Item item))
		{
			__result = Mathf.Min(Mathf.Max(1, __instance.m_minStationLevel) + (quality - 1), itemConfigs[item].maximumTableLevel?.Value ?? item.MaximumRequiredStationLevel);
		}
	}

	private static bool CheckItemIsUpgrade(InventoryGui gui) => gui.m_selectedRecipe.Value?.m_quality > 0;

	internal static IEnumerable<CodeInstruction> Transpile_InventoryGui(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> instrs = instructions.ToList();
		FieldInfo amountField = AccessTools.DeclaredField(typeof(Recipe), nameof(Recipe.m_amount));
		for (int i = 0; i < instrs.Count; ++i)
		{
			yield return instrs[i];
			if (i > 1 && instrs[i - 2].opcode == OpCodes.Ldfld && instrs[i - 2].OperandIs(amountField) && instrs[i - 1].opcode == OpCodes.Ldc_I4_1 && instrs[i].operand is Label)
			{
				yield return new CodeInstruction(OpCodes.Ldarg_0);
				yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(Item), nameof(CheckItemIsUpgrade)));
				yield return new CodeInstruction(OpCodes.Brtrue, instrs[i].operand);
			}
		}
	}

	private static void drawConfigTable(ConfigEntryBase cfg)
	{
		bool locked = cfg.Description.Tags.Select(a => a.GetType().Name == "ConfigurationManagerAttributes" ? (bool?)a.GetType().GetField("ReadOnly")?.GetValue(a) : null).FirstOrDefault(v => v != null) ?? false;

		List<Requirement> newReqs = new();
		bool wasUpdated = false;

		GUILayout.BeginVertical();
		foreach (Requirement req in new SerializedRequirements((string)cfg.BoxedValue).Reqs)
		{
			GUILayout.BeginHorizontal();

			int amount = req.amount;
			if (int.TryParse(GUILayout.TextField(amount.ToString(), new GUIStyle(GUI.skin.textField) { fixedWidth = 40 }), out int newAmount) && newAmount != amount && !locked)
			{
				amount = newAmount;
				wasUpdated = true;
			}

			string newItemName = GUILayout.TextField(req.itemName);
			string itemName = locked ? req.itemName : newItemName;
			wasUpdated = wasUpdated || itemName != req.itemName;

			if (GUILayout.Button("x", new GUIStyle(GUI.skin.button) { fixedWidth = 21 }) && !locked)
			{
				wasUpdated = true;
			}
			else
			{
				newReqs.Add(new Requirement { amount = amount, itemName = itemName });
			}

			if (GUILayout.Button("+", new GUIStyle(GUI.skin.button) { fixedWidth = 21 }) && !locked)
			{
				wasUpdated = true;
				newReqs.Add(new Requirement { amount = 1, itemName = "" });
			}

			GUILayout.EndHorizontal();
		}
		GUILayout.EndVertical();

		if (wasUpdated)
		{
			cfg.BoxedValue = new SerializedRequirements(newReqs).ToString();
		}
	}

	private class SerializedRequirements
	{
		public readonly List<Requirement> Reqs;

		public SerializedRequirements(List<Requirement> reqs) => Reqs = reqs;

		public SerializedRequirements(string reqs)
		{
			Reqs = reqs.Split(',').Select(r =>
			{
				string[] parts = r.Split(':');
				return new Requirement { itemName = parts[0], amount = parts.Length > 1 && int.TryParse(parts[1], out int amount) ? amount : 1 };
			}).ToList();
		}

		public override string ToString()
		{
			return string.Join(",", Reqs.Select(r => $"{r.itemName}:{r.amount}"));
		}

		public static Piece.Requirement[] toPieceReqs(ObjectDB objectDB, SerializedRequirements craft, SerializedRequirements upgrade)
		{
			ItemDrop? ResItem(Requirement r)
			{
				ItemDrop? item = objectDB.GetItemPrefab(r.itemName)?.GetComponent<ItemDrop>();
				if (item == null)
				{
					Debug.LogWarning($"The required item '{r.itemName}' does not exist.");
				}
				return item;
			}

			Dictionary<string, Piece.Requirement?> resources = craft.Reqs.Where(r => r.itemName != "").ToDictionary(r => r.itemName, r => ResItem(r) is { } item ? new Piece.Requirement { m_amount = r.amount, m_resItem = item, m_amountPerLevel = 0 } : null);
			foreach (Requirement req in upgrade.Reqs.Where(r => r.itemName != ""))
			{
				if ((!resources.TryGetValue(req.itemName, out Piece.Requirement? requirement) || requirement == null) && ResItem(req) is { } item)
				{
					requirement = resources[req.itemName] = new Piece.Requirement { m_resItem = item, m_amount = 0 };
				}

				if (requirement != null)
				{
					requirement.m_amountPerLevel = req.amount;
				}
			}

			return resources.Values.Where(v => v != null).ToArray()!;
		}
	}

	private static Localization? _english;

	private static Localization english
	{
		get
		{
			if (_english == null)
			{
				_english = new Localization();
				_english.SetupLanguage("English");
			}

			return _english;
		}
	}

	private static BaseUnityPlugin? _plugin;

	private static BaseUnityPlugin plugin
	{
		get
		{
			if (_plugin is null)
			{
				IEnumerable<TypeInfo> types;
				try
				{
					types = Assembly.GetExecutingAssembly().DefinedTypes.ToList();
				}
				catch (ReflectionTypeLoadException e)
				{
					types = e.Types.Where(t => t != null).Select(t => t.GetTypeInfo());
				}
				_plugin = (BaseUnityPlugin)BepInEx.Bootstrap.Chainloader.ManagerObject.GetComponent(types.First(t => t.IsClass && typeof(BaseUnityPlugin).IsAssignableFrom(t)));
			}
			return _plugin;
		}
	}

	private static bool hasConfigSync = true;
	private static object? _configSync;

	private static object? configSync
	{
		get
		{
			if (_configSync == null && hasConfigSync)
			{
				if (Assembly.GetExecutingAssembly().GetType("ServerSync.ConfigSync") is { } configSyncType)
				{
					_configSync = Activator.CreateInstance(configSyncType, plugin.Info.Metadata.GUID + " ItemManager");
					configSyncType.GetField("CurrentVersion").SetValue(_configSync, plugin.Info.Metadata.Version.ToString());
					configSyncType.GetProperty("IsLocked")!.SetValue(_configSync, true);
				}
				else
				{
					hasConfigSync = false;
				}
			}

			return _configSync;
		}
	}

	private static ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description)
	{
		ConfigEntry<T> configEntry = plugin.Config.Bind(group, name, value, description);

		configSync?.GetType().GetMethod("AddConfigEntry")!.MakeGenericMethod(typeof(T)).Invoke(configSync, new object[] { configEntry });

		return configEntry;
	}

	private static ConfigEntry<T> config<T>(string group, string name, T value, string description) => config(group, name, value, new ConfigDescription(description));
}

[PublicAPI]
public class LocalizeKey
{
	public readonly string Key;

	public LocalizeKey(string key) => Key = key.Replace("$", "");

	public LocalizeKey English(string key) => addForLang("English", key);
	public LocalizeKey Swedish(string key) => addForLang("Swedish", key);
	public LocalizeKey French(string key) => addForLang("French", key);
	public LocalizeKey Italian(string key) => addForLang("Italian", key);
	public LocalizeKey German(string key) => addForLang("German", key);
	public LocalizeKey Spanish(string key) => addForLang("Spanish", key);
	public LocalizeKey Russian(string key) => addForLang("Russian", key);
	public LocalizeKey Romanian(string key) => addForLang("Romanian", key);
	public LocalizeKey Bulgarian(string key) => addForLang("Bulgarian", key);
	public LocalizeKey Macedonian(string key) => addForLang("Macedonian", key);
	public LocalizeKey Finnish(string key) => addForLang("Finnish", key);
	public LocalizeKey Danish(string key) => addForLang("Danish", key);
	public LocalizeKey Norwegian(string key) => addForLang("Norwegian", key);
	public LocalizeKey Icelandic(string key) => addForLang("Icelandic", key);
	public LocalizeKey Turkish(string key) => addForLang("Turkish", key);
	public LocalizeKey Lithuanian(string key) => addForLang("Lithuanian", key);
	public LocalizeKey Czech(string key) => addForLang("Czech", key);
	public LocalizeKey Hungarian(string key) => addForLang("Hungarian", key);
	public LocalizeKey Slovak(string key) => addForLang("Slovak", key);
	public LocalizeKey Polish(string key) => addForLang("Polish", key);
	public LocalizeKey Dutch(string key) => addForLang("Dutch", key);
	public LocalizeKey Portuguese_European(string key) => addForLang("Portuguese_European", key);
	public LocalizeKey Portuguese_Brazilian(string key) => addForLang("Portuguese_Brazilian", key);
	public LocalizeKey Chinese(string key) => addForLang("Chinese", key);
	public LocalizeKey Japanese(string key) => addForLang("Japanese", key);
	public LocalizeKey Korean(string key) => addForLang("Korean", key);
	public LocalizeKey Hindi(string key) => addForLang("Hindi", key);
	public LocalizeKey Thai(string key) => addForLang("Thai", key);
	public LocalizeKey Abenaki(string key) => addForLang("Abenaki", key);
	public LocalizeKey Croatian(string key) => addForLang("Croatian", key);
	public LocalizeKey Georgian(string key) => addForLang("Georgian", key);
	public LocalizeKey Greek(string key) => addForLang("Greek", key);
	public LocalizeKey Serbian(string key) => addForLang("Serbian", key);
	public LocalizeKey Ukrainian(string key) => addForLang("Ukrainian", key);

	private LocalizeKey addForLang(string lang, string value)
	{
		if (Localization.instance.GetSelectedLanguage() == lang)
		{
			Localization.instance.AddWord(Key, value);
		}
		else if (lang == "English" && !Localization.instance.m_translations.ContainsKey(Key))
		{
			Localization.instance.AddWord(Key, value);
		}
		return this;
	}
}

public static class PrefabManager
{
	static PrefabManager()
	{
		Harmony harmony = new("org.bepinex.helpers.ItemManager");
		harmony.Patch(AccessTools.DeclaredMethod(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB)), postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(PrefabManager), nameof(Patch_ObjectDBInit))));
		harmony.Patch(AccessTools.DeclaredMethod(typeof(ObjectDB), nameof(ObjectDB.Awake)), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(PrefabManager), nameof(Patch_ObjectDBInit))));
		harmony.Patch(AccessTools.DeclaredMethod(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB)), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Item), nameof(Item.Patch_ObjectDBInit))));
		harmony.Patch(AccessTools.DeclaredMethod(typeof(ObjectDB), nameof(ObjectDB.Awake)), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Item), nameof(Item.Patch_ObjectDBInit))));
		harmony.Patch(AccessTools.DeclaredMethod(typeof(FejdStartup), nameof(FejdStartup.Awake)), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Item), nameof(Item.Patch_FejdStartup))));
		harmony.Patch(AccessTools.DeclaredMethod(typeof(ZNetScene), nameof(ZNetScene.Awake)), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(PrefabManager), nameof(Patch_ZNetSceneAwake))));
		harmony.Patch(AccessTools.DeclaredMethod(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipe)), transpiler: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Item), nameof(Item.Transpile_InventoryGui))));
		harmony.Patch(AccessTools.DeclaredMethod(typeof(Recipe), nameof(Recipe.GetRequiredStationLevel)), postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Item), nameof(Item.Patch_MaximumRequiredStationLevel))));
	}

	private struct BundleId
	{
		[UsedImplicitly] public string assetBundleFileName;
		[UsedImplicitly] public string folderName;
	}

	private static readonly Dictionary<BundleId, AssetBundle> bundleCache = new();

	public static AssetBundle RegisterAssetBundle(string assetBundleFileName, string folderName = "assets")
	{
		BundleId id = new() { assetBundleFileName = assetBundleFileName, folderName = folderName };
		if (!bundleCache.TryGetValue(id, out AssetBundle assets))
		{
			assets = bundleCache[id] = Resources.FindObjectsOfTypeAll<AssetBundle>().FirstOrDefault(a => a.name == assetBundleFileName) ?? AssetBundle.LoadFromStream(Assembly.GetExecutingAssembly().GetManifestResourceStream(Assembly.GetExecutingAssembly().GetName().Name + $".{folderName}." + assetBundleFileName));
		}

		return assets;
	}

	private static readonly List<GameObject> prefabs = new();
	private static readonly List<GameObject> ZnetOnlyPrefabs = new();

	public static GameObject RegisterPrefab(string assetBundleFileName, string prefabName, string folderName = "assets") => RegisterPrefab(RegisterAssetBundle(assetBundleFileName, folderName), prefabName);

	public static GameObject RegisterPrefab(AssetBundle assets, string prefabName, bool addToObjectDb = false)
	{
		GameObject prefab = assets.LoadAsset<GameObject>(prefabName);

		if (addToObjectDb)
		{
			prefabs.Add(prefab);
		}
		else
		{
			ZnetOnlyPrefabs.Add(prefab);
		}

		return prefab;
	}

	[HarmonyPriority(Priority.VeryHigh)]
	private static void Patch_ObjectDBInit(ObjectDB __instance)
	{
		foreach (GameObject prefab in prefabs)
		{
			if (!__instance.m_items.Contains(prefab))
			{
				__instance.m_items.Add(prefab);
			}
		}

		__instance.UpdateItemHashes();
	}

	[HarmonyPriority(Priority.VeryHigh)]
	private static void Patch_ZNetSceneAwake(ZNetScene __instance)
	{
		foreach (GameObject prefab in prefabs.Concat(ZnetOnlyPrefabs))
		{
			__instance.m_prefabs.Add(prefab);
		}
	}
}