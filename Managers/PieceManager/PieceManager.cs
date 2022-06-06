using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace PieceManager;

[PublicAPI]
public enum BuildPieceCategory
{
    Misc = 0,
    Crafting = 1,
    Building = 2,
    Furniture = 3,
    All = 100,
    Custom
}

[PublicAPI]
public class RequiredResourcesList
{
    public readonly List<Requirement> Requirements = new();

    public void Add(string item, int amount, bool recover) => Requirements.Add(new Requirement
        { itemName = item, amount = amount, recover = recover });
}

public struct Requirement
{
    public string itemName;
    public int amount;
    public bool recover;
}

[PublicAPI]
public class BuildingPieceCategoryList
{
    public readonly List<BuildPieceTableConfig> BuildPieceCategories = new();

    public void Add(BuildPieceCategory category) => BuildPieceCategories.Add(
        new BuildPieceTableConfig
            { Category = category });

    public void Add(string customCategory) => BuildPieceCategories.Add(new BuildPieceTableConfig
        { Category = BuildPieceCategory.Custom, custom = customCategory });
}

public struct BuildPieceTableConfig
{
    public BuildPieceCategory Category;
    public string? custom;
}

[PublicAPI]
public class BuildPiece
{
    private class PieceConfig
    {
        public ConfigEntry<string> craft = null!;
        public ConfigEntry<BuildPieceCategory> category = null!;
        public ConfigEntry<string> customCategory = null!;
    }

    private static readonly List<BuildPiece> registeredPieces = new();
    private static Dictionary<BuildPiece, PieceConfig> pieceConfigs = new();

    public static bool ConfigurationEnabled = true;

    public readonly GameObject Prefab;
    
    [Description("Specifies the resources needed to craft the piece.\nUse .Add to add resources with their internal ID and an amount.\nUse one .Add for each resource type the building piece should need.")]
    public readonly RequiredResourcesList RequiredItems = new();

    [Description("Sets the category for the building piece.")]
    public readonly BuildingPieceCategoryList Category = new();

    private LocalizeKey? _name;

