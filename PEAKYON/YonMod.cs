using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MyLegDay;
using PEAKLib.UI;
using PEAKLib.UI.Elements;
using Photon.Pun;
using Photon.Voice.Unity;
using Steamworks;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using TMPro;
using UnityEngine;
using UnityEngine.PlayerLoop;
using UnityEngine.SceneManagement;

namespace PEAKYON
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency("com.github.PEAKModding.PEAKLib.UI")]
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

        internal static new ManualLogSource Logger;

        static int MyViewID;
        //static bool isSearching;
        //static List<(CSteamID id, string name, int players, int max)> lobbies = new();
        //static CallResult<LobbyMatchList_t> matchListResult;
        //static PeakCustomPage serverBrowserPage;
        //static GameObject serverBrowserPageObj;
        //static PeakScrollableContent scrollableContent;

        private void Awake()
        {
            Logger = base.Logger;
            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

            Binds();
            HearYourselfToggle.Value = false;
            HearYourselfToggle.SettingChanged += (_, _) => HearYourself();
            BuglePatchEnabled.Value = false;
            BuglePatchEnabled.SettingChanged += (_, _) => Notification("Bugle Patch is " + (BuglePatchEnabled.Value ? "ON" : "OFF"));
            enableSuperKick.SettingChanged += (_, _) => Notification("SuperKick is " + (enableSuperKick.Value ? "ON" : "OFF"));

            TalkAsPV.SettingChanged += (_, _) => TalkAs(TalkAsPV.Value);
            new Harmony("com.yonij.lobbybrowser").PatchAll();
            //matchListResult = CallResult<LobbyMatchList_t>.Create(OnLobbyList);
        }
        private void Update()
        {
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

        [HarmonyPatch(typeof(PEAKLobbyBrowser.PEAKLobbyBrowser), "Start")]
        public static class PatchPEAKLobbyBrowserStart
        {
            static bool Prefix() { 
                Logger.LogInfo("Anti-Cheat Bypassed");
                return false; // returning false skips the original
            }
        }

        //no Stamina patch for kicks
        [HarmonyPatch(typeof(CharacterGrabbing), nameof(CharacterGrabbing.KickCast))]
        static class Patch_KickCast_NoStamina
        {
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


        private void Binds()
        {
            KickPatchToggleKey = Config.Bind("SuperKick", "FastKickToggleKey", KeyCode.K, "Key to toggle the fast kick patch on/off.");
            enableSuperKick = Config.Bind("SuperKick", "SuperKick Enable", true, "Toggle the super kick patch on/off.");
            SuperKickForce = Config.Bind("SuperKick", "SuperKick Force", 50f,
                new ConfigDescription("The force of the SuperKick. (default is 10)", new AcceptableValueRange<float>(-100f, 1500f)));
            SuperKickRange = Config.Bind("SuperKick", "SuperKick Range", 3f,
                new ConfigDescription("The range of the SuperKick. (default is 3)", new AcceptableValueRange<float>(0f, 1000f)));
            SuperKickRagdollTime = Config.Bind("SuperKick", "SuperKick Ragdoll Time", 1f,
                new ConfigDescription("How long the ragdoll effect lasts. (default is 1)", new AcceptableValueRange<float>(0f, 1000f)));
            SuperKickDistance = Config.Bind("SuperKick", "SuperKick Distance", 1f,
                new ConfigDescription("The distance of the SuperKick. (default is 1)", new AcceptableValueRange<float>(0f, 1000f)));
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
            
        }
        //kick cooldown patch
        [HarmonyPatch(typeof(CharacterGrabbing), "Update")]
        public static class CharacterGrabbing_Update_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(CharacterGrabbing __instance)
            {
                if (!YonMod.enableSuperKick.Value)
                {
                    //default values for kicks
                    __instance.kickForce = 10f;
                    __instance.kickRange = 3f;
                    __instance.kickRagdollTime = 1f;
                    __instance.kickDistance = 1f;
                    __instance.kickAngle = 45f;
                    return true;
                }
                __instance.kickForce = 500f;
                var kickTime = Traverse.Create(__instance).Field<float>("_kickTime");
                if (kickTime.Value < 0.6f)
                {
                    kickTime.Value = 0.6f;
                }
                return true;
            }
        }

        //[HarmonyPatch(typeof(Atlas.Plugin), "FixedUpdate")]
        //public static class PatchAtlasUpdate
        //{
        //    static bool Prefix()
        //    {
        //        return false; // skip original
        //    }
        //}
        public static void Notification(string message, string color = "FFFFFF", bool sound = false)
        {
            PlayerConnectionLog connectionLog = Object.FindAnyObjectByType<PlayerConnectionLog>();
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

        //betterkick patch
        //[HarmonyPatch(typeof(BetterKick.Plugin), "AllPlayersHaveMod")]
        //public static class PatchAllPlayersHaveMod
        //{
        //    static bool Prefix(ref bool __result)
        //    {
        //        __result = true;
        //        Logger.LogInfo("BetterKick bypassed");
        //        return false; // skip original
        //    }
        //}

        //private void OnEnable()
        //{
        //    SceneManager.sceneLoaded += OnSceneLoaded;
        //}

        //private void OnDisable()
        //{
        //    SceneManager.sceneLoaded -= OnSceneLoaded;
        //}

        //private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        //{
        //    Logger.LogInfo($"Scene loaded: {scene.name}");
        //    if (scene.name == "Title")
        //        StartCoroutine(CreateButtonAndMenu());
        //}

        //private IEnumerator CreateButtonAndMenu()
        //{
        //    yield return new WaitForSeconds(5f);

        //    GameObject ogButton = null;
        //    while (ogButton == null)
        //    {
        //        ogButton = GameObject.Find("MainMenu/Canvas/MainPage/Menu/Buttons/Button_PlaySolo");
        //        yield return new WaitForSeconds(1f);
        //    }
        //    Logger.LogInfo("Found original button, creating lobby browser button...");

        //    // --- Cloned button ---
        //    GameObject buttonObj = Instantiate(ogButton, GameObject.Find("MainMenu/Canvas/MainPage/").transform);
        //    buttonObj.name = "LobbyBrowserButton";
        //    buttonObj.GetComponent<RectTransform>().localScale = new Vector3(-1.25f, 1.25f, 1);
        //    buttonObj.GetComponent<RectTransform>().position = new Vector2(1710, 625);

        //    RectTransform textRectTransform = buttonObj.transform.Find("Hinge/Text").GetComponent<RectTransform>();
        //    Vector3 textScale = textRectTransform.localScale;
        //    textScale.x *= -1;
        //    textRectTransform.localScale = textScale;

        //    TextMeshProUGUI textComponent = textRectTransform.GetComponent<TextMeshProUGUI>();
        //    textComponent.text = "Lobby Browser";
        //    textComponent.alignment = TextAlignmentOptions.Capline;
        //    textComponent.fontSize = 30;

        //    MakeMenuUI();

        //    // --- Wire up button click ---
        //    UnityEngine.UI.Button btn = buttonObj.GetComponent<UnityEngine.UI.Button>();
        //    btn.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        //    btn.onClick.AddListener(() =>
        //    {
        //        Logger.LogInfo("Lobby Browser button clicked!");
        //        serverBrowserPageObj.SetActive(true);
        //        Refresh();
        //    });

        //    Logger.LogInfo("Lobby browser button and menu created!");
        //}

        //private void MakeMenuUI()
        //{
        //    serverBrowserPage = MenuAPI.CreatePageWithBackground("ServerBrowserPage");
        //    serverBrowserPageObj = ((MonoBehaviour)serverBrowserPage).gameObject;

        //    Transform bg = serverBrowserPageObj.transform.Find("Background");
        //    if (bg == null)
        //    {
        //        Logger.LogInfo("Background not found!");
        //        return;
        //    }

        //    // Create a 600x500 panel centered on the background
        //    GameObject panel = new GameObject("Panel");
        //    panel.transform.SetParent(bg, false);
        //    UnityEngine.UI.Image panelImage = panel.AddComponent<UnityEngine.UI.Image>();
        //    panelImage.color = new Color(0.76f, 0.60f, 0.38f, 0.95f);
        //    RectTransform panelRect = panel.GetComponent<RectTransform>();
        //    panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        //    panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        //    panelRect.pivot = new Vector2(0.5f, 0.5f);
        //    panelRect.sizeDelta = new Vector2(600f, 500f);
        //    panelRect.anchoredPosition = Vector2.zero;

        //    Transform p = panel.transform;

        //    // Title
        //    var title = MenuAPI.CreateText("Server Browser").ParentTo(p).SetFontSize(28);
        //    RectTransform titleRect = title.gameObject.GetComponent<RectTransform>();
        //    titleRect.anchorMin = new Vector2(0, 1);
        //    titleRect.anchorMax = new Vector2(1, 1);
        //    titleRect.pivot = new Vector2(0.5f, 1);
        //    titleRect.anchoredPosition = new Vector2(0, -15);
        //    titleRect.sizeDelta = new Vector2(0, 40);
        //    titleRect.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;

        //    // Refresh button
        //    var refreshBtn = MenuAPI.CreateMenuButton("Refresh").ParentTo(p)
        //        .OnClick(() => { if (!isSearching) Refresh(); });
        //    RectTransform refreshRect = refreshBtn.gameObject.GetComponent<RectTransform>();
        //    refreshRect.anchorMin = new Vector2(0, 1);
        //    refreshRect.anchorMax = new Vector2(0, 1);
        //    refreshRect.pivot = new Vector2(0, 1);
        //    refreshRect.anchoredPosition = new Vector2(15, -60);
        //    refreshRect.sizeDelta = new Vector2(120, 40);

        //    // Close button
        //    var closeBtn = MenuAPI.CreateMenuButton("Close").ParentTo(p)
        //        .OnClick(() => serverBrowserPageObj.SetActive(false));
        //    RectTransform closeRect = closeBtn.gameObject.GetComponent<RectTransform>();
        //    closeRect.anchorMin = new Vector2(1, 1);
        //    closeRect.anchorMax = new Vector2(1, 1);
        //    closeRect.pivot = new Vector2(1, 1);
        //    closeRect.anchoredPosition = new Vector2(-15, -60);
        //    closeRect.sizeDelta = new Vector2(120, 40);

        //    // Scrollable lobby list
        //    var scroll = MenuAPI.CreateScrollableContent("LobbyList").ParentTo(p);
        //    scrollableContent = scroll;
        //    RectTransform scrollRect = scroll.gameObject.GetComponent<RectTransform>();
        //    scrollRect.anchorMin = new Vector2(0, 0);
        //    scrollRect.anchorMax = new Vector2(1, 1);
        //    scrollRect.offsetMin = new Vector2(10, 10);
        //    scrollRect.offsetMax = new Vector2(-10, -110);

        //    UnityEngine.UI.VerticalLayoutGroup vlg = scroll.Content.gameObject.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        //    if (vlg != null)
        //    {
        //        vlg.spacing = 5;
        //        vlg.childForceExpandWidth = true;
        //        vlg.childForceExpandHeight = false;
        //        vlg.childControlWidth = true;
        //        vlg.childControlHeight = true;
        //        vlg.padding = new RectOffset(5, 5, 5, 5);
        //    }

        //    UnityEngine.UI.ContentSizeFitter csf = scroll.Content.gameObject.GetComponent<UnityEngine.UI.ContentSizeFitter>()
        //        ?? scroll.Content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        //    csf.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

        //    serverBrowserPageObj.SetActive(false);
        //}
        //static void RefreshLobbyUI()
        //{
        //    if (scrollableContent == null) return;

        //    foreach (Transform child in scrollableContent.Content)
        //        Destroy(child.gameObject);

        //    if (lobbies.Count == 0)
        //    {
        //        MenuAPI.CreateText(isSearching ? "Searching..." : "No servers found.")
        //            .ParentTo(scrollableContent.Content)
        //            .SetFontSize(22)
        //            .AlignToParent(UIAlignment.TopCenter);
        //        return;
        //    }

        //    foreach (var lobby in lobbies)
        //    {
        //        GameObject row = new GameObject("LobbyRow");
        //        row.transform.SetParent(scrollableContent.Content, false);

        //        UnityEngine.UI.HorizontalLayoutGroup hlg = row.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        //        hlg.spacing = 10;
        //        hlg.childForceExpandHeight = false;
        //        hlg.childForceExpandWidth = false;
        //        hlg.childControlWidth = true;
        //        hlg.childControlHeight = true;
        //        hlg.padding = new RectOffset(10, 10, 5, 5);

        //        UnityEngine.UI.LayoutElement rowLayout = row.AddComponent<UnityEngine.UI.LayoutElement>();
        //        rowLayout.preferredHeight = 50;
        //        rowLayout.flexibleWidth = 1;

        //        // Server name
        //        var nameText = MenuAPI.CreateText(lobby.name).ParentTo(row.transform).SetFontSize(22);
        //        nameText.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1;

        //        // Player count
        //        var countText = MenuAPI.CreateText($"{lobby.players}/{lobby.max}").ParentTo(row.transform).SetFontSize(22);
        //        countText.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 70;

        //        // Join button
        //        var lobbyId = lobby.id;
        //        var joinBtn = MenuAPI.CreateMenuButton("Join").ParentTo(row.transform)
        //            .SetWidth(90)
        //            .OnClick(() =>
        //            {
        //                SteamMatchmaking.JoinLobby(lobbyId);
        //                serverBrowserPageObj.SetActive(false);
        //            });
        //        var joinLayout = joinBtn.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        //        joinLayout.preferredWidth = 90;
        //        joinLayout.minWidth = 90;
        //        joinLayout.preferredHeight = 40;
        //        joinLayout.minHeight = 40;
        //        joinLayout.flexibleWidth = 0;
        //    }
        //}
        //static void Refresh()
        //{
        //    lobbies.Clear();
        //    isSearching = true;
        //    RefreshLobbyUI();
        //    SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
        //    matchListResult.Set(SteamMatchmaking.RequestLobbyList());
        //}

        //static void OnLobbyList(LobbyMatchList_t result, bool failure)
        //{
        //    isSearching = false;
        //    if (failure) return;

        //    for (int i = 0; i < result.m_nLobbiesMatching; i++)
        //    {
        //        var id = SteamMatchmaking.GetLobbyByIndex(i);
        //        string name = SteamMatchmaking.GetLobbyData(id, "name");
        //        if (string.IsNullOrEmpty(name)) name = $"Server {i + 1}";
        //        lobbies.Add((id, name, SteamMatchmaking.GetNumLobbyMembers(id), SteamMatchmaking.GetLobbyMemberLimit(id)));
        //    }

        //    RefreshLobbyUI();
        //}
    }
}