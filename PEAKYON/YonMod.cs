using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using ExitGames.Client.Photon; // for SendOptions
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using Photon.Voice.Unity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace PEAKYON
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency("PEAKLobbyBrowser", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("synq.peak.atlas", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.dummy.anticheatcontinuum", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("evaisa.ThirdPersonToggle", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.github.LIghtJUNction.TerrainScanner", BepInDependency.DependencyFlags.SoftDependency)]

    //[BepInDependency("com.github.PEAKModding.PEAKLib.UI")]
    public class YonMod : BaseUnityPlugin
    {
        //super kick configs
        public static ConfigEntry<KeyCode> KickPatchToggleKey;
        public static ConfigEntry<bool> enableSuperKick;
        public static ConfigEntry<float> SuperKickForce;
        public static ConfigEntry<float> SuperKickRange;
        public static ConfigEntry<float> SuperKickRagdollTime;
        public static ConfigEntry<float> SuperKickDistance;
        public static ConfigEntry<float> SuperKickAngle;
        
        //talk as configs
        public static ConfigEntry<KeyCode> TalkAsKey;
        public static ConfigEntry<int> TalkAsPV;
        public static ConfigEntry<bool> HearYourselfToggle;

        //bugle configs
        public static ConfigEntry<KeyCode> BuglePatchToggleKey;
        public static ConfigEntry<bool> BuglePatchEnabled;
        public static ConfigEntry<float> BuglePitchMin;
        public static ConfigEntry<float> BuglePitchMax;
        public static ConfigEntry<float> BugleVolume;
        public static ConfigEntry<float> BuglePitchWobble;

        //BackFlip Success
        public static ConfigEntry<float> SuccessChance;

        internal static new ManualLogSource Logger;

        static int MyViewID;

        private void Awake()
        {
            Logger = base.Logger;
            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

            Binds();
            HearYourselfToggle.Value = false;
            HearYourselfToggle.SettingChanged += (_, _) => HearYourself();
            BuglePatchEnabled.Value = false;
            BuglePatchEnabled.SettingChanged += (_, _) => Notification("Bugle Patch is " + (BuglePatchEnabled.Value ? "ON" : "OFF"));
            enableSuperKick.SettingChanged += (_, _) => SuperKick();

            TalkAsPV.SettingChanged += (_, _) => TalkAs(TalkAsPV.Value);
            //new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
            var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            PatchAll(harmony);
            //matchListResult = CallResult<LobbyMatchList_t>.Create(OnLobbyList);
        }
        private void Update()
        {
            if (GUIManager.instance != null && GUIManager.instance.windowBlockingInput) return; //no keypress when typing in chat or using menus

            if (Input.GetKeyDown(KickPatchToggleKey.Value))
            {
                enableSuperKick.Value = !enableSuperKick.Value;
            }
            if (Input.GetKeyDown(BuglePatchToggleKey.Value))
            {
                BuglePatchEnabled.Value = !BuglePatchEnabled.Value;
            }
            //if (Input.GetKey(TalkAsKey.Value) && Character.localCharacter != null)
            //{
            //    TalkAs(TalkAsPV.Value);
            //}
            //if (Input.GetKeyUp(TalkAsKey.Value) && Character.localCharacter != null)
            //{
            //    Character.localCharacter.transform.GetChild(2).GetComponent<Recorder>().UserData = 0;
            //}
        }

        //[HarmonyPatch("PEAKLobbyBrowser.PEAKLobbyBrowser", "Start")]
        public static class PatchPEAKLobbyBrowserStart
        {
            public static bool Prefix() { 
                Logger.LogInfo("Anti-Cheat Bypassed");
                return false; // returning false skips the original
            }
        }

        //no Stamina patch for kicks
        [HarmonyPatch(typeof(CharacterGrabbing), nameof(CharacterGrabbing.KickCast))]
        static class Patch_KickCast_NoStamina
        {
            [HarmonyPrefix]
            private static bool Prefix(CharacterGrabbing __instance)
            {
                if (!YonMod.enableSuperKick.Value) return true;

                // only local player
                if (!__instance.GetComponent<Character>().photonView.IsMine) return true;

                __instance.kickForce = YonMod.SuperKickForce.Value;
                __instance.kickRange = YonMod.SuperKickRange.Value;
                __instance.kickRagdollTime = YonMod.SuperKickRagdollTime.Value;
                __instance.kickDistance = YonMod.SuperKickDistance.Value;
                __instance.kickAngle = YonMod.SuperKickAngle.Value;

                return true; // let KickCast still run
            }

            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var useStamina = AccessTools.Method(typeof(Character), "UseStamina");
                var codes = new List<CodeInstruction>(instructions);

                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].Calls(useStamina))
                    {
                        // Nop the call and the 3 pushes before it (ldfld character, ldc.r4, ldc.i4)
                        codes[i].opcode = OpCodes.Nop;
                        codes[i].operand = null;
                        codes[i - 1].opcode = OpCodes.Nop; // ldc.i4.1 (bool true)
                        codes[i - 1].operand = null;
                        codes[i - 2].opcode = OpCodes.Nop; // ldc.r4 1f
                        codes[i - 2].operand = null;
                        codes[i - 3].opcode = OpCodes.Nop; // ldfld character
                        codes[i - 3].operand = null;
                        // ldarg.0 before that is fine, it's used elsewhere too — don't nop it
                        break;
                    }
                }

                return codes;
            }
        }

        //[HarmonyPatch("Atlas.Plugin", "FixedUpdate")]
        static class PatchAtlasUpdate
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                return false; // skip the original method to prevent crashes
            }
        }
        private void Binds()
        {
            KickPatchToggleKey = Config.Bind("SuperKick", "FastKickToggleKey", KeyCode.K, "Key to toggle the fast kick patch on/off.");
            enableSuperKick = Config.Bind("SuperKick", "SuperKick Enable", true, "Toggle the super kick patch on/off.");
            SuperKickForce = Config.Bind("SuperKick", "SuperKick Force", 50f,
                new ConfigDescription("The force of the SuperKick. (default is 10)", new AcceptableValueRange<float>(-100000, 100000)));
            SuperKickRange = Config.Bind("SuperKick", "SuperKick Range(UP)", 3f,
                new ConfigDescription("The range of the SuperKick.(default is 3)", new AcceptableValueRange<float>(-100000, 100000)));
            SuperKickDistance = Config.Bind("SuperKick", "SuperKick Distance(Forward)", 1f,
                new ConfigDescription("The distance of the SuperKick. (default is 1)", new AcceptableValueRange<float>(-100000, 100000)));
            SuperKickRagdollTime = Config.Bind("SuperKick", "SuperKick Ragdoll Time", 1f,
                new ConfigDescription("How long the ragdoll effect lasts. (default is 1)", new AcceptableValueRange<float>(0f, 1000f)));
            SuperKickAngle = Config.Bind("SuperKick", "SuperKick Angle", 45f,
                new ConfigDescription("The angle of the SuperKick. (default is 45)", new AcceptableValueRange<float>(0f, 360f)));
            TalkAsKey = Config.Bind("TalkAs", "TalkAsKey", KeyCode.T, "Key to talk as another player (hold while talking).");
            TalkAsPV = Config.Bind("TalkAs", "TalkAs", 0, "Talk As Object.");
            HearYourselfToggle = Config.Bind("TalkAs", "HearYourself", false, "Whether to hear your own voice when using Talk As.");

            BuglePitchMin = Config.Bind("Bugle", "Pitch Min", 0.7f,
                new ConfigDescription("Minimum pitch of the bugle. (default: 0.7)", new AcceptableValueRange<float>(0f, 5f)));

            BuglePitchMax = Config.Bind("Bugle", "Pitch Max", 1.3f,
                new ConfigDescription("Maximum pitch of the bugle. (default: 1.3)", new AcceptableValueRange<float>(0f, 5f)));

            BugleVolume = Config.Bind("Bugle", "Volume", 0.35f,
                new ConfigDescription("Volume of the bugle. (default: 0.35)", new AcceptableValueRange<float>(0f, 1f)));

            BuglePitchWobble = Config.Bind("Bugle", "Pitch Wobble", 0f,
                new ConfigDescription("Pitch wobble of the bugle. (default: 0)", new AcceptableValueRange<float>(0f, 1f)));


            BuglePatchEnabled = Config.Bind("Bugle", "Bugle Patch Enabled", true, "Toggle the bugle patch on/off.");
            BuglePatchToggleKey = Config.Bind("Bugle", "Bugle Patch Toggle Key", KeyCode.Backslash, "Key to toggle the bugle patch on/off.");

            SuccessChance = Config.Bind("Emotes", "Backflip Success", 50f,
                new ConfigDescription("Probability of backflip succeeding (0 - 100)", new AcceptableValueRange<float>(0f, 100f)));
        }

        //kick cooldown patch
        [HarmonyPatch(typeof(CharacterGrabbing), "Update")]
        public static class CharacterGrabbing_Update_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(CharacterGrabbing __instance)
            {
                if (!YonMod.enableSuperKick.Value) return true; // if SuperKick is not enabled, run the original method
                var kickTime = Traverse.Create(__instance).Field<float>("_kickTime");
                if (kickTime.Value < 0.6f)
                {
                    kickTime.Value = 0.6f;
                }
                return true;
            }
        }

        public static void Notification(string message, string color = "FFFFFF", bool sound = false)
        {
            PlayerConnectionLog connectionLog = UnityEngine.Object.FindAnyObjectByType<PlayerConnectionLog>();
            if (connectionLog == null)
            {
                return;
            }
            string formattedMessage = string.Concat(new string[] { "<color=#", color, ">", message, "</color>" });
            MethodInfo addMessageMethod = typeof(PlayerConnectionLog).GetMethod("AddMessage", BindingFlags.Instance | BindingFlags.NonPublic);
            if (addMessageMethod != null)
            {
                addMessageMethod.Invoke(connectionLog, new object[] { formattedMessage });
                if (connectionLog.sfxJoin != null && sound)
                {
                    connectionLog.sfxJoin.Play(default(Vector3));
                    return;
                }
            }
            else
            {
                Logger.LogMessage("AddMessage method not found.");
            }
        }
        //talk As photonview Id
        public static void TalkAs(int ViewID)
        {
            Recorder recorder = Character.localCharacter.transform.GetChild(2).GetComponent<Recorder>();
            if (MyViewID == 0)
            {
                MyViewID = int.TryParse(recorder.UserData.ToString(), out int id) ? id : 0;
                if (MyViewID == 0)
                {
                    YonMod.Notification("Error: Could not get your ViewID. Talk As may not work correctly.", "FF0000", true);
                }
            }
            recorder.UserData = ViewID;
        }
        
        public static void SuperKick()
        {
            if (Character.localCharacter == null) return;

            CharacterGrabbing characterGrabbing = Character.localCharacter.GetComponent<CharacterGrabbing>();
            if (YonMod.enableSuperKick.Value)
            {
                Notification("Super Kick Activated! 🦶", "FF4500", true);
                return;
            }
            //default values for kicks
            characterGrabbing.kickForce = 10f;
            characterGrabbing.kickRange = 3f;
            characterGrabbing.kickRagdollTime = 1f;
            characterGrabbing.kickDistance = 1f;
            characterGrabbing.kickAngle = 45f;
            Notification("Super Kick Deactivated! 👢", "FF4500", true);

        }
        public static void HearYourself()
        {
            Recorder recorder = Character.localCharacter.transform.GetChild(2).GetComponent<Recorder>();
            recorder.DebugEchoMode = HearYourselfToggle.Value;
            Notification("HearYourself is " + (HearYourselfToggle.Value ? "ON" : "OFF"));
        }
        //bugle Patch
        public static void buglePatch(Item item)
        {
            if (!BuglePatchEnabled.Value) return;

            BugleSFX bugleSFX = item.GetComponent<BugleSFX>();
            if (bugleSFX == null) return;

            bugleSFX.pitchMin = BuglePitchMin.Value;
            bugleSFX.pitchMax = BuglePitchMax.Value;
            bugleSFX.volume = BugleVolume.Value;
            bugleSFX.pitchWobble = BuglePitchWobble.Value;

            YonMod.Notification("You patched the bugle! 🎺", "FFD700", true);
        }
        [HarmonyPatch(typeof(CharacterData), nameof(CharacterData.currentItem), MethodType.Setter)]
        public static class CurrentItemPatch
        {
            [HarmonyPostfix]
            static void Postfix(CharacterData __instance, Item value)
            {
                if (value == null) return;
                if (!__instance.GetComponent<Character>().IsLocal) return;

                //YonMod.Notification($"Picked up {value.UIData.itemName}", "FFD700", true);

                if (value.UIData.itemName.ToLower().Contains("bugle"))
                {
                    buglePatch(value);
                }
            }
        }

        //[HarmonyPatch("AntiCheatMod.AntiCheatPlugin", "OnJoinedRoom")]
        static class PatchAntiCheatOnJoinedRoom
        {
            [HarmonyPrefix]
            public static bool Prefix(object __instance)
            {
                Logger.LogInfo("Bypassing Anti-Cheat OnJoinedRoom");
                if (PhotonNetwork.IsMasterClient) return true; // let the master client run the original method

                var enumerator = Traverse.Create(__instance)
                    .Method("SendAntiCheatPingDelayed")
                    .GetValue<IEnumerator>();

                ((MonoBehaviour)__instance).StartCoroutine(enumerator);
                var playerManagerType = AccessTools.TypeByName("AntiCheatMod.PlayerManager");
                var addPlayerMethod = AccessTools.Method(playerManagerType, "AddPlayer");
                foreach (Photon.Realtime.Player player in PhotonNetwork.CurrentRoom.Players.Values)
                {
                    addPlayerMethod.Invoke(null, new object[] { player });
                }

                var inviteLinkType = AccessTools.TypeByName("AntiCheatMod.InviteLinkGenerator");
                var onJoinedRoomMethod = AccessTools.Method(inviteLinkType, "OnJoinedRoom");
                onJoinedRoomMethod.Invoke(null, null);
                PhotonNetwork.RaiseEvent(104, null, new RaiseEventOptions
                {
                    Receivers = ReceiverGroup.Others
                }, SendOptions.SendReliable);

                return false; // skip the original method to prevent crashes
            }
        }
        
        static class ChatFix
        {
            [HarmonyPrefix]
            public static bool Prefix(object __instance)
            {
                if (GUIManager.instance != null && GUIManager.instance.windowBlockingInput) Input.ResetInputAxes();
                return true; // let the rest of the original method run normally
            }
        }

        [HarmonyPatch(typeof(CharacterAnimations), "PlayEmote")]
        static class BackFlipPatch
        {
            [HarmonyTranspiler]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var randomValue = AccessTools.Property(typeof(UnityEngine.Random), nameof(UnityEngine.Random.value)).GetGetMethod();
                var codes = new List<CodeInstruction>(instructions);

                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].Calls(randomValue))
                    {
                        // remove: Random.value, ldc.r4 0.5, cgt
                        // replace with just our method call which returns bool directly
                        codes[i] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BackFlipPatch), nameof(GetSucceeded)));
                        codes.RemoveAt(i + 1); // remove ldc.r4 0.5
                        codes.RemoveAt(i + 1); // remove cgt
                        break;
                    }
                }

                return codes;
            }

            static bool GetSucceeded()
            {
                bool succeeded = UnityEngine.Random.value * 100f > SuccessChance.Value;
                //Notification(succeeded ? "Backflip Failed!" : "Backflip Succeeded!", succeeded ? "FF0000" : "00FF00", true);
                return succeeded;
            }
        }

        private void PatchAll(Harmony harmony)
        {
            var patchTypes = new List<Type>
            {
                typeof(Patch_KickCast_NoStamina),
                typeof(CharacterGrabbing_Update_Patch),
                typeof(CurrentItemPatch),
                typeof(BackFlipPatch),

                // add/remove patches here easily
            };
            foreach (var type in patchTypes)
            {
                harmony.CreateClassProcessor(type).Patch();
            }

            // Optional patches
            StartCoroutine(PatchOptionalLate(harmony));

        }
        private IEnumerator PatchOptionalLate(Harmony harmony)
        {
            if (Chainloader.PluginInfos.ContainsKey("PEAKLobbyBrowser"))
                PatchOptional(harmony, "PEAKLobbyBrowser.PEAKLobbyBrowser", "Start",
                    typeof(PatchPEAKLobbyBrowserStart), nameof(PatchPEAKLobbyBrowserStart.Prefix));

            yield return new WaitForSeconds(1f);
            if (Chainloader.PluginInfos.ContainsKey("synq.peak.atlas"))
            {
                Logger.LogInfo("Atlas plugin detected, applied Atlas FixedUpdate patch");
                PatchOptional(harmony, "Atlas.Plugin", "FixedUpdate",
                    typeof(PatchAtlasUpdate), nameof(PatchAtlasUpdate.Prefix));
            }
            else Logger.LogInfo("Atlas plugin not detected, skipping Atlas FixedUpdate patch");

            if (Chainloader.PluginInfos.ContainsKey("com.dummy.anticheatcontinuum"))
                PatchOptional(harmony, "AntiCheatMod.AntiCheatPlugin", "OnJoinedRoom",
                    typeof(PatchAntiCheatOnJoinedRoom), nameof(PatchAntiCheatOnJoinedRoom.Prefix));

            if (Chainloader.PluginInfos.ContainsKey("evaisa.ThirdPersonToggle"))
                PatchOptional(harmony, "Evaisa.ThirdPersonToggle.ThirdPersonToggle", "MainCameraMovement_LateUpdate",
                    typeof(ChatFix), nameof(ChatFix.Prefix));
            if (Chainloader.PluginInfos.ContainsKey("com.github.LIghtJUNction.TerrainScanner"))
                PatchOptional(harmony, "TerrainScanner.DS.ActiveScan", "Update",
                    typeof(ChatFix), nameof(ChatFix.Prefix));

        }
        private void PatchOptional(Harmony harmony, string typeName, string methodName, Type patchClass, string prefixName)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null) { Logger.LogWarning($"Optional type not found: {typeName}"); return; }
            var method = AccessTools.Method(type, methodName);
            if (method == null) { Logger.LogWarning($"Optional method not found: {typeName}.{methodName}"); return; }

            var prefix = new HarmonyMethod(patchClass, prefixName);
            harmony.Patch(method, prefix: prefix);
            Logger.LogInfo($"Patched optional: {typeName}.{methodName}");
        }
    }
}