    public LocalizeKey Name
    {
        get
        {
            if (_name is { } name)
            {
                return name;
            }

            Piece data = Prefab.GetComponent<Piece>();
            if (data.m_name.StartsWith("$"))
            {
                _name = new LocalizeKey(data.m_name);
            }
            else
            {
                string key = "$piece_" + Prefab.name.Replace(" ", "_");
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

            Piece data = Prefab.GetComponent<Piece>();
            if (data.m_description.StartsWith("$"))
            {
                _description = new LocalizeKey(data.m_description);
            }
            else
            {
                string key = "$piece_" + Prefab.name.Replace(" ", "_") + "_description";
                _description = new LocalizeKey(key).English(data.m_description);
                data.m_description = key;
            }

            return _description;
        }
    }

    public BuildPiece(string assetBundleFileName, string prefabName, string folderName = "assets") : this(
        PiecePrefabManager.RegisterAssetBundle(assetBundleFileName, folderName), prefabName)
    {
    }

    public BuildPiece(AssetBundle bundle, string prefabName, bool addToCustom = false, string customPieceTable = "")
    {
        if (addToCustom)
        {
            Prefab = PiecePrefabManager.RegisterPrefab(bundle, prefabName, false, true, customPieceTable);
        }
        else
        {
            Prefab = PiecePrefabManager.RegisterPrefab(bundle, prefabName, true);
        }

        registeredPieces.Add(this);
    }

    private class ConfigurationManagerAttributes
    {
        [UsedImplicitly] public int? Order;
        [UsedImplicitly] public bool? Browsable;
        [UsedImplicitly] public string? Category;
        [UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer;
    }

    private static object? configManager;

    internal static void Patch_FejdStartup()
    {
        Assembly? bepinexConfigManager = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "ConfigurationManager");

        Type? configManagerType = bepinexConfigManager?.GetType("ConfigurationManager.ConfigurationManager");
        configManager = configManagerType == null ? null : BepInEx.Bootstrap.Chainloader.ManagerObject.GetComponent(configManagerType);
        void ReloadConfigDisplay() => configManagerType?.GetMethod("BuildSettingList")!.Invoke(configManager, Array.Empty<object>());
        
        
        if (ConfigurationEnabled)
        {
            bool SaveOnConfigSet = plugin.Config.SaveOnConfigSet;
            plugin.Config.SaveOnConfigSet = false;
            foreach (BuildPiece piece in registeredPieces)
            {
                PieceConfig cfg = pieceConfigs[piece] = new PieceConfig();
                Piece piecePrefab = piece.Prefab.GetComponent<Piece>();
                string pieceName = piecePrefab.m_name;
                string englishName = new Regex("['[\"\\]]").Replace(english.Localize(pieceName), "").Trim();
                string localizedName = Localization.instance.Localize(pieceName).Trim();

                int order = 0;
                
                cfg.category = config(englishName, "Build Table Category", piece.Category.BuildPieceCategories.First().Category, new ConfigDescription($"Build Category where {englishName} is available.", null, new ConfigurationManagerAttributes { Order = --order, Category = localizedName }));
                ConfigurationManagerAttributes customTableAttributes = new() { Order = --order, Browsable = cfg.category.Value == BuildPieceCategory.Custom, Category = localizedName };
                cfg.customCategory = config(englishName, "Custom Build Category", piece.Category.BuildPieceCategories.First().custom ?? "", new ConfigDescription("", null, customTableAttributes));
                void BuildTableConfigChanged(object o, EventArgs e)
                {
                    if (registeredPieces.Count > 0)
                    {
                        if (cfg.category.Value is BuildPieceCategory.Custom)
                        {
                            // broken
                            // piece.Prefab.GetComponent<Piece>().m_category = (Piece.PieceCategory)ZNetScene.instance.GetPrefab(cfg.customCategory.Value)?.GetComponent<Piece>().m_category;
                        }
                        else
                        {
                            piecePrefab.m_category = (Piece.PieceCategory)cfg.category.Value;
                        }
                    }
                    customTableAttributes.Browsable = cfg.category.Value == BuildPieceCategory.Custom;
                    ReloadConfigDisplay();
                }
                
                cfg.category.SettingChanged += BuildTableConfigChanged;
                cfg.customCategory.SettingChanged += BuildTableConfigChanged;

                if (cfg.category.Value != BuildPieceCategory.Custom)
                {
                    piecePrefab.m_category = (Piece.PieceCategory)cfg.category.Value;
                }

                ConfigEntry<string> itemConfig(string name, string value, string desc)
                {
                    ConfigurationManagerAttributes attributes = new() { CustomDrawer = DrawConfigTable, Order = --order, Category = localizedName };
                    return config(englishName, name, value, new ConfigDescription(desc, null, attributes));
                }

                cfg.craft = itemConfig("Crafting Costs",
                    new SerializedRequirements(piece.RequiredItems.Requirements).ToString(),
                    $"Item costs to craft {englishName}");
                cfg.craft.SettingChanged += (_, _) =>
                {
                    if (ObjectDB.instance && ObjectDB.instance.GetItemPrefab("Wood") != null)
                    {
                        Piece.Requirement[] requirements =
                            SerializedRequirements.toPieceReqs(new SerializedRequirements(cfg.craft.Value));
                        piecePrefab.m_resources = requirements;
                        foreach (Piece instantiatedPiece in UnityEngine.Object.FindObjectsOfType<Piece>())
                        {
                            if (instantiatedPiece.m_name == pieceName)
                            {
                                instantiatedPiece.m_resources = requirements;
                            }
                        }
                    }
                };
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

        foreach (BuildPiece piece in registeredPieces)
        {
            pieceConfigs.TryGetValue(piece, out PieceConfig? cfg);
            piece.Prefab.GetComponent<Piece>().m_resources = SerializedRequirements.toPieceReqs(cfg == null
                ? new SerializedRequirements(piece.RequiredItems.Requirements)
                : new SerializedRequirements(cfg.craft.Value));
        }
    }

    private static void DrawConfigTable(ConfigEntryBase cfg)
    {
        bool locked = cfg.Description.Tags
            .Select(a =>
                a.GetType().Name == "ConfigurationManagerAttributes"
                    ? (bool?)a.GetType().GetField("ReadOnly")?.GetValue(a)
                    : null).FirstOrDefault(v => v != null) ?? false;

        List<Requirement> newReqs = new();
        bool wasUpdated = false;

        int RightColumnWidth = (int)(configManager?.GetType().GetProperty("RightColumnWidth", BindingFlags.Instance | BindingFlags.NonPublic)!.GetGetMethod(true).Invoke(configManager, Array.Empty<object>()) ?? 130);

        GUILayout.BeginVertical();
        foreach (Requirement req in new SerializedRequirements((string)cfg.BoxedValue).Reqs)
        {
            GUILayout.BeginHorizontal();

            int amount = req.amount;
            if (int.TryParse(
                    GUILayout.TextField(amount.ToString(), new GUIStyle(GUI.skin.textField) { fixedWidth = 40 }),
                    out int newAmount) && newAmount != amount && !locked)
            {
                amount = newAmount;
                wasUpdated = true;
            }

            string newItemName = GUILayout.TextField(req.itemName, new GUIStyle(GUI.skin.textField) { fixedWidth = RightColumnWidth - 40 - 67 - 21 - 21 - 12 });
            string itemName = locked ? req.itemName : newItemName;
            wasUpdated = wasUpdated || itemName != req.itemName;

            bool recover = req.recover;
            if (GUILayout.Toggle(req.recover, "Recover", new GUIStyle(GUI.skin.toggle) { fixedWidth = 67 }) !=
                req.recover)
            {
                recover = !recover;
                wasUpdated = true;
            }

            if (GUILayout.Button("x", new GUIStyle(GUI.skin.button) { fixedWidth = 21 }) && !locked)
            {
                wasUpdated = true;
            }
            else
            {
                newReqs.Add(new Requirement { amount = amount, itemName = itemName, recover = recover });
            }

            if (GUILayout.Button("+", new GUIStyle(GUI.skin.button) { fixedWidth = 21 }) && !locked)
            {
                wasUpdated = true;
                newReqs.Add(new Requirement { amount = 1, itemName = "", recover = false });
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
                return new Requirement
                {
                    itemName = parts[0],
                    amount = parts.Length > 1 && int.TryParse(parts[1], out int amount) ? amount : 1,
                    recover = parts.Length <= 2 || !bool.TryParse(parts[2], out bool recover) || recover,
                };
            }).ToList();
        }

        public override string ToString()
        {
            return string.Join(",", Reqs.Select(r => $"{r.itemName}:{r.amount}:{r.recover}"));
        }

        public static Piece.Requirement[] toPieceReqs(SerializedRequirements craft)
        {
            ItemDrop? ResItem(Requirement r)
            {
                ItemDrop? item = ObjectDB.instance.GetItemPrefab(r.itemName)?.GetComponent<ItemDrop>();
                if (item == null)
                {
                    Debug.LogWarning($"The required item '{r.itemName}' does not exist.");
                }

                return item;
            }

            Dictionary<string, Piece.Requirement?> resources = craft.Reqs.Where(r => r.itemName != "")
                .ToDictionary(r => r.itemName,
                    r => ResItem(r) is { } item
                        ? new Piece.Requirement { m_amount = r.amount, m_resItem = item, m_recover = r.recover }
                        : null);

            return resources.Values.Where(v => v != null).ToArray()!;
        }
    }

    private static Localization? _english;

    private static Localization english
    {
        get
        {
            if (_english != null) return _english;
            _english = new Localization();
            _english.SetupLanguage("English");

            return _english;
        }
    }

    private static BaseUnityPlugin? _plugin;

    private static BaseUnityPlugin plugin
    {
        get
        {
            if (_plugin is not null) return _plugin;
            IEnumerable<TypeInfo> types;
            try
            {
                types = Assembly.GetExecutingAssembly().DefinedTypes.ToList();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).Select(t => t.GetTypeInfo());
            }

            _plugin = (BaseUnityPlugin)BepInEx.Bootstrap.Chainloader.ManagerObject.GetComponent(types.First(t =>
                t.IsClass && typeof(BaseUnityPlugin).IsAssignableFrom(t)));

            return _plugin;
        }
    }

    private static bool hasConfigSync = true;
    private static object? _configSync;

    private static object? configSync
    {
        get
        {
            if (_configSync != null || !hasConfigSync) return _configSync;
            if (Assembly.GetExecutingAssembly().GetType("ServerSync.ConfigSync") is { } configSyncType)
            {
                _configSync = Activator.CreateInstance(configSyncType, plugin.Info.Metadata.GUID + " PieceManager");
                configSyncType.GetField("CurrentVersion")
                    .SetValue(_configSync, plugin.Info.Metadata.Version.ToString());
                configSyncType.GetProperty("IsLocked")!.SetValue(_configSync, true);
            }
            else
            {
                hasConfigSync = false;
            }

            return _configSync;
        }
    }

    private static ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description)
    {
        ConfigEntry<T> configEntry = plugin.Config.Bind(group, name, value, description);

        configSync?.GetType().GetMethod("AddConfigEntry")!.MakeGenericMethod(typeof(T))
            .Invoke(configSync, new object[] { configEntry });

        return configEntry;
    }

