using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using Bulbul;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Sherry.CostumeControl
{
    [BepInPlugin("sherry.chillwithyou.costumecontrol", "Sherry Costume Control", "1.0.0")]
    public sealed class CostumeControlPlugin : BaseUnityPlugin
    {
        private static readonly CostumeChangeService.CostumeSkinType[] SkinTypes =
        {
            CostumeChangeService.CostumeSkinType.Default_1,
            CostumeChangeService.CostumeSkinType.Polo_1,
            CostumeChangeService.CostumeSkinType.Polo_2,
            CostumeChangeService.CostumeSkinType.Tee_1,
            CostumeChangeService.CostumeSkinType.Tee_2,
        };

        private static readonly System.Random Random = new System.Random();
        private static ManualLogSource Log = null!;
        private static Font? UiFont;
        private static CostumeChangeService? CurrentService;
        private static Bulbul.HeroineService? CurrentHeroineService;
        private static string ConfigFilePath = string.Empty;
        private static DateTime LastConfigWriteTimeUtc;
        private static string Mode = "fixed";
        private static CostumeChangeService.CostumeSkinType SelectedSkinType = CostumeChangeService.CostumeSkinType.Polo_2;
        private static float ToastEndTime;
        private static string ToastText = string.Empty;
        private static bool HasLoggedUpdate;
        private static bool HasReportedUiRect;
        private static int LastHotkeyFrame = -1;
        private static bool IsWindowVisible;
        private static readonly string[] RecentLogs = new string[10];
        private static int RecentLogCount;
        private static GameObject? UiRoot;
        private static GameObject? PanelObject;
        private static GameObject? ToggleObject;
        private static GameObject[] PageObjects = Array.Empty<GameObject>();
        private static Text? StatusText;
        private static Text? LogText;
        private static Text? PageText;
        private static int CurrentPageIndex;
        // CanvasScaler 会把 anchoredPosition 按分辨率放大（1920 宽约 2.4 倍），
        // 原始默认值 -222 / -150 会被放大成 500+ 像素偏移，把 UI 顶出屏幕外。
        private static Vector2 TogglePosition = new Vector2(-40f, -80f);
        private static Vector2 PanelPosition = new Vector2(-40f, -20f);

        private void Awake()
        {
            Log = Logger;
            ConfigFilePath = Path.Combine(Paths.ConfigPath, "Sherry.CostumeControl.json");
            EnsureConfigFile();
            LoadConfig();
            new Harmony("sherry.chillwithyou.costumecontrol").PatchAll(typeof(CostumeControlPlugin).Assembly);
            // 插件自身的 MonoBehaviour 可能被游戏在场景切换时销毁，导致 Update 停止。
            // 本游戏用 URP 渲染管线，Camera.onPostRender 不会触发，必须用 RenderPipelineManager。
            Camera.onPostRender += OnCameraPostRender;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;


            AddLog("衣服控制插件已加载。F7 显示/隐藏面板。");
            ToastText = "Sherry Costume Control loaded - press F7 for the costume panel";
            ToastEndTime = Time.realtimeSinceStartup + 20f;
        }

        private static void OnCameraPostRender(Camera camera)
        {
            PollHotkeys();
        }

        private static void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            PollHotkeys();
        }

        private static void PollHotkeys()
        {
            if (Time.frameCount == LastHotkeyFrame)
            {
                return;
            }

            LastHotkeyFrame = Time.frameCount;

            if (!HasLoggedUpdate)
            {
                HasLoggedUpdate = true;
                Log?.LogInfo("热键轮询已启动（渲染事件）。");
            }

            // UI 刚创建时 CanvasScaler 尚未生效，延后一帧才拿得到真实屏幕位置
            if (!HasReportedUiRect && UiRoot != null)
            {
                HasReportedUiRect = true;
                ReportUiRect();
            }

            if (Input.GetKeyDown(KeyCode.F10))
            {
                ReloadConfigByUser();
            }
            else if (Input.GetKeyDown(KeyCode.F7))
            {
                SetPanelVisible(!IsWindowVisible);
            }
            else if (Input.GetKeyDown(KeyCode.F8))
            {
                RandomNextByUser();
            }
            else if (Input.GetKeyDown(KeyCode.F9))
            {
                FixCurrentByUser();
            }
        }

        private static void ReportUiRect()
        {
            Log?.LogInfo("衣装面板：屏幕 " + Screen.width + "x" + Screen.height);

            if (ToggleObject != null)
            {
                var corners = new Vector3[4];
                ToggleObject.GetComponent<RectTransform>().GetWorldCorners(corners);
                Log?.LogInfo("衣装按钮实际屏幕位置 左下=" + corners[0].ToString("0") + " 右上=" + corners[2].ToString("0"));
            }

            if (PanelObject != null)
            {
                var corners = new Vector3[4];
                PanelObject.GetComponent<RectTransform>().GetWorldCorners(corners);
                Log?.LogInfo("衣装面板实际屏幕位置 左下=" + corners[0].ToString("0") + " 右上=" + corners[2].ToString("0"));
            }
        }

        private void Update()
        {
            PollHotkeys();
        }

        private void OnGUI()
        {
            var currentEvent = Event.current;
            if (currentEvent != null && currentEvent.type == EventType.KeyDown)
            {
                if (currentEvent.keyCode == KeyCode.F7)
                {
                    SetPanelVisible(!IsWindowVisible);
                    currentEvent.Use();
                }
                else if (currentEvent.keyCode == KeyCode.F8)
                {
                    RandomNextByUser();
                    currentEvent.Use();
                }
                else if (currentEvent.keyCode == KeyCode.F9)
                {
                    FixCurrentByUser();
                    currentEvent.Use();
                }
                else if (currentEvent.keyCode == KeyCode.F10)
                {
                    ReloadConfigByUser();
                    currentEvent.Use();
                }
            }

            if (Time.realtimeSinceStartup <= ToastEndTime)
            {
                GUI.Label(new Rect(16f, 16f, 640f, 32f), ToastText);
            }
        }

        private static void EnsureUi()
        {
            if (UiRoot != null)
            {
                UpdateUiText();
                SyncPanelVisible();
                return;
            }

            EnsureEventSystem();

            UiRoot = new GameObject("SherryCostumeControlCanvas");
            UnityEngine.Object.DontDestroyOnLoad(UiRoot);

            var canvas = UiRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            UiRoot.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            UiRoot.AddComponent<GraphicRaycaster>();

            var panel = CreateAnchoredRect("Panel", UiRoot.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), PanelPosition, new Vector2(330f, 314f));
            PanelObject = panel.gameObject;
            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = new Color(0.04f, 0.05f, 0.07f, 0.86f);
            AddDraggable(panel, position =>
            {
                PanelPosition = position;
                SaveConfig();
            });

            ToggleObject = CreateCircleButton(UiRoot.transform, TogglePosition, 48f, "衣装", () =>
            {
                SetPanelVisible(true);
            });
            AddDraggable(ToggleObject.GetComponent<RectTransform>(), position =>
            {
                TogglePosition = position;
                SaveConfig();
            });


            var title = CreateText("Title", panel, new Vector2(14f, -12f), new Vector2(300f, 28f), "衣装", 20, TextAnchor.MiddleLeft);
            title.color = new Color(1f, 0.88f, 0.96f, 1f);

            CreateButton(panel, new Vector2(254f, -10f), new Vector2(58f, 30f), "收起", () =>
            {
                SetPanelVisible(false);
            });

            StatusText = CreateText("Status", panel, new Vector2(14f, -45f), new Vector2(298f, 44f), string.Empty, 12, TextAnchor.UpperLeft);

            var costumePage = CreatePage(panel, "CostumePage");
            CreateText("CostumeHint", costumePage.transform, new Vector2(14f, -2f), new Vector2(298f, 24f), "衣服页：点击后立即切换并固定到今天。", 12, TextAnchor.UpperLeft).color = new Color(0.82f, 0.86f, 0.96f, 1f);
            CreateButton(costumePage.transform, new Vector2(14f, -34f), new Vector2(96f, 32f), "Default", () => SelectSkinByUi(CostumeChangeService.CostumeSkinType.Default_1));
            CreateButton(costumePage.transform, new Vector2(118f, -34f), new Vector2(96f, 32f), "Polo 1", () => SelectSkinByUi(CostumeChangeService.CostumeSkinType.Polo_1));
            CreateButton(costumePage.transform, new Vector2(222f, -34f), new Vector2(96f, 32f), "Polo 2", () => SelectSkinByUi(CostumeChangeService.CostumeSkinType.Polo_2));
            CreateButton(costumePage.transform, new Vector2(14f, -74f), new Vector2(96f, 32f), "Tee 1", () => SelectSkinByUi(CostumeChangeService.CostumeSkinType.Tee_1));
            CreateButton(costumePage.transform, new Vector2(118f, -74f), new Vector2(96f, 32f), "Tee 2", () => SelectSkinByUi(CostumeChangeService.CostumeSkinType.Tee_2));
            CreateButton(costumePage.transform, new Vector2(222f, -74f), new Vector2(96f, 32f), "随机", RandomNextByUser);
            CreateButton(costumePage.transform, new Vector2(14f, -120f), new Vector2(148f, 32f), "应用并固定", () =>
            {
                Mode = "fixed";
                SaveConfig();
                ApplySkinType(SelectedSkinType, true);
                AddLog($"已应用并固定：{SelectedSkinType} ({(int)SelectedSkinType})");
            });
            CreateButton(costumePage.transform, new Vector2(170f, -120f), new Vector2(148f, 32f), "重载配置", ReloadConfigByUser);

            var actionPage = CreatePage(panel, "ActionPage");
            CreateText("ActionHint", actionPage.transform, new Vector2(14f, -2f), new Vector2(298f, 24f), "动作页：剧情或计时中可能被游戏状态覆盖。", 12, TextAnchor.UpperLeft).color = new Color(0.82f, 0.86f, 0.96f, 1f);
            CreateButton(actionPage.transform, new Vector2(14f, -34f), new Vector2(96f, 32f), "伸展", () => SelectActionByUi(HeroineAI.ActionStateType.WildStretchFullBody, "伸展"));
            CreateButton(actionPage.transform, new Vector2(118f, -34f), new Vector2(96f, 32f), "喝茶", () => SelectActionByUi(HeroineAI.ActionStateType.WildTea, "喝茶"));
            CreateButton(actionPage.transform, new Vector2(222f, -34f), new Vector2(96f, 32f), "打气", () => SelectActionByUi(HeroineAI.ActionStateType.WildGuts, "打气"));
            CreateButton(actionPage.transform, new Vector2(14f, -74f), new Vector2(96f, 32f), "读书", () => SelectActionByUi(HeroineAI.ActionStateType.BreakReadBook, "读书"));
            CreateButton(actionPage.transform, new Vector2(118f, -74f), new Vector2(96f, 32f), "休息", () => SelectActionByUi(HeroineAI.ActionStateType.BreakForward, "休息"));
            CreateButton(actionPage.transform, new Vector2(222f, -74f), new Vector2(96f, 32f), "互动", PlayTouchReactionByUi);
            CreateText("ActionTip", actionPage.transform, new Vector2(14f, -120f), new Vector2(298f, 24f), "无变化时，先等角色进入房间主界面。", 12, TextAnchor.UpperLeft).color = new Color(0.82f, 0.86f, 0.96f, 1f);

            var logPage = CreatePage(panel, "LogPage");
            CreateText("LogHint", logPage.transform, new Vector2(14f, -4f), new Vector2(298f, 28f), "日志页：用于确认服务捕获、配置重载和失败原因。", 12, TextAnchor.UpperLeft).color = new Color(0.82f, 0.86f, 0.96f, 1f);
            LogText = CreateText("Logs", logPage.transform, new Vector2(14f, -36f), new Vector2(298f, 78f), string.Empty, 12, TextAnchor.UpperLeft);
            CreateButton(logPage.transform, new Vector2(14f, -120f), new Vector2(148f, 32f), "重置位置", ResetUiPositions);
            CreateButton(logPage.transform, new Vector2(170f, -120f), new Vector2(148f, 32f), "清空日志", ClearRecentLogs);

            PageObjects = new[] { costumePage, actionPage, logPage };
            CreateButton(panel, new Vector2(14f, -268f), new Vector2(72f, 30f), "上页", () => SetUiPage(CurrentPageIndex - 1));
            PageText = CreateText("PageText", panel, new Vector2(98f, -268f), new Vector2(134f, 30f), string.Empty, 13, TextAnchor.MiddleCenter);
            CreateButton(panel, new Vector2(246f, -268f), new Vector2(72f, 30f), "下页", () => SetUiPage(CurrentPageIndex + 1));
            SetUiPage(CurrentPageIndex);
            UpdateUiText();
            SyncPanelVisible();
        }

        private static void SetPanelVisible(bool isVisible)
        {
            IsWindowVisible = isVisible;
            SyncPanelVisible();
            AddLog(isVisible ? "已展开衣装面板。" : "已收起衣装面板。");
        }

        private static void SyncPanelVisible()
        {
            PanelObject?.SetActive(IsWindowVisible);
            ToggleObject?.SetActive(!IsWindowVisible);
        }

        private static void ResetUiPositions()
        {
            TogglePosition = new Vector2(-40f, -80f);
            PanelPosition = new Vector2(-40f, -20f);

            if (ToggleObject != null)
            {
                ToggleObject.GetComponent<RectTransform>().anchoredPosition = TogglePosition;
            }

            if (PanelObject != null)
            {
                PanelObject.GetComponent<RectTransform>().anchoredPosition = PanelPosition;
            }

            SaveConfig();
            AddLog("已重置衣装按钮和面板位置。");
        }

        private static void ClearRecentLogs()
        {
            Array.Clear(RecentLogs, 0, RecentLogs.Length);
            RecentLogCount = 0;
            UpdateUiText();
            AddLog("已清空面板日志。");
        }

        private static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            var eventSystemObject = new GameObject("SherryCostumeControlEventSystem");
            UnityEngine.Object.DontDestroyOnLoad(eventSystemObject);
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<StandaloneInputModule>();
        }

        private static RectTransform CreateRect(string name, Transform parent, Vector2 anchoredPosition, Vector2 size)
        {
            return CreateAnchoredRect(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), anchoredPosition, size);
        }

        private static RectTransform CreateAnchoredRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            var rectObject = new GameObject(name);
            rectObject.transform.SetParent(parent, false);
            var rectTransform = rectObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.pivot = pivot;
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = size;
            return rectTransform;
        }

        private static GameObject CreatePage(Transform parent, string name)
        {
            var rectTransform = CreateRect(name, parent, new Vector2(0f, -96f), new Vector2(330f, 154f));
            return rectTransform.gameObject;
        }

        private static void SetUiPage(int pageIndex)
        {
            if (PageObjects.Length == 0)
            {
                return;
            }

            CurrentPageIndex = Mathf.Clamp(pageIndex, 0, PageObjects.Length - 1);

            for (var index = 0; index < PageObjects.Length; index++)
            {
                PageObjects[index].SetActive(index == CurrentPageIndex);
            }

            if (PageText != null)
            {
                var pageName = CurrentPageIndex == 0 ? "衣服" : CurrentPageIndex == 1 ? "动作" : "日志";
                PageText.text = $"{pageName} {CurrentPageIndex + 1}/{PageObjects.Length}";
            }
        }

        private static void AddDraggable(RectTransform rectTransform, Action<Vector2> onDragEnded)
        {
            var dragHandler = rectTransform.gameObject.AddComponent<DraggableRect>();
            dragHandler.Initialize(onDragEnded);
        }

        private static Text CreateText(string name, Transform parent, Vector2 anchoredPosition, Vector2 size, string content, int fontSize, TextAnchor alignment)
        {
            var rectTransform = CreateRect(name, parent, anchoredPosition, size);
            var text = rectTransform.gameObject.AddComponent<Text>();
            text.font = GetUiFont();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.text = content;
            return text;
        }

        private static Font? GetUiFont()
        {
            if (UiFont != null)
            {
                return UiFont;
            }

            var candidates = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "SimSun", "Arial" };
            foreach (var name in candidates)
            {
                try
                {
                    var font = Font.CreateDynamicFontFromOSFont(name, 16);
                    if (font != null)
                    {
                        font.hideFlags = HideFlags.HideAndDontSave;
                        UnityEngine.Object.DontDestroyOnLoad(font);
                        UiFont = font;
                        return UiFont;
                    }
                }
                catch (Exception ex)
                {
                    Log?.LogDebug("字体创建失败 " + name + "：" + ex.Message);
                }
            }

            UiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return UiFont;
        }

        private static GameObject CreateButton(Transform parent, Vector2 anchoredPosition, Vector2 size, string label, Action onClick)
        {
            var rectTransform = CreateRect(label + "Button", parent, anchoredPosition, size);
            var image = rectTransform.gameObject.AddComponent<Image>();
            image.color = new Color(0.18f, 0.2f, 0.26f, 0.96f);

            var button = rectTransform.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());

            var text = CreateText(label + "Text", rectTransform, new Vector2(0f, 0f), size, label, 14, TextAnchor.MiddleCenter);
            text.raycastTarget = false;
            return rectTransform.gameObject;
        }

        private static GameObject CreateCircleButton(Transform parent, Vector2 anchoredPosition, float size, string label, Action onClick)
        {
            var rectTransform = CreateAnchoredRect(label + "CircleButton", parent, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), anchoredPosition, new Vector2(size, size));
            var image = rectTransform.gameObject.AddComponent<Image>();
            image.sprite = CreateCircleSprite(96, new Color(0.12f, 0.13f, 0.18f, 0.82f), new Color(0.72f, 0.76f, 0.88f, 0.86f));

            var button = rectTransform.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());

            var text = CreateText(label + "Text", rectTransform, new Vector2(0f, 0f), new Vector2(size, size), label, 18, TextAnchor.MiddleCenter);
            text.raycastTarget = false;
            return rectTransform.gameObject;
        }

        private static Sprite CreateCircleSprite(int size, Color fillColor, Color borderColor)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var center = (size - 1) * 0.5f;
            var radius = size * 0.46f;
            var innerRadius = radius - 4f;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);
                    var color = Color.clear;

                    if (distance <= innerRadius)
                    {
                        color = fillColor;
                    }
                    else if (distance <= radius)
                    {
                        color = borderColor;
                    }

                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }

        private static void SelectSkinByUi(CostumeChangeService.CostumeSkinType skinType)
        {
            SelectedSkinType = skinType;
            Mode = "fixed";
            SaveConfig();
            ApplySkinType(skinType, true);
            AddLog($"已选择：{skinType} ({(int)skinType})");
        }

        private static void UpdateUiText()
        {
            if (StatusText != null)
            {
                StatusText.text = $"衣服服务：{(CurrentService == null ? "未捕获" : "已捕获")}    动作服务：{(CurrentHeroineService == null ? "未捕获" : "已捕获")}\n当前衣服：{SelectedSkinType} ({(int)SelectedSkinType})";
            }

            if (LogText != null)
            {
                LogText.text = string.Join("\n", RecentLogs.Take(RecentLogCount));
            }
        }

        private static void RandomNextByUser()
        {
            var nextSkinType = GetRandomSkinType();
            Mode = "fixed";
            SelectedSkinType = nextSkinType;
            SaveConfig();
            ApplySkinType(nextSkinType, true);
            ShowToast($"Sherry衣服控制：随机切换到 {nextSkinType} ({(int)nextSkinType})");
            AddLog($"已随机切换衣服：{nextSkinType} ({(int)nextSkinType})");
        }

        private static void SelectActionByUi(HeroineAI.ActionStateType actionStateType, string label)
        {
            if (CurrentHeroineService == null)
            {
                AddLog("还没有捕获到动作服务，进入房间主界面后再试。");
                return;
            }

            try
            {
                CurrentHeroineService.DebugChangeState(actionStateType);
                AddLog($"已请求动作：{label} ({actionStateType})");
            }
            catch (Exception ex)
            {
                AddLog($"动作请求失败：{ex.Message}");
            }
        }

        private static void PlayTouchReactionByUi()
        {
            if (CurrentHeroineService == null)
            {
                AddLog("还没有捕获到动作服务，进入房间主界面后再试。");
                return;
            }

            try
            {
                CurrentHeroineService.OnStartClickHeroineReaction();
                AddLog("已请求互动反应。");
            }
            catch (Exception ex)
            {
                AddLog($"互动请求失败：{ex.Message}");
            }
        }

        private static void FixCurrentByUser()
        {
            Mode = "fixed";
            SaveConfig();
            SaveTodaySkinType(SelectedSkinType);
            ShowToast($"Sherry衣服控制：已固定 {SelectedSkinType} ({(int)SelectedSkinType})");
            AddLog($"已固定当前配置衣服：{SelectedSkinType} ({(int)SelectedSkinType})");
        }

        private static void ReloadConfigByUser()
        {
            LoadConfig();
            ShowToast($"Sherry衣服控制：已重载 {SelectedSkinType} ({(int)SelectedSkinType})");
            AddLog("已重载衣服配置。");
        }

        private static void ShowToast(string text)
        {
            ToastText = text;
            ToastEndTime = Time.realtimeSinceStartup + 3f;
        }

        private static void AddLog(string message)
        {
            var text = DateTime.Now.ToString("HH:mm:ss") + "  " + message;

            if (RecentLogCount < RecentLogs.Length)
            {
                RecentLogCount++;
            }

            for (var index = RecentLogCount - 1; index > 0; index--)
            {
                RecentLogs[index] = RecentLogs[index - 1];
            }

            RecentLogs[0] = text;
            Log?.LogInfo(message);
            UpdateUiText();
        }

        private static bool TryGetForcedSkinType(out CostumeChangeService.CostumeSkinType skinType)
        {
            ReloadConfigIfChanged();
            skinType = SelectedSkinType;
            return string.Equals(Mode, "fixed", StringComparison.OrdinalIgnoreCase);
        }

        private static void CaptureService(CostumeChangeService service)
        {
            if (CurrentService == null)
            {
                AddLog("已捕获衣服服务。");
            }

            CurrentService = service;
            EnsureUi();
        }

        private static void CaptureHeroineService(Bulbul.HeroineService service)
        {
            if (CurrentHeroineService == null)
            {
                AddLog("已捕获动作服务。");
            }

            CurrentHeroineService = service;
            EnsureUi();
        }

        private static void ApplySkinType(CostumeChangeService.CostumeSkinType skinType, bool save)
        {
            if (CurrentService == null)
            {
                AddLog("还没有捕获到衣服服务，进入房间主界面后再试。");
                return;
            }

            CurrentService.ChangeCostume(skinType);

            if (save)
            {
                SaveTodaySkinType(skinType);
            }
        }

        private static void SaveTodaySkinType(CostumeChangeService.CostumeSkinType skinType)
        {
            try
            {
                var latestChangeData = SaveDataManager.Instance.CostumeChangeSaveData.LatestChangeData;
                latestChangeData.SetChangedCostumeDate();
                latestChangeData.SetChangedCostumeSkinType(skinType);
                SaveDataManager.Instance.SaveCostumeChangeSaveData();
            }
            catch (Exception ex)
            {
                AddLog($"保存今日衣服失败：{ex.Message}");
            }
        }

        private static CostumeChangeService.CostumeSkinType GetRandomSkinType()
        {
            var candidates = SkinTypes.Where(skinType => skinType != SelectedSkinType).ToArray();
            return candidates[Random.Next(candidates.Length)];
        }

        private static void EnsureConfigFile()
        {
            if (File.Exists(ConfigFilePath))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ConfigFilePath)!);
            File.WriteAllText(ConfigFilePath, CreateConfigContent(), new UTF8Encoding(false));
        }

        private static void LoadConfig()
        {
            EnsureConfigFile();
            var text = File.ReadAllText(ConfigFilePath, Encoding.UTF8);
            Mode = ReadString(text, "mode", "fixed");
            var skinTypeValue = ReadInt(text, "skinType", (int)SelectedSkinType);

            if (Enum.IsDefined(typeof(CostumeChangeService.CostumeSkinType), skinTypeValue))
            {
                SelectedSkinType = (CostumeChangeService.CostumeSkinType)skinTypeValue;
            }
            else
            {
                AddLog($"配置中的 skinType 无效：{skinTypeValue}，已继续使用 {SelectedSkinType}。");
            }

            TogglePosition = new Vector2(ReadFloat(text, "toggleX", TogglePosition.x), ReadFloat(text, "toggleY", TogglePosition.y));
            PanelPosition = new Vector2(ReadFloat(text, "panelX", PanelPosition.x), ReadFloat(text, "panelY", PanelPosition.y));

            LastConfigWriteTimeUtc = File.GetLastWriteTimeUtc(ConfigFilePath);
        }

        private static void ReloadConfigIfChanged()
        {
            if (!File.Exists(ConfigFilePath))
            {
                EnsureConfigFile();
                LoadConfig();
                return;
            }

            var writeTimeUtc = File.GetLastWriteTimeUtc(ConfigFilePath);
            if (writeTimeUtc != LastConfigWriteTimeUtc)
            {
                LoadConfig();
            }
        }

        private static void SaveConfig()
        {
            File.WriteAllText(ConfigFilePath, CreateConfigContent(), new UTF8Encoding(false));
            LastConfigWriteTimeUtc = File.GetLastWriteTimeUtc(ConfigFilePath);
        }

        private static string CreateConfigContent()
        {
            return "{\n"
                + "  \"mode\": \"" + Mode + "\",\n"
                + "  \"skinType\": " + (int)SelectedSkinType + ",\n"
                + "  \"toggleX\": " + FormatFloat(TogglePosition.x) + ",\n"
                + "  \"toggleY\": " + FormatFloat(TogglePosition.y) + ",\n"
                + "  \"panelX\": " + FormatFloat(PanelPosition.x) + ",\n"
                + "  \"panelY\": " + FormatFloat(PanelPosition.y) + ",\n"
                + "  \"availableSkinTypes\": {\n"
                + "    \"Default_1\": 1,\n"
                + "    \"Polo_1\": 1001,\n"
                + "    \"Polo_2\": 1002,\n"
                + "    \"Tee_1\": 2001,\n"
                + "    \"Tee_2\": 2002\n"
                + "  },\n"
                + "  \"hotkeys\": {\n"
                + "    \"randomNext\": \"F8\",\n"
                + "    \"fixCurrent\": \"F9\",\n"
                + "    \"reloadConfig\": \"F10\"\n"
                + "  }\n"
                + "}\n";
        }

        private static string ReadString(string text, string key, string defaultValue)
        {
            var match = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"");
            return match.Success ? match.Groups[1].Value : defaultValue;
        }

        private static int ReadInt(string text, string key, int defaultValue)
        {
            var match = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+)");
            return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : defaultValue;
        }

        private static float ReadFloat(string text, string key, float defaultValue)
        {
            var match = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)");
            return match.Success && float.TryParse(match.Groups[1].Value, out var value) ? value : defaultValue;
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private sealed class DraggableRect : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            private RectTransform? RectTransform;
            private Canvas? Canvas;
            private Action<Vector2>? OnDragEnded;

            public void Initialize(Action<Vector2> onDragEnded)
            {
                RectTransform = GetComponent<RectTransform>();
                Canvas = GetComponentInParent<Canvas>();
                OnDragEnded = onDragEnded;
            }

            public void OnBeginDrag(PointerEventData eventData)
            {
                if (RectTransform == null)
                {
                    RectTransform = GetComponent<RectTransform>();
                }

                if (Canvas == null)
                {
                    Canvas = GetComponentInParent<Canvas>();
                }
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (RectTransform == null)
                {
                    return;
                }

                var scaleFactor = Canvas == null || Canvas.scaleFactor <= 0f ? 1f : Canvas.scaleFactor;
                RectTransform.anchoredPosition += eventData.delta / scaleFactor;
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                if (RectTransform == null)
                {
                    return;
                }

                OnDragEnded?.Invoke(RectTransform.anchoredPosition);
            }
        }

        [HarmonyPatch(typeof(CostumeChangeService), nameof(CostumeChangeService.ChangeTodayCostume))]
        private static class ChangeTodayCostumePatch
        {
            private static void Prefix(CostumeChangeService __instance)
            {
                CaptureService(__instance);
            }
        }

        [HarmonyPatch(typeof(Bulbul.HeroineService), nameof(Bulbul.HeroineService.OnGameStart))]
        private static class HeroineServiceOnGameStartPatch
        {
            private static void Prefix(Bulbul.HeroineService __instance)
            {
                CaptureHeroineService(__instance);
            }
        }

        [HarmonyPatch(typeof(Bulbul.HeroineService), nameof(Bulbul.HeroineService.DebugChangeState))]
        private static class HeroineServiceDebugChangeStatePatch
        {
            private static void Prefix(Bulbul.HeroineService __instance)
            {
                CaptureHeroineService(__instance);
            }
        }

        [HarmonyPatch(typeof(CostumeChangeService), nameof(CostumeChangeService.ChangeCostume))]
        private static class ChangeCostumePatch
        {
            private static void Prefix(CostumeChangeService __instance, ref CostumeChangeService.CostumeSkinType skinType)
            {
                CaptureService(__instance);

                if (TryGetForcedSkinType(out var forcedSkinType))
                {
                    skinType = forcedSkinType;
                }
            }
        }

        [HarmonyPatch(typeof(CostumeChangeService), "TryChangeCostume")]
        private static class TryChangeCostumePatch
        {
            private static void Prefix(CostumeChangeService __instance, ref CostumeChangeService.CostumeSkinType skinType)
            {
                CaptureService(__instance);

                if (TryGetForcedSkinType(out var forcedSkinType))
                {
                    skinType = forcedSkinType;
                }
            }
        }

        [HarmonyPatch(typeof(CostumeChangeService), nameof(CostumeChangeService.LotterySkin))]
        private static class LotterySkinPatch
        {
            private static bool Prefix(CostumeChangeService __instance, ref CostumeChangeService.CostumeSkinType __result)
            {
                CaptureService(__instance);

                if (!TryGetForcedSkinType(out var skinType))
                {
                    return true;
                }

                __result = skinType;
                SaveTodaySkinType(skinType);
                return false;
            }
        }
    }
}
