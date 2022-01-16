using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UnityModManagerNet;
using System.Reflection;
using System.IO;
using Crest;

// The SaveFileHelper is good boilerplate code to paste into all your mods that need to store data in the save file:
public static class SaveFileHelper
{
    // How to use: 
    // Main.myModsSaveContainer = SaveFileHelper.Load<MyModsSaveContainer>("MyModName");
    public static T Load<T>(this string modName) where T : new()
    {
        string xmlStr;
        if (GameState.modData != null && GameState.modData.TryGetValue(modName, out xmlStr)) {
            Debug.Log("Proceeding to parse save data for " + modName);
            System.Xml.Serialization.XmlSerializer xmlSerializer = new System.Xml.Serialization.XmlSerializer(typeof(T));
            using (System.IO.StringReader textReader = new System.IO.StringReader(xmlStr)) {
                return (T)xmlSerializer.Deserialize(textReader);
            }
        }
        Debug.Log("Cannot load data from save file. Using defaults for " + modName);
        return new T();
    }

    // How to use:
    // SaveFileHelper.Save(Main.myModsSaveContainer, "MyModName");
    public static void Save<T>(this T toSerialize, string modName)
    {
        System.Xml.Serialization.XmlSerializer xmlSerializer = new System.Xml.Serialization.XmlSerializer(typeof(T));
        using (System.IO.StringWriter textWriter = new System.IO.StringWriter()) {
            xmlSerializer.Serialize(textWriter, toSerialize);
            GameState.modData[modName] = textWriter.ToString();
            Debug.Log("Packed save data for " + modName);
        }
    }
}

namespace SailwindStormController
{
    static class Main
    {
        public static bool enabled;
        public static UnityModManager.ModEntry mod;

        //This is standard code, you can just copy it directly into your mod
        static bool Load(UnityModManager.ModEntry modEntry)
        {
            var harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            mod = modEntry;
            modEntry.OnToggle = OnToggle;
            return true;
        }

        //This is also standard code, you can just copy it
        static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            enabled = value;
            return true;
        }