    private static ConfigEntry<T> config<T>(string group, string name, T value, string description) =>
        config(group, name, value, new ConfigDescription(description));
}

[PublicAPI]
public class LocalizeKey
{
    private static readonly List<LocalizeKey> keys = new();

    public readonly string Key;
    public readonly Dictionary<string, string> Localizations = new();

    public LocalizeKey(string key) => Key = key.Replace("$", "");
    
    public void Alias(string alias)
    {
        Localizations.Clear();
        if (!alias.Contains("$"))
        {
            alias = $"${alias}";
        }
        Localizations["alias"] = alias;
        Localization.instance.AddWord(Key, Localization.instance.Localize(alias));
    }

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
        Localizations[lang] = value;
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

    [HarmonyPriority(Priority.LowerThanNormal)]
    internal static void AddLocalizedKeys(Localization __instance, string language)
    {
        foreach (LocalizeKey key in keys)
        {
            if (key.Localizations.TryGetValue(language, out string Translation) || key.Localizations.TryGetValue("English", out Translation))
            {
                __instance.AddWord(key.Key, Translation);
            }
            else if (key.Localizations.TryGetValue("alias", out string alias))
            {
                Localization.instance.AddWord(key.Key, Localization.instance.Localize(alias));
            }
        }
    }
}

