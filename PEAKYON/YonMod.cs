using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using PEAKLib.UI;
using PEAKLib.UI.Elements;
using Steamworks;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PEAKYON
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency("com.github.PEAKModding.PEAKLib.UI")]
    public class YonMod : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;
        static bool isSearching;
        static List<(CSteamID id, string name, int players, int max)> lobbies = new();
        static CallResult<LobbyMatchList_t> matchListResult;
        static PeakCustomPage serverBrowserPage;
        static GameObject serverBrowserPageObj;
        static PeakScrollableContent scrollableContent;

        private void Awake()
        {
            Logger = base.Logger;
            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
            new Harmony("com.yonij.lobbybrowser").PatchAll();
            matchListResult = CallResult<LobbyMatchList_t>.Create(OnLobbyList);
        }
        [HarmonyPatch(typeof(PEAKLobbyBrowser.PEAKLobbyBrowser), "Start")]
        public static class PatchPEAKLobbyBrowserStart
        {
            static bool Prefix() { 
                Logger.LogInfo("Anti-Cheat Bypassed");
                return false; // returning false skips the original
            }
        }
        [HarmonyPatch(typeof(BetterKick.Plugin), "AllPlayersHaveMod")]
        public static class PatchAllPlayersHaveMod
        {
            static bool Prefix(ref bool __result)
            {
                __result = false;
                Logger.LogInfo("BetterKick bypassed");
                return false; // skip original
            }
        }
        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.LogInfo($"Scene loaded: {scene.name}");
            if (scene.name == "Title")
                StartCoroutine(CreateButtonAndMenu());
        }

        private IEnumerator CreateButtonAndMenu()
        {
            yield return new WaitForSeconds(5f);

            GameObject ogButton = null;
            while (ogButton == null)
            {
                ogButton = GameObject.Find("MainMenu/Canvas/MainPage/Menu/Buttons/Button_PlaySolo");
                yield return new WaitForSeconds(1f);
            }
            Logger.LogInfo("Found original button, creating lobby browser button...");

            // --- Cloned button ---
            GameObject buttonObj = Instantiate(ogButton, GameObject.Find("MainMenu/Canvas/MainPage/").transform);
            buttonObj.name = "LobbyBrowserButton";
            buttonObj.GetComponent<RectTransform>().localScale = new Vector3(-1.25f, 1.25f, 1);
            buttonObj.GetComponent<RectTransform>().position = new Vector2(1710, 625);

            RectTransform textRectTransform = buttonObj.transform.Find("Hinge/Text").GetComponent<RectTransform>();
            Vector3 textScale = textRectTransform.localScale;
            textScale.x *= -1;
            textRectTransform.localScale = textScale;

            TextMeshProUGUI textComponent = textRectTransform.GetComponent<TextMeshProUGUI>();
            textComponent.text = "Lobby Browser";
            textComponent.alignment = TextAlignmentOptions.Capline;
            textComponent.fontSize = 30;

            MakeMenuUI();

            // --- Wire up button click ---
            UnityEngine.UI.Button btn = buttonObj.GetComponent<UnityEngine.UI.Button>();
            btn.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            btn.onClick.AddListener(() =>
            {
                Logger.LogInfo("Lobby Browser button clicked!");
                serverBrowserPageObj.SetActive(true);
                Refresh();
            });

            Logger.LogInfo("Lobby browser button and menu created!");
        }

        private void MakeMenuUI()
        {
            serverBrowserPage = MenuAPI.CreatePageWithBackground("ServerBrowserPage");
            serverBrowserPageObj = ((MonoBehaviour)serverBrowserPage).gameObject;

            Transform bg = serverBrowserPageObj.transform.Find("Background");
            if (bg == null)
            {
                Logger.LogInfo("Background not found!");
                return;
            }

            // Create a 600x500 panel centered on the background
            GameObject panel = new GameObject("Panel");
            panel.transform.SetParent(bg, false);
            UnityEngine.UI.Image panelImage = panel.AddComponent<UnityEngine.UI.Image>();
            panelImage.color = new Color(0.76f, 0.60f, 0.38f, 0.95f);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(600f, 500f);
            panelRect.anchoredPosition = Vector2.zero;

            Transform p = panel.transform;

            // Title
            var title = MenuAPI.CreateText("Server Browser").ParentTo(p).SetFontSize(28);
            RectTransform titleRect = title.gameObject.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.pivot = new Vector2(0.5f, 1);
            titleRect.anchoredPosition = new Vector2(0, -15);
            titleRect.sizeDelta = new Vector2(0, 40);
            titleRect.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;

            // Refresh button
            var refreshBtn = MenuAPI.CreateMenuButton("Refresh").ParentTo(p)
                .OnClick(() => { if (!isSearching) Refresh(); });
            RectTransform refreshRect = refreshBtn.gameObject.GetComponent<RectTransform>();
            refreshRect.anchorMin = new Vector2(0, 1);
            refreshRect.anchorMax = new Vector2(0, 1);
            refreshRect.pivot = new Vector2(0, 1);
            refreshRect.anchoredPosition = new Vector2(15, -60);
            refreshRect.sizeDelta = new Vector2(120, 40);

            // Close button
            var closeBtn = MenuAPI.CreateMenuButton("Close").ParentTo(p)
                .OnClick(() => serverBrowserPageObj.SetActive(false));
            RectTransform closeRect = closeBtn.gameObject.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1, 1);
            closeRect.anchorMax = new Vector2(1, 1);
            closeRect.pivot = new Vector2(1, 1);
            closeRect.anchoredPosition = new Vector2(-15, -60);
            closeRect.sizeDelta = new Vector2(120, 40);

            // Scrollable lobby list
            var scroll = MenuAPI.CreateScrollableContent("LobbyList").ParentTo(p);
            scrollableContent = scroll;
            RectTransform scrollRect = scroll.gameObject.GetComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0, 0);
            scrollRect.anchorMax = new Vector2(1, 1);
            scrollRect.offsetMin = new Vector2(10, 10);
            scrollRect.offsetMax = new Vector2(-10, -110);

            UnityEngine.UI.VerticalLayoutGroup vlg = scroll.Content.gameObject.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.spacing = 5;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;
                vlg.childControlWidth = true;
                vlg.childControlHeight = true;
                vlg.padding = new RectOffset(5, 5, 5, 5);
            }

            UnityEngine.UI.ContentSizeFitter csf = scroll.Content.gameObject.GetComponent<UnityEngine.UI.ContentSizeFitter>()
                ?? scroll.Content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            csf.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            serverBrowserPageObj.SetActive(false);
        }
        static void RefreshLobbyUI()
        {
            if (scrollableContent == null) return;

            foreach (Transform child in scrollableContent.Content)
                Destroy(child.gameObject);

            if (lobbies.Count == 0)
            {
                MenuAPI.CreateText(isSearching ? "Searching..." : "No servers found.")
                    .ParentTo(scrollableContent.Content)
                    .SetFontSize(22)
                    .AlignToParent(UIAlignment.TopCenter);
                return;
            }

            foreach (var lobby in lobbies)
            {
                GameObject row = new GameObject("LobbyRow");
                row.transform.SetParent(scrollableContent.Content, false);

                UnityEngine.UI.HorizontalLayoutGroup hlg = row.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                hlg.spacing = 10;
                hlg.childForceExpandHeight = false;
                hlg.childForceExpandWidth = false;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.padding = new RectOffset(10, 10, 5, 5);

                UnityEngine.UI.LayoutElement rowLayout = row.AddComponent<UnityEngine.UI.LayoutElement>();
                rowLayout.preferredHeight = 50;
                rowLayout.flexibleWidth = 1;

                // Server name
                var nameText = MenuAPI.CreateText(lobby.name).ParentTo(row.transform).SetFontSize(22);
                nameText.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1;

                // Player count
                var countText = MenuAPI.CreateText($"{lobby.players}/{lobby.max}").ParentTo(row.transform).SetFontSize(22);
                countText.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 70;

                // Join button
                var lobbyId = lobby.id;
                var joinBtn = MenuAPI.CreateMenuButton("Join").ParentTo(row.transform)
                    .SetWidth(90)
                    .OnClick(() =>
                    {
                        SteamMatchmaking.JoinLobby(lobbyId);
                        serverBrowserPageObj.SetActive(false);
                    });
                var joinLayout = joinBtn.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                joinLayout.preferredWidth = 90;
                joinLayout.minWidth = 90;
                joinLayout.preferredHeight = 40;
                joinLayout.minHeight = 40;
                joinLayout.flexibleWidth = 0;
            }
        }
        static void Refresh()
        {
            lobbies.Clear();
            isSearching = true;
            RefreshLobbyUI();
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            matchListResult.Set(SteamMatchmaking.RequestLobbyList());
        }

        static void OnLobbyList(LobbyMatchList_t result, bool failure)
        {
            isSearching = false;
            if (failure) return;

            for (int i = 0; i < result.m_nLobbiesMatching; i++)
            {
                var id = SteamMatchmaking.GetLobbyByIndex(i);
                string name = SteamMatchmaking.GetLobbyData(id, "name");
                if (string.IsNullOrEmpty(name)) name = $"Server {i + 1}";
                lobbies.Add((id, name, SteamMatchmaking.GetNumLobbyMembers(id), SteamMatchmaking.GetLobbyMemberLimit(id)));
            }

            RefreshLobbyUI();
        }
    }
}