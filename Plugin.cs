using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using CreatureManager;
using HarmonyLib;
using ItemManager;
using JetBrains.Annotations;
using LocalizationManager;
using LocationManager;
using PieceManager;
using ServerSync;
using SkillManager;
using StatusEffectManager;
using UnityEngine;
using PrefabManager = ItemManager.PrefabManager;
using Range = LocationManager.Range;

namespace AllManagersModTemplate
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class AllManagersModTemplatePlugin : BaseUnityPlugin
    {
        internal const string ModName = "AllManagersModTemplate";
        internal const string ModVersion = "1.0.0";
        internal const string Author = "{azumatt}";
        private const string ModGUID = Author + "." + ModName;
        private static string ConfigFileName = ModGUID + ".cfg";
        private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
        internal static string ConnectionError = "";
        private readonly Harmony _harmony = new(ModGUID);

        public static readonly ManualLogSource AllManagersModTemplateLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);

        private static readonly ConfigSync ConfigSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };
        
        // Location Manager variables
        public Texture2D tex = null!;
        
        // Use only if you need them
        //private Sprite mySprite = null!;
        //private SpriteRenderer sr = null!;

        public enum Toggle
        {
            On = 1,
            Off = 0 
        }

        public void Awake()
        {
            // Uncomment the line below to use the LocalizationManager for localizing your mod.
            //Localizer.Load(); // Use this to initialize the LocalizationManager (for more information on LocalizationManager, see the LocalizationManager documentation https://github.com/blaxxun-boop/LocalizationManager#example-project).
            bool saveOnSet = Config.SaveOnConfigSet;
            Config.SaveOnConfigSet = false; // This and the variable above are used to prevent the config from saving on startup for each config entry. This is speeds up the startup process.
            
            _serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
            _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);


            #region PieceManager Example Code

            // Globally turn off configuration options for your pieces, omit if you don't want to do this.
            BuildPiece.ConfigurationEnabled = false;
            
            // Format: new("AssetBundleName", "PrefabName", "FolderName");
            BuildPiece examplePiece1 = new("funwardBundle", "funwardPrefabName", "FunWardFolder");

            examplePiece1.Name.English("Fun Ward"); // Localize the name and description for the building piece for a language. Use this if you're not using the LocalizationManager.
            examplePiece1.Description.English("Ward For testing the Piece Manager");
            examplePiece1.RequiredItems.Add("FineWood", 20, false); // Set the required items to build. Format: ("PrefabName", Amount, Recoverable)
            examplePiece1.RequiredItems.Add("SurtlingCore", 20, false);
            examplePiece1.Category.Set(PieceManager.BuildPieceCategory.Misc);
            examplePiece1.Crafting.Set(PieceManager.CraftingTable.ArtisanTable); // Set a crafting station requirement for the piece.
            examplePiece1.Extension.Set(PieceManager.CraftingTable.Forge, 2); // Makes this piece a station extension, can change the max station distance by changing the second value. Use strings for custom tables.
            //examplePiece1.Crafting.Set("CUSTOMTABLE"); // If you have a custom table you're adding to the game. Just set it like this.
            //examplePiece1.SpecialProperties.NoConfig = true;  // Do not generate a config for this piece, omit this line of code if you want to generate a config.
            examplePiece1.SpecialProperties = new SpecialProperties() { AdminOnly = true, NoConfig = true}; // You can declare multiple properties in one line           


            BuildPiece examplePiece2 = new("bamboo", "Bamboo_Wall"); // Note: If you wish to use the default "assets" folder for your assets, you can omit it!
            examplePiece2.Name.English("Bamboo Wall");
            examplePiece2.Description.English("A wall made of bamboo!");
            examplePiece2.RequiredItems.Add("BambooLog", 20, false);
            examplePiece2.Category.Set(PieceManager.BuildPieceCategory.BuildingWorkbench);
            examplePiece2.Crafting.Set("CUSTOMTABLE"); // If you have a custom table you're adding to the game. Just set it like this.
            examplePiece2.SpecialProperties.AdminOnly = true;  // You can declare these one at a time as well!.


            // If you want to add your item to the cultivator or another hammer with vanilla categories
            // Format: (AssetBundle, "PrefabName", addToCustom, "Item that has a piecetable")
            BuildPiece examplePiece3 = new("bamboo", "Bamboo_Sapling");
            examplePiece3.Name.English("Bamboo Sapling");
            examplePiece3.Description.English("A young bamboo tree, called a sapling");
            examplePiece3.RequiredItems.Add("BambooSeed", 20, false);
            examplePiece3.Tool.Add("Cultivator"); // Format: ("Item that has a piecetable")
            examplePiece3.SpecialProperties.NoConfig = true;
            
            // If you don't want to make an icon inside unity, but want the PieceManager to snag one for you, simply add .Snapshot() to your piece.
            examplePiece3.Snapshot(); // Optionally, you can use the lightIntensity parameter to set the light intensity of the snapshot. Default is 1.3 or the cameraRotation parameter to set the rotation of the camera. Default is null.

            // If you want a more custom piece, below is an example. Including custom category and custom crafting station. Also adding to a custom hammer.
            BuildPiece examplePiece4 = new("bamboo", "Bamboo_Beam_Light");
            examplePiece4.Name.English("Bamboo Beam Light");
            examplePiece4.Description.English("A light made of bamboo!");
            examplePiece4.RequiredItems.Add("BambooLog", 20, false);
            examplePiece4.Category.Set("Custom Category");
            examplePiece4.Crafting.Set("CUSTOMTABLE");
            examplePiece4.Tool.Add("Custom Hammer");
            examplePiece4.SpecialProperties.NoConfig = true;
            examplePiece4.Snapshot(); // Optionally, you can use the lightIntensity parameter to set the light intensity of the snapshot. Default is 1.3 or the cameraRotation parameter to set the rotation of the camera. Default is null.
            
            // Need to add something to ZNetScene but not the hammer, cultivator or other? 
            PiecePrefabManager.RegisterPrefab("bamboo", "Bamboo_Beam_Light");
            
            // Does your model need to swap materials with a vanilla material? Format: (GameObject, isJotunnMock)
            MaterialReplacer.RegisterGameObjectForMatSwap(examplePiece3.Prefab, false);
            
            
            // What if you want to use a custom shader from the game (like Custom/Piece that allows snow!!!) but your unity shader isn't set to Custom/Piece? Format: (GameObject, MaterialReplacer.ShaderType.)
            //MaterialReplacer.RegisterGameObjectForShaderSwap(examplePiece3.Prefab, MaterialReplacer.ShaderType.PieceShader);

            // Detailed instructions on how to use the MaterialReplacer can be found on the current PieceManager Wiki. https://github.com/AzumattDev/PieceManager/wiki
            #endregion

            #region SkillManager Example Code

            Skill tenacity = new("Tenacity", "tenacity-icon.png"); // Skill name along with the skill icon. By default the icon is found in the icons folder. Put it there if you wish to load one.

            tenacity.Description.English("Reduces damage taken by 0.2% per level.");
            tenacity.Name.German("Hartnäckigkeit"); // Use this to localize values for the name
            tenacity.Description.German("Reduziert erlittenen Schaden um 0,2% pro Stufe."); // You can do the same for the description
            tenacity.Configurable = true;

            #endregion

            #region LocationManager Example Code

            var example = new LocationManager.Location("guildfabs", "GuildAltarSceneFab")
            {
                MapIcon = "portalicon.png",
                ShowMapIcon = ShowIcon.Explored,
                MapIconSprite = Sprite.Create(tex, new Rect(0.0f, 0.0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100.0f),
                CanSpawn = true,
                SpawnArea = Heightmap.BiomeArea.Everything,
                Prioritize = true,
                PreferCenter = true,
                Rotation = Rotation.Slope,
                HeightDelta = new Range(0, 2),
                SnapToWater = false,
                ForestThreshold = new Range(0, 2.19f),
                Biome = Heightmap.Biome.Meadows,
                SpawnDistance = new Range(500, 1500),
                SpawnAltitude = new Range(10, 100),
                MinimumDistanceFromGroup = 100,
                GroupName = "groupName",
                Count = 15,
                Unique = true
            };
            
            LocationManager.Location location = new("krumpaclocations", "WaterPit1")
            {
                MapIcon = "K_Church_Ruin01.png",
                ShowMapIcon = ShowIcon.Always,
                Biome = Heightmap.Biome.Meadows,
                SpawnDistance = new Range(100, 1500),
                SpawnAltitude = new Range(5, 150),
                MinimumDistanceFromGroup = 100,
                Count = 15
            };
		
            // If your location has creature spawners, you can configure the creature they spawn like this.
            location.CreatureSpawner.Add("Spawner_1", "Neck");
            location.CreatureSpawner.Add("Spawner_2", "Troll");
            location.CreatureSpawner.Add("Spawner_3", "Greydwarf");
            location.CreatureSpawner.Add("Spawner_4", "Neck");
            location.CreatureSpawner.Add("Spawner_5", "Troll");
            location.CreatureSpawner.Add("Spawner_6", "Greydwarf");
            
            #region Location Notes

            // MapIcon                      Sets the map icon for the location.
            // ShowMapIcon                  When to show the map icon of the location. Requires an icon to be set. Use "Never" to not show a map icon for the location. Use "Always" to always show a map icon for the location. Use "Explored" to start showing a map icon for the location as soon as a player has explored the area.
            // MapIconSprite                Sets the map icon for the location.
            // CanSpawn                     Can the location spawn at all.
            // SpawnArea                    If the location should spawn more towards the edge of the biome or towards the center. Use "Edge" to make it spawn towards the edge. Use "Median" to make it spawn towards the center. Use "Everything" if it doesn't matter.</para>
            // Prioritize                   If set to true, this location will be prioritized over other locations, if they would spawn in the same area.
            // PreferCenter                 If set to true, Valheim will try to spawn your location as close to the center of the map as possible.
            // Rotation                     How to rotate the location. Use "Fixed" to use the rotation of the prefab. Use "Random" to randomize the rotation. Use "Slope" to rotate the location along a possible slope.
            // HeightDelta                  The minimum and maximum height difference of the terrain below the location.
            // SnapToWater                  If the location should spawn near water.
            // ForestThreshold              If the location should spawn in a forest. Everything above 1.15 is considered a forest by Valheim. 2.19 is considered a thick forest by Valheim.
            // Biome
            // SpawnDistance                Minimum and maximum range from the center of the map for the location.
            // SpawnAltitude                Minimum and maximum altitude for the location.
            // MinimumDistanceFromGroup     Locations in the same group will keep at least this much distance between each other.
            // GroupName                    The name of the group of the location, used by the minimum distance from group setting.
            // Count                        Maximum number of locations to spawn in. Does not mean that this many locations will spawn. But Valheim will try its best to spawn this many, if there is space.
            // Unique                       If set to true, all other locations will be deleted, once the first one has been discovered by a player.

            #endregion

            #endregion

            #region StatusEffectManager Example Code

             CustomSE mycooleffect = new("Toxicity");
            mycooleffect.Name.English("Toxicity");
            mycooleffect.Type = EffectType.Consume;
            mycooleffect.IconSprite = null;
            mycooleffect.Name.German("Toxizität"); 
            mycooleffect.Effect.m_startMessageType = MessageHud.MessageType.TopLeft;
            mycooleffect.Effect.m_startMessage = "My Cool Status Effect Started"; 
            mycooleffect.Effect.m_stopMessageType = MessageHud.MessageType.TopLeft;
            mycooleffect.Effect.m_stopMessage = "Not cool anymore, ending effect."; 
            mycooleffect.Effect.m_tooltip = "<color=orange>Toxic damage over time</color>"; 
            mycooleffect.AddSEToPrefab(mycooleffect, "SwordIron");
            
            CustomSE drunkeffect = new("se_drunk", "se_drunk_effect");
			drunkeffect.Name.English("Drunk"); // You can use this to fix the display name in code
			drunkeffect.Icon = "DrunkIcon.png"; // Use this to add an icon (64x64) for the status effect. Put your icon in an "icons" folder
			drunkeffect.Name.German("Betrunken"); // Or add translations for other languages
			drunkeffect.Effect.m_startMessageType = MessageHud.MessageType.Center; // Specify where the start effect message shows
			drunkeffect.Effect.m_startMessage = "I'm drunk!"; // What the start message says
			drunkeffect.Effect.m_stopMessageType = MessageHud.MessageType.Center; // Specify where the stop effect message shows
			drunkeffect.Effect.m_stopMessage = "Sober...again."; // What the stop message says
			drunkeffect.Effect.m_tooltip = "<color=red>Your vision is blurry</color>"; // Tooltip that will describe the effect applied to the player
			drunkeffect.AddSEToPrefab(drunkeffect, "TankardAnniversary"); // Adds the status effect to the Anniversary Tankard. Applies when equipped.
			
			// Create a new status effect in code and apply it to a prefab.
			CustomSE codeSE = new("CodeStatusEffect");
			codeSE.Name.English("New Effect");
			codeSE.Type = EffectType.Consume; // Set the type of status effect this should be.
			codeSE.Icon = "ModDevPower.png";
			codeSE.Name.German("Betrunken"); // Or add translations for other languages
			codeSE.Effect.m_startMessageType = MessageHud.MessageType.Center; // Specify where the start effect message shows
			codeSE.Effect.m_startMessage = "Mod Dev power, granted."; // What the start message says
			codeSE.Effect.m_stopMessageType = MessageHud.MessageType.Center; // Specify where the stop effect message shows
			codeSE.Effect.m_stopMessage = "Mod Dev power, removed."; // What the stop message says
			codeSE.Effect.m_tooltip = "<color=green>You now have Mod Dev POWER!</color>"; // Tooltip that will describe the effect applied to the player
			codeSE.AddSEToPrefab(codeSE, "SwordCheat"); // Adds the status effect to the Cheat Sword. Applies when equipped.
		


            #endregion

            #region ItemManager Example Code

            Item ironFangAxe = new("ironfang", "IronFangAxe", "IronFang");
            ironFangAxe.Name.English("Iron Fang Axe"); // You can use this to fix the display name in code
            ironFangAxe.Description.English("A sharp blade made of iron.");
            ironFangAxe.Name.German("Eisenzahnaxt"); // Or add translations for other languages
            ironFangAxe.Description.German("Eine sehr scharfe Axt, bestehend aus Eisen und Wolfszähnen.");
            ironFangAxe.Crafting.Add("MyAmazingCraftingStation",
                3); // Custom crafting stations can be specified as a string
            ironFangAxe.RequiredItems.Add("Iron", 120);
            ironFangAxe.RequiredItems.Add("WolfFang", 20);
            ironFangAxe.RequiredItems.Add("Silver", 40);
            ironFangAxe.RequiredUpgradeItems
                .Add("Iron", 20); // Upgrade requirements are per item, even if you craft two at the same time
            ironFangAxe.RequiredUpgradeItems.Add("Silver",
                10); // 10 Silver: You need 10 silver for level 2, 20 silver for level 3, 30 silver for level 4
            ironFangAxe.CraftAmount = 2; // We really want to dual wield these
            ironFangAxe.Trade.Price = 100; // You can set a price for the item
            ironFangAxe.Trade.Stack = 10; // And how many you can buy at once
            ironFangAxe.Trade.RequiredGlobalKey = "defeated_bonemass"; // You can set a global key that is required to buy this item
            ironFangAxe.Trade.Trader = ItemManager.Trader.Haldor; // You can set a specific trader that sells this item


            // If you have something that shouldn't go into the ObjectDB, like vfx or sfx that only need to be added to ZNetScene
            ItemManager.PrefabManager.RegisterPrefab(PrefabManager.RegisterAssetBundle("ironfang"), "axeVisual", false); // If our axe has a special visual effect, like a glow, we can skip adding it to the ObjectDB this way
            ItemManager.PrefabManager.RegisterPrefab(PrefabManager.RegisterAssetBundle("ironfang"), "axeSound", false); // Same for special sound effects
            
            Item heroBlade = new("heroset", "HeroBlade");
            heroBlade.Crafting.Add(ItemManager.CraftingTable.Workbench, 2);
            heroBlade.RequiredItems.Add("Wood", 5);
            heroBlade.RequiredItems.Add("DeerHide", 2);
            heroBlade.RequiredUpgradeItems.Add("Wood", 2);
            heroBlade.RequiredUpgradeItems.Add("Flint", 2); // You can even add new items for the upgrade
			
            Item heroShield = new("heroset", "HeroShield");
            heroShield["My first recipe"].Crafting.Add(ItemManager.CraftingTable.Workbench, 1); // You can add multiple recipes for the same item, by giving the recipe a name
            heroShield["My first recipe"].RequiredItems.Add("Wood", 10);
            heroShield["My first recipe"].RequiredItems.Add("Flint", 5);
            heroShield["My first recipe"].RequiredUpgradeItems.Add("Wood", 5);
            heroShield["My alternate recipe"].Crafting.Add(ItemManager.CraftingTable.Workbench, 1); // And this is our second recipe then
            heroShield["My alternate recipe"].RequiredItems.Add("Bronze", 2);
            heroShield["My alternate recipe"].RequiredUpgradeItems.Add("Bronze", 1);
            heroShield.Snapshot(); // I don't have an icon for this item in my asset bundle, so I will let the ItemManager generate one automatically
            // The icon for the item will have the same rotation as the item in unity
			
            _ = new ItemManager.Conversion(heroBlade) // For some reason, we want to be able to put a hero shield into a smelter, to get a hero blade
            {
                Input = "HeroShield",
                Piece = ConversionPiece.Smelter
            };
            
            heroShield.DropsFrom.Add("Greydwarf", 0.3f, 1, 2); // A Greydwarf has a 30% chance, to drop 1-2 hero shields.

            #endregion

            #region CreatureManager Example Code

            Creature wereBearBlack = new("werebear", "WereBearBlack")
            {
                Biome = Heightmap.Biome.Meadows,
                GroupSize = new CreatureManager.Range(1, 2),
                CheckSpawnInterval = 600,
                RequiredWeather = Weather.Rain | Weather.Fog,
                Maximum = 2
            };
            wereBearBlack.Localize()
                .English("Black Werebear")
                .German("Schwarzer Werbär")
                .French("Ours-Garou Noir");
            wereBearBlack.Drops["Wood"].Amount = new CreatureManager.Range(1, 2);
            wereBearBlack.Drops["Wood"].DropChance = 100f;

            Creature wereBearRed = new("werebear", "WereBearRed")
            {
                Biome = Heightmap.Biome.AshLands,
                GroupSize = new CreatureManager.Range(1, 1),
                CheckSpawnInterval = 900,
                AttackImmediately = true,
                RequiredGlobalKey = GlobalKey.KilledYagluth,
            };
            wereBearRed.Localize()
                .English("Red Werebear")
                .German("Roter Werbär")
                .French("Ours-Garou Rouge");
            wereBearRed.Drops["Coal"].Amount = new CreatureManager.Range(1, 2);
            wereBearRed.Drops["Coal"].DropChance = 100f;
            wereBearRed.Drops["Flametal"].Amount = new CreatureManager.Range(1, 1);
            wereBearRed.Drops["Flametal"].DropChance = 10f;

            #endregion


            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
            SetupWatcher();
            
            if (saveOnSet)
            {
                Config.SaveOnConfigSet = saveOnSet;
                Config.Save();
            }
            
            // If you want to do something once localization completes, LocalizationManager has a hook for that.
            /*Localizer.OnLocalizationComplete += () =>
            {
                // Do something
                ItemManagerModTemplateLogger.LogDebug("OnLocalizationComplete called");
            };*/
        }

        private void OnDestroy()
        {
            Config.Save();
        }

        private void SetupWatcher()
        {
            FileSystemWatcher watcher = new(Paths.ConfigPath, ConfigFileName);
            watcher.Changed += ReadConfigValues;
            watcher.Created += ReadConfigValues;
            watcher.Renamed += ReadConfigValues;
            watcher.IncludeSubdirectories = true;
            watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            watcher.EnableRaisingEvents = true;
        }

        private void ReadConfigValues(object sender, FileSystemEventArgs e)
        {
            if (!File.Exists(ConfigFileFullPath)) return;
            try
            {
                AllManagersModTemplateLogger.LogDebug("ReadConfigValues called");
                Config.Reload();
            }
            catch
            {
                AllManagersModTemplateLogger.LogError($"There was an issue loading your {ConfigFileName}");
                AllManagersModTemplateLogger.LogError("Please check your config entries for spelling and format!");
            }
        }


        #region ConfigOptions

        private static ConfigEntry<Toggle> _serverConfigLocked = null!;

        private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
        {
            ConfigDescription extendedDescription = new(description.Description + (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"), description.AcceptableValues, description.Tags);
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
            //var configEntry = Config.Bind(group, name, value, description);

            SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true)
        {
            return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
        }

        private class ConfigurationManagerAttributes
        {
            [UsedImplicitly] public int? Order = null!;
            [UsedImplicitly] public bool? Browsable = null!;
            [UsedImplicitly] public string? Category = null!;
            [UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer = null!;
        }
        class AcceptableShortcuts : AcceptableValueBase
        {
            public AcceptableShortcuts() : base(typeof(KeyboardShortcut))
            {
            }

            public override object Clamp(object value) => value;
            public override bool IsValid(object value) => true;

            public override string ToDescriptionString() => "# Acceptable values: " + string.Join(", ", UnityInput.Current.SupportedKeyCodes);
        }

        #endregion
    }

    public static class KeyboardExtensions
    {
        public static bool IsKeyDown(this KeyboardShortcut shortcut)
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKeyDown(shortcut.MainKey) && shortcut.Modifiers.All(Input.GetKey);
        }

        public static bool IsKeyHeld(this KeyboardShortcut shortcut)
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKey(shortcut.MainKey) && shortcut.Modifiers.All(Input.GetKey);
        }
    }
}