        [HarmonyPatch(typeof(Sun), "Start")]
        static class SetupConfig
        {
            private static void Postfix(Sun __instance)
            {
                if (Main.enabled)
                {
                    StormControllerMaster.configPath = Directory.GetCurrentDirectory() + @"\Mods\SailwindStormController\config.txt";
                    if(File.Exists(StormControllerMaster.configPath))
                    {
                        Debug.Log("FoundConfig");
                        StormControllerMaster.ReadModConfig();
                        StormControllerMaster.LoadConfigValues();
                    }
                    else
                    {
                        Debug.Log("Missing config file!");
                        //File.Create(StormControllerMaster.configPath);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(OceanUpdaterCrest), "DCTInertiaUpdate")]
        static class WavesMod1
        {
            private static void Postfix(OceanUpdaterCrest __instance,
                ref int ___wavesUp,
                ref int ___wavesDown,
                ref float ___currentMult,
                ref ShapeGerstnerBatched[] ___inertiaWaves,
                ref WavesInertia ___wavesInertia,
                ref float ___windSpeedMult,
                ref float ___smallWavesMult)
            {
                if (Main.enabled)
                {
                    //Debug.Log("WindSpeedMult: " + ___windSpeedMult);
                    //Debug.Log("InertiaWindScale: " + __instance.inertiaWindScale);
                    //Debug.Log("SmallWavesMult: " + ___smallWavesMult);
                    ___windSpeedMult = StormControllerMaster.windSpeedWaveFactor;
                    __instance.inertiaWindScale = StormControllerMaster.inertiaWindScale;
                    ___smallWavesMult = StormControllerMaster.smallWavesValueMultiplier;

                    float t = ___currentMult;
                    float num = Mathf.Clamp(Mathf.InverseLerp(150f, 600f, GameState.distanceToLand + StormControllerMaster.waveDistanceToLandIncrease), 0.15f, 0.65f);
                    float num2 = Mathf.InverseLerp(1300f, 2200f, GameState.distanceToLand + StormControllerMaster.waveDistanceToLandIncrease) * 0.35f;
                    float num3 = 1f;
                    if (GameState.eyesFullyClosed) {num3 = 0.1f;}
                    float num4 = ___wavesInertia.currentMagnitude * __instance.inertiaWindScale * (num + num2) * num3;
                    float weight = Mathf.Lerp(0f, num4, t);
                    float weight2 = Mathf.Lerp(num4, 0f, t);
                    ___inertiaWaves[___wavesUp]._weight = weight * StormControllerMaster.waveWeight;
                    ___inertiaWaves[___wavesDown]._weight = weight2 * StormControllerMaster.waveWeight;
                    ___inertiaWaves[___wavesUp]._spectrum.ApplyPhillipsSpectrum(___wavesInertia.currentInertia * ___windSpeedMult, ___smallWavesMult);
                }
            }
        }

        [HarmonyPatch(typeof(WanderingStorm), "Update")]
        static class StormMod1
        {
            private static void Postfix(WanderingStorm __instance,
                ref float ___stormRadius,
                ref float ___particlesDistance)
            {
                if (Main.enabled)
                {
                    if(StormControllerMaster.giveStorm)
                    {
                        __instance.active = true;
                        __instance.transform.position = Camera.main.transform.position;
                        if (StormControllerMaster.stormRadius > 0)
                        {
                            ___stormRadius = StormControllerMaster.stormRadius;
                            ___particlesDistance = ___stormRadius + 2500f;
                        }
                        //reset values
                        StormControllerMaster.giveStorm = false;
                        StormControllerMaster.stormRadius = 0;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(StartMenu), "GameToSettings")]
        static class ConsolePatchInitAltStart
        {
            [HarmonyPriority(350)]
            private static void Postfix()
            {
                if (Main.enabled)
                {
                    //when the game is paused, grab the console input if we haven't already
                    if(StormControllerMaster.modConsoleInput == null) StormControllerMaster.GetConsoleReference();
                }
            }
        }

        public static class StormControllerMaster
        {
            public static InputField modConsoleInput;
            public static string configPath;

            public static string[] lines;

            public static float inertiaWindScale = 0.3f;            //inertiawindscale - wind effect on wave inertia; VERY sensitive
            public static float windSpeedWaveFactor = 0.2f;         //windspeedmult - wind affect on wave strength
            public static float waveWeight = 1f;                    //wave weight 1&2 - must be the same
            public static float waveDistanceToLandIncrease = 0f;    //increases wave strength as if you are this distance farther from land(bigger waves near shore)
            public static float smallWavesValueMultiplier = 0.3f;   //small waves mult - waves in between the swells

            public static bool giveStorm;                           //use flag here so only 1 is pulled since in update
            public static float stormRadius;                        //not sure if this works

            public static void GetConsoleReference()
            {
                //grab the console input by name
                modConsoleInput = GameObject.Find("ConsoleInputField")?.GetComponent<InputField>();
                modConsoleInput?.onEndEdit.AddListener(delegate { CheckConsoleCommands(); });
            }

            public static void CheckConsoleCommands()
            {
                if (modConsoleInput.text == "givemestorm")
                {
                    giveStorm = true;
                    UISoundPlayer.instance.PlayGoldSound();
                    modConsoleInput.text = "Storm Granted";
                }

                if (modConsoleInput.text.Contains("givemestorm_"))
                {
                    string valueString = modConsoleInput.text.Split('_')[1];
                    int value;
                    bool success = int.TryParse(valueString, out value);
                    if (success && value != 0 && value > 0)
                    {
                        giveStorm = true;
                        stormRadius = value;
                        UISoundPlayer.instance.PlayGoldSound();
                        modConsoleInput.text = "Storm Granted";
                    }
                    else
                    {
                        stormRadius = 0;
                        modConsoleInput.text = "Invalid StormRadius Value";
                    }
                }
            }

            public static void ReadModConfig()
            {
                lines = null;
                StreamReader reader = new StreamReader(configPath, true);
                lines = reader.ReadToEnd().Split('\n');
                reader.Close();
            }

            public static void LoadConfigValues()
            {
                float value;

                if (float.TryParse(GetValue("windSpeedWaveFactor"), out value))
                {
                    windSpeedWaveFactor = value;
                }
                else { Debug.Log("BAD config value: windSpeedWaveFactor"); }

                if (float.TryParse(GetValue("waveInertiaWindScale"), out value))
                {
                    inertiaWindScale = value;
                }
                else { Debug.Log("BAD config value: waveInertiaWindScale"); }

                if (float.TryParse(GetValue("waveWeight"), out value))
                {
                    waveWeight = value;
                }
                else { Debug.Log("BAD config value: waveWeight"); }

                if (float.TryParse(GetValue("waveDistanceToLandIncrease"), out value))
                {
                    waveDistanceToLandIncrease = value;
                }
                else { Debug.Log("BAD config value: waveDistanceToLandIncrease"); }

                if (float.TryParse(GetValue("smallWavesValueMultiplier"), out value))
                {
                    smallWavesValueMultiplier = value;
                }
                else { Debug.Log("BAD config value: smallWavesValueMultiplier"); }
            }

            public static string GetValue(string key)
            {
                foreach (string line in lines)
                {
                    if (line.Split(':')[0].Contains(key))
                    {
                        string value = line.Split(':')[1];
                        return value;
                    }
                }
                return null;
            }
        }


    }
}