public static class PiecePrefabManager
{
    static PiecePrefabManager()
    {
        Harmony harmony = new("org.bepinex.helpers.PieceManager");
        harmony.Patch(AccessTools.DeclaredMethod(typeof(FejdStartup), nameof(FejdStartup.Awake)),
            new HarmonyMethod(AccessTools.DeclaredMethod(typeof(BuildPiece),
                nameof(BuildPiece.Patch_FejdStartup))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(ZNetScene), nameof(ZNetScene.Awake)),
            new HarmonyMethod(AccessTools.DeclaredMethod(typeof(PiecePrefabManager),
                nameof(Patch_ZNetSceneAwake))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(ObjectDB), nameof(ObjectDB.Awake)),
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(PiecePrefabManager),
                nameof(Patch_ObjectDBInit))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB)),
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(PiecePrefabManager),
                nameof(Patch_ObjectDBInit))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(ObjectDB), nameof(ObjectDB.Awake)),
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(BuildPiece),
                nameof(BuildPiece.Patch_ObjectDBInit))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Localization), nameof(Localization.LoadCSV)),
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(LocalizeKey),
                nameof(LocalizeKey.AddLocalizedKeys))));
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
            assets = bundleCache[id] =
                Resources.FindObjectsOfTypeAll<AssetBundle>().FirstOrDefault(a => a.name == assetBundleFileName) ??
                AssetBundle.LoadFromStream(Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(Assembly.GetExecutingAssembly().GetName().Name + $".{folderName}." +
                                               assetBundleFileName));
        }

        return assets;
    }

    private static readonly List<GameObject> piecePrefabs = new();
    private static readonly Dictionary<GameObject, string> customPiecePrefabs = new();
    private static readonly List<GameObject> ZnetOnlyPrefabs = new();

    public static GameObject RegisterPrefab(string assetBundleFileName, string prefabName,
        string folderName = "assets") =>
        RegisterPrefab(RegisterAssetBundle(assetBundleFileName, folderName), prefabName);

    public static GameObject RegisterPrefab(AssetBundle assets, string prefabName, bool addToPieceTable = false,
        bool addToCustomPieceTable = false, string customPieceTable = "")
    {
        GameObject prefab = assets.LoadAsset<GameObject>(prefabName);

        if (addToPieceTable)
        {
            piecePrefabs.Add(prefab);
        }
        else if (addToCustomPieceTable)
        {
            customPiecePrefabs.Add(prefab, customPieceTable);
        }
        else
        {
            ZnetOnlyPrefabs.Add(prefab);
        }

        return prefab;
    }

    /* Sprites Only! */
    public static Sprite RegisterSprite(string assetBundleFileName, string prefabName,
        string folderName = "assets") =>
        RegisterSprite(RegisterAssetBundle(assetBundleFileName, folderName), prefabName);

    public static Sprite RegisterSprite(AssetBundle assets, string prefabName)
    {
        Sprite prefab = assets.LoadAsset<Sprite>(prefabName);
        return prefab;
    }

    [HarmonyPriority(Priority.VeryHigh)]
    private static void Patch_ZNetSceneAwake(ZNetScene __instance)
    {
        foreach (GameObject prefab in piecePrefabs.Concat(ZnetOnlyPrefabs).Concat(customPiecePrefabs.Keys))
        {
            __instance.m_prefabs.Add(prefab);
        }
    }

    [HarmonyPriority(Priority.VeryHigh)]
    private static void Patch_ObjectDBInit(ObjectDB __instance)
    {
        if (__instance.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces is not
            { } hammerPieces)
        {
            return;
        }

        foreach (KeyValuePair<GameObject, string> customPiecePrefab in customPiecePrefabs)
        {
            if (__instance.GetItemPrefab(customPiecePrefab.Value)?.GetComponent<ItemDrop>().m_itemData.m_shared
                    .m_buildPieces is not
                { } customPieces)
            {
                continue;
            }

            if (customPieces.m_pieces.Contains(customPiecePrefab.Key))
            {
                continue;
            }

            customPieces.m_pieces.Add(customPiecePrefab.Key);
        }

        foreach (GameObject prefab in piecePrefabs)
        {
            if (hammerPieces.m_pieces.Contains(prefab))
            {
                return;
            }

            hammerPieces.m_pieces.Add(prefab);
        }
    }
}