using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace ConfigurableWipe
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public class PluginConfig
        {
            public ConfigEntry<bool> keepScrap;
            public ConfigEntry<float> scrapKeepChance;
            public ConfigEntry<bool> keepGear;
            public ConfigEntry<float> gearKeepChance;

            public PluginConfig(ConfigFile cfg)
            {
                const string section = "General";

                keepScrap = cfg.Bind(
                    section,
                    "KeepScrap",
                    false,
                    "Keep scrap when all players die"
                );

                scrapKeepChance = cfg.Bind(
                    section,
                    "ScrapKeepChance",
                    1f,
                    new ConfigDescription("Chance to keep scrap in the ship",new AcceptableValueRange<float>(0f, 1f))
                );

                keepGear = cfg.Bind(
                    section,
                    "KeepGear",
                    true,
                    "Keep purchased gear when all players die"
                );

                gearKeepChance = cfg.Bind(
                    section,
                    "GearKeepChance",
                    1f,
                    new ConfigDescription("Chance to keep purchased gear",new AcceptableValueRange<float>(0f, 1f))
                );
            }
        }

        public static PluginConfig config;

        public static ManualLogSource Log;

        private void Awake()
        {
            config = new PluginConfig(Config);
            Log = Logger;

            Log.LogInfo($"{PluginInfo.PLUGIN_GUID} is loaded!");
            new Harmony(PluginInfo.PLUGIN_GUID).PatchAll(Assembly.GetExecutingAssembly());
        }
    }
}

namespace ConfigurableWipe.Patches
{
    [HarmonyPatch(typeof(RoundManager), "DespawnPropsAtEndOfRound")]
    internal class RoundManagerPatcher
    {
        [HarmonyPrefix]
        public static bool DespawnPropsAtEndOfRound(RoundManager __instance, bool despawnAllItems)
        {
            if (!__instance.IsServer)
            {
                return false;
            }

            Plugin.Log.LogInfo("DespawnPropsAtEndOfRound");
            Plugin.Log.LogInfo($"keepScrap: {Plugin.config.keepScrap.Value}");
            Plugin.Log.LogInfo($"scrapKeepChance: {Plugin.config.scrapKeepChance.Value}");
            Plugin.Log.LogInfo($"keepGear: {Plugin.config.keepGear.Value}");
            Plugin.Log.LogInfo($"gearKeepChance: {Plugin.config.gearKeepChance.Value}");

            if (StartOfRound.Instance.allPlayersDead)
            {
                Plugin.Log.LogInfo("Team wipe");
            }

            foreach (var item in Object.FindObjectsOfType<GrabbableObject>())
            {
                var remove = despawnAllItems
                             || (!item.isHeld && !item.isInShipRoom)
                             || item.deactivated;

                if (!remove && StartOfRound.Instance.allPlayersDead)
                {
                    if (item.itemProperties.isScrap)
                    {
                        if (!Plugin.config.keepScrap.Value)
                        {
                            remove = Random.value > Plugin.config.scrapKeepChance.Value;
                        }
                    }
                    else if (!Plugin.config.keepGear.Value)
                    {
                        remove = Random.value > Plugin.config.gearKeepChance.Value;
                    }

                    if (remove)
                    {
                        Plugin.Log.LogInfo($"Removing item {item.name}");
                    }
                }

                if (remove)
                {
                    if (item.isHeld && item.playerHeldBy != null)
                    {
                        item.playerHeldBy.DropAllHeldItemsAndSync();
                    }

                    var component = item.gameObject.GetComponent<NetworkObject>();
                    if (component && component.IsSpawned)
                    {
                        component.Despawn(true);
                    }
                    else
                    {
                        Debug.Log("Error/warning: prop '" + item.gameObject.name + "' was not spawned or did not have a NetworkObject component! Skipped despawning and destroyed it instead.");
                        Object.Destroy(item.gameObject);
                    }
                }
                else
                {
                    item.scrapPersistedThroughRounds = true;
                }

                __instance.spawnedSyncedObjects.Remove(item.gameObject);
            }

            foreach (var go in GameObject.FindGameObjectsWithTag("TemporaryEffect"))
            {
                Object.Destroy(go);
            }

            return false;
        }
    }
}
