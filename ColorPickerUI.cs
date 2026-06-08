using System.Collections.Generic;
using System.Linq;
using GameEvent;
using UnityEngine;
using UnityEngine.UI;

namespace OssieCustomColors
{
    public partial class ColorPickerUI : MonoBehaviour, InputReceiver
    {
        public static ColorPickerUI Instance { get; private set; }

        int _maxColors;
        bool _customMode;
        List<Color> _savedColors;
        int _selectedIdx = -1;
        PickableCustomizationButton[] _allColorBtns;
        int _rightmostBtnIdx;
        float _btnW, _btnH;
        bool _layoutReady;
        bool _open;
        bool _bookOpen;
        InventoryBook _inventoryBookCache;
        float _r, _g, _b;
        float _h, _s, _v;
        string _hexInput = "FFFFFF";
        bool _loggedColorSpace;

        // HSV picker textures
        Texture2D _svTex;
        Texture2D _hueTex;
        float _lastHueForSvTex = -1f;
        bool _svDragging, _hueDragging;

        // Runtime color mapping state. CalibrationTools.cs can populate this when explicitly built elsewhere.
        readonly Dictionary<string, Color> _calibratedBackendColors = new Dictionary<string, Color>();
        Color _lastDisplayColor = Color.white;
        Color _lastBackendColor = Color.white;
        string _lastDisplayHex = "";
        string _lastBackendHex = "";

        // styles
        bool _stylesReady;
        Font _font;
        GUIStyle _titleStyle, _labelStyle, _btnStyle, _actionBtnStyle, _fieldStyle;
        GUIStyle _toggleStyle, _plusStyle, _xStyle;
        Texture2D _texPanel, _texBtn, _texBtnHov, _texSwatch;
        Texture2D _texWhite, _texDark, _texAccent;

        // Unity UI runtime view. It replaces the custom-colors IMGUI controls while
        // keeping the existing inventory-page state and color dispatch logic.
        Canvas _uiCanvas;
        RectTransform _canvasRect;
        RectTransform _uiRoot;
        InventoryPage _uiPage;
        bool _uiBuilt;
        bool _ownsUiCanvas;
        Sprite _uiSprite;
        bool _uiUsesInventoryCamera;
        bool _loggedCanvasConfig;
        bool _syncingHexField;
        RectTransform _toggleRt;
        Image _toggleBg;
        Text _toggleText;
        Rect _toggleGuiRect;
        Image _plusBg;
        Text _plusText;
        RectTransform _plusRt;
        Rect _plusGuiRect;
        readonly List<CanvasSwatch> _swatchViews = new List<CanvasSwatch>();
        readonly List<Rect> _swatchGuiRects = new List<Rect>();
        readonly List<Rect> _deleteGuiRects = new List<Rect>();
        readonly List<Graphic> _pickerGraphics = new List<Graphic>();
        RectTransform _pickerRoot;
        Image _pickerPanelBg;
        Text _pickerTitle;
        RawImage _svRaw;
        RawImage _hueRaw;
        Image _svCursorOuter, _svCursorInner, _hueMarkerOuter, _hueMarkerInner;
        Text _hexLabel;
        InputField _hexField;
        Image _hexBg;
        Image _previewImage;
        Image _useBg, _saveBg, _cancelBg;
        Text _useText, _saveText, _cancelText;
        RectTransform _useRt, _saveRt, _cancelRt;
        Rect _panelGuiRect, _svGuiRect, _hueGuiRect, _hexGuiRect, _useGuiRect, _saveGuiRect, _cancelGuiRect;
        int _acceptPressedFrame = -999;
        int _acceptConsumedFrame = -999;
        int _acceptReleasedFrame = -999;
        bool _acceptHeld;
        Controller _acceptSender;

        static readonly Color ColPanel   = new Color(0.06f, 0.06f, 0.08f, 0.97f);
        static readonly Color ColTitle   = Color.white;
        static readonly Color ColBody    = new Color(0.78f, 0.80f, 0.84f, 1f);
        static readonly Color ColBtn     = new Color(1f, 0.82f, 0.30f, 1f);
        static readonly Color ColBtnHov  = new Color(1f, 0.90f, 0.50f, 1f);
        static readonly Color ColBtnText = new Color(0.10f, 0.09f, 0.06f, 1f);
        static readonly Color ColDark    = new Color(0.10f, 0.10f, 0.12f, 0.95f);
        static readonly Color ColAccent  = new Color(0.30f, 0.65f, 1.00f, 0.95f);

        // Implemented by CalibrationTools.cs when present; no-op in release builds.
        partial void LoadCalibrationData();
        partial void CheckCalibrationInput();
        partial void DrawCalibrationUI();

        void Awake()
        {
            Instance = this;
            Controller.AddGlobalReceiver(this);
            LoadColors();
            LoadCalibrationData();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Controller.RemoveGlobalReceiver(this);
            DestroyCanvasUi();
            if (_uiSprite != null) Destroy(_uiSprite);
            if (_svTex  != null) Destroy(_svTex);
            if (_hueTex != null) Destroy(_hueTex);
        }

        static bool IsFreeplay()
        {
            var gs = GameSettings.GetInstance();
            return gs != null && gs.GameMode == GameState.GameMode.FREEPLAY;
        }

        void Update()
        {
            bool freeplay = IsFreeplay();

            // Authoritative _bookOpen from scene poll — handles BOTH rising and falling edges.
            // Harmony hooks on TurnOffScreens are kept as fast-path triggers but unreliable:
            //   - OnBookHidden misses paths (PlayerInventoryEvent non-screen-mode, ForceClose,
            //     main menu over inventory). Poll catches the falling edge.
            //   - During Escape+Inventory spam, a deferred OnBookHidden can fire AFTER a fresh
            //     OnBookShown for the next open, falsing _bookOpen even though the book is
            //     visually open. Poll catches the rising edge.
            if (_inventoryBookCache == null)
                _inventoryBookCache = Object.FindObjectOfType<InventoryBook>();
            bool bookActiveNow = _inventoryBookCache != null
                                && _inventoryBookCache.gameObject.activeInHierarchy
                                && _inventoryBookCache.Visible;

            if (bookActiveNow && !_bookOpen)
            {
                _bookOpen = true;
                _open = false;
                _allColorBtns = null;
                _layoutReady = false;
                _btnW = _btnH = 0f;
            }
            else if (!bookActiveNow && _bookOpen)
            {
                _bookOpen = false;
                _open = false;
                _allColorBtns = null;
                _layoutReady = false;
                _btnW = _btnH = 0f;
                SyncNativesToMode();
            }

            if (!freeplay || !_bookOpen)
            {
                _open = false;
                SetCanvasVisible(false);
                SetPickerGraphicsVisible(false);
                if (!freeplay && _customMode)
                {
                    _customMode = false;
                    _open = false;
                    SyncNativesToMode();
                }
                return;
            }

            if (_open && Input.GetKeyDown(KeyCode.Escape))
                _open = false;

            RefreshButtons();

            bool colorsTabActive = _layoutReady && IsColorPageActive();
            if (!colorsTabActive)
                _open = false;

            SyncNativesToMode();
            UpdateCanvasUi(colorsTabActive);

            CheckCalibrationInput();
        }

        public void OnBookShown()
        {
            _bookOpen = true;
            _open = false;
            _inventoryBookCache = null;
            _allColorBtns = null;
            _layoutReady = false;
            _btnW = _btnH = 0f;
        }

        public void OnBookHidden()
        {
            _bookOpen = false;
            _open = false;
            _allColorBtns = null;
            _layoutReady = false;
            _btnW = _btnH = 0f;
            SetCanvasVisible(false);
            SyncNativesToMode();
        }

        public void Open(Color current)
        {
            _r = current.r; _g = current.g; _b = current.b;
            Color.RGBToHSV(current, out _h, out _s, out _v);
            _lastHueForSvTex = -1f;
            _hexInput = ToHex(_r, _g, _b);
            _open = true;
        }

        void OnGUI()
        {
            if (!IsFreeplay() || !_bookOpen) return;
            DrawCalibrationUI();
        }

        // ---- native button cache ----

        void RefreshButtons()
        {
            if (_layoutReady && _allColorBtns != null && _allColorBtns.Length > 0 && _allColorBtns[0] != null && IsCurrentColorPageButton(_allColorBtns[0]))
                return;

            _layoutReady = false;
            _allColorBtns = null;

            var found = Object.FindObjectsOfType<PickableCustomizationButton>(true)
                .Where(IsCurrentColorPageButton)
                .OrderByDescending(b => b.transform.position.y)
                .ThenBy(b => b.transform.position.x)
                .ToArray();

            if (found.Length == 0) return;
            Plugin.Log.LogInfo($"[OssieCustomColors] RefreshButtons: {found.Length} current-page BlockColors buttons found");

            _allColorBtns = found;

            var cam = InventoryLayoutCamera();
            if (cam == null)
            {
                _allColorBtns = null;
                return;
            }

            if (!TryUpdateButtonSize(cam, keepLargest: false))
            {
                _allColorBtns = null;
                _btnW = _btnH = 0f;
                return;
            }

            _rightmostBtnIdx = 0;
            float maxScreenX = float.MinValue;
            for (int i = 0; i < _allColorBtns.Length; i++)
            {
                float sx = cam.WorldToScreenPoint(_allColorBtns[i].transform.position).x;
                if (sx > maxScreenX) { maxScreenX = sx; _rightmostBtnIdx = i; }
            }

            _maxColors = Mathf.Max(1, _allColorBtns.Length - 1);
            _layoutReady = true;
            Plugin.Log.LogInfo($"[OssieCustomColors] Layout ready: {_allColorBtns.Length} buttons, max={_maxColors}, size={_btnW:F1}x{_btnH:F1}px, rightmost={_rightmostBtnIdx}");
        }

        bool IsCurrentColorPageButton(PickableCustomizationButton button)
        {
            if (button == null || button.customizationType != CustomizationType.BlockColors)
                return false;

            var book = button.InventoryBook ?? _inventoryBookCache;
            if (book == null || !book.Visible || book.ScreenMode || !book.inInventory)
                return false;
            if (_inventoryBookCache != null && book != _inventoryBookCache)
                return false;

            return book.currentPage == button.PageNumber;
        }

        bool TryUpdateButtonSize(Camera cam, bool keepLargest)
        {
            if (cam == null || _allColorBtns == null || _allColorBtns.Length == 0)
                return false;

            var sr = _allColorBtns
                .Select(b => b != null ? b.GetComponentInChildren<SpriteRenderer>(true) : null)
                .FirstOrDefault(r => r != null && r.enabled && r.gameObject.activeInHierarchy && r.bounds.size.sqrMagnitude > 0.0001f);
            if (sr == null) return false;

            if (!TryGetProjectedBounds(cam, sr.bounds, out float w, out float h))
                return false;
            if (w <= 5f || h <= 5f)
                return false;

            // Native color buttons are square; don't let page-turn animation cache a squashed replacement.
            float size = Mathf.Max(w, h);
            if (keepLargest)
                size = Mathf.Max(size, _btnW, _btnH);

            _btnW = _btnH = size;
            return true;
        }

        bool TryGetProjectedBounds(Camera cam, Bounds bounds, out float w, out float h)
        {
            w = h = 0f;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;

            for (int xi = 0; xi < 2; xi++)
            for (int yi = 0; yi < 2; yi++)
            for (int zi = 0; zi < 2; zi++)
            {
                Vector3 p = new Vector3(xi == 0 ? min.x : max.x, yi == 0 ? min.y : max.y, zi == 0 ? min.z : max.z);
                Vector3 s = cam.WorldToScreenPoint(p);
                if (s.z < 0f) continue;
                minX = Mathf.Min(minX, s.x);
                maxX = Mathf.Max(maxX, s.x);
                minY = Mathf.Min(minY, s.y);
                maxY = Mathf.Max(maxY, s.y);
            }

            if (float.IsInfinity(minX) || float.IsInfinity(maxX) || float.IsInfinity(minY) || float.IsInfinity(maxY)
                || float.IsNaN(minX) || float.IsNaN(maxX) || float.IsNaN(minY) || float.IsNaN(maxY))
                return false;

            w = maxX - minX;
            h = maxY - minY;
            return w > 0f && h > 0f;
        }

        // ---- show / hide native buttons ----

        bool IsColorPageActive()
        {
            if (_allColorBtns == null || _allColorBtns.Length == 0) return false;

            var first = _allColorBtns[0];
            if (first == null) return false;

            var book = first.InventoryBook ?? _inventoryBookCache;
            if (book == null || !book.Visible || book.ScreenMode) return false;
            if (!book.inInventory) return false;

            return book.currentPage == first.PageNumber;
        }

        void SyncNativesToMode()
        {
            bool hide = _customMode && _layoutReady;
            var all = Object.FindObjectsOfType<PickableCustomizationButton>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null) continue;
                if (b.customizationType != CustomizationType.BlockColors) continue;
                bool desired = !hide;
                if (b.gameObject.activeSelf != desired)
                    b.gameObject.SetActive(desired);
            }
        }

        void ShowNativeButtons()
        {
            if (_allColorBtns == null) return;
            foreach (var b in _allColorBtns)
                if (b != null) b.gameObject.SetActive(true);
        }

        void HideNativeButtons()
        {
            if (_allColorBtns == null) return;
            foreach (var b in _allColorBtns)
                if (b != null) b.gameObject.SetActive(false);
        }

        // ---- shared rect helpers ----

        Rect ToggleRect(Camera cam)
        {
            var p = cam.WorldToScreenPoint(_allColorBtns[_rightmostBtnIdx].transform.position);
            float cx = p.x + _btnW * 0.65f;
            float cy = Screen.height - p.y - _btnH;
            return new Rect(cx - _btnW * 0.5f, cy - _btnH * 0.5f, _btnW, _btnH);
        }

        Rect PlusRect(Camera cam)
        {
            var p = cam.WorldToScreenPoint(_allColorBtns[0].transform.position);
            return new Rect(p.x - _btnW * 0.5f, Screen.height - p.y - _btnH * 0.5f, _btnW, _btnH);
        }

        Rect SwatchRect(Camera cam, int index)
        {
            var p = cam.WorldToScreenPoint(_allColorBtns[index].transform.position);
            return new Rect(p.x - _btnW * 0.5f, Screen.height - p.y - _btnH * 0.5f, _btnW, _btnH);
        }

        // ---- toggle button ----

        void DrawToggleButton()
        {
            if (_allColorBtns == null || _allColorBtns.Length == 0) return;
            if (_allColorBtns[_rightmostBtnIdx] == null) return;

            var cam = Camera.main;
            if (cam == null) return;

            EnsureStyles();
            if (!_stylesReady) return;

            var rect = ToggleRect(cam);
            var e = Event.current;

            if (e.type == EventType.Repaint)
            {
                GUI.DrawTexture(rect, _customMode ? _texAccent : _texDark);
                GUI.Label(rect, _customMode ? "S" : "C", _toggleStyle);
            }

            bool clicked = (e.type == EventType.MouseDown && PointerIn(rect, e))
                        || ControllerClick(rect);
            if (clicked)
            {
                _customMode = !_customMode;
                if (_customMode) HideNativeButtons();
                else
                {
                    _open = false;
                    HideCustomPaletteVisuals();
                    ShowNativeButtons();
                }
                if (e.isMouse) e.Use();
            }

            if (e.isMouse && PointerIn(rect, e)) e.Use();
        }

        // ---- custom palette ----

        void DrawCustomPalette()
        {
            if (_savedColors == null) return;
            if (_allColorBtns == null || _allColorBtns.Length == 0) return;

            var cam = Camera.main;
            if (cam == null) return;

            EnsureStyles();
            if (!_stylesReady) return;

            var e = Event.current;
            bool isRepaint = e.type == EventType.Repaint;

            for (int i = 0; i < _savedColors.Count; i++)
            {
                var swRect = SwatchRect(cam, i + 1);
                bool hovered = PointerIn(swRect, e);
                var xRect = new Rect(swRect.xMax - 14f, swRect.yMin, 14f, 14f);
                bool xHovered = hovered && PointerIn(xRect, e);

                bool xClicked  = (e.type == EventType.MouseDown && xHovered) || ControllerClick(xRect);
                bool swClicked = !xClicked && ((e.type == EventType.MouseDown && hovered) || ControllerClick(swRect));

                if (xClicked)
                {
                    _savedColors.RemoveAt(i);
                    if (_selectedIdx == i) _selectedIdx = -1;
                    else if (_selectedIdx > i) _selectedIdx--;
                    SaveColors();
                    if (e.isMouse) e.Use();
                    return;
                }
                if (swClicked)
                {
                    SendColorChange(_savedColors[i]);
                    _selectedIdx = i;
                    if (e.isMouse) e.Use();
                }

                if (isRepaint)
                {
                    if (i == _selectedIdx)
                    {
                        var border = new Rect(swRect.x - 2f, swRect.y - 2f, swRect.width + 4f, swRect.height + 4f);
                        GUI.DrawTexture(border, _texWhite);
                    }

                    var prev = GUI.color;
                    GUI.color = _savedColors[i];
                    GUI.DrawTexture(swRect, _texWhite);
                    GUI.color = prev;

                    if (hovered)
                    {
                        GUI.DrawTexture(xRect, _texDark);
                        GUI.Label(xRect, "x", _xStyle);
                    }
                }

                if (e.isMouse && PointerIn(swRect, e)) e.Use();
            }

            if (_savedColors.Count < _maxColors)
            {
                var plusRect = PlusRect(cam);
                bool hovered = PointerIn(plusRect, e);

                if (isRepaint)
                {
                    GUI.DrawTexture(plusRect, _texDark);
                    GUI.Label(plusRect, "+", _plusStyle);
                }

                bool plusClicked = (e.type == EventType.MouseDown && hovered) || ControllerClick(plusRect);
                if (plusClicked)
                {
                    Open(_lastDisplayColor);
                    if (e.isMouse) e.Use();
                }

                if (e.isMouse && PointerIn(plusRect, e)) e.Use();
            }
        }

        // ---- picker panel ----

        void DrawPickerPanel()
        {
            EnsureStyles();
            if (!_stylesReady) return;

            const float pad     = 18f;
            const float svSz    = 260f;
            const float hueSz   = 18f;
            const float titleH  = 28f;
            const float hexH    = 28f;
            const float swatchH = 28f;
            const float btnsH   = 42f;
            const float pw      = 360f;
            float innerW = pw - pad * 2f;           // 324
            float svX    = (innerW - svSz) / 2f;   // 32px centering margin each side

            float ph = pad + titleH + 12f + svSz + 8f + hueSz + 10f + hexH + 8f + swatchH + 12f + btnsH + pad;

            float px = (Screen.width  - pw) * 0.5f;
            float py = (Screen.height - ph) * 0.5f;
            float ix = px + pad;
            float cy = py + pad;

            GUI.DrawTexture(new Rect(px, py, pw, ph), _texPanel);

            var e = Event.current;

            // Title
            GUI.Label(new Rect(ix, cy, innerW, titleH), "CUSTOM COLOR", _titleStyle);
            cy += titleH + 12f;

            // SV box
            RebuildSvTexIfNeeded();
            var svRect = new Rect(ix + svX, cy, svSz, svSz);
            GUI.DrawTexture(svRect, _svTex);

            if (e.type == EventType.Repaint)
            {
                float cx2 = svRect.x + _s * svRect.width;
                float cy2 = svRect.y + (1f - _v) * svRect.height;
                var prev = GUI.color;
                GUI.color = Color.black;
                GUI.DrawTexture(new Rect(cx2 - 6, cy2 - 6, 12, 12), _texWhite);
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(cx2 - 4, cy2 - 4,  8,  8), _texWhite);
                GUI.color = prev;
            }

            bool svPressed = (e.type == EventType.MouseDown && PointerIn(svRect, e)) || ControllerClick(svRect);
            if (svPressed) _svDragging = true;
            if (e.type == EventType.MouseUp) _svDragging = false;
            if (_svDragging && (e.type == EventType.MouseDrag || e.type == EventType.MouseDown))
            {
                var svp = PointerPos(e);
                _s = Mathf.Clamp01((svp.x - svRect.x) / svRect.width);
                _v = Mathf.Clamp01(1f - (svp.y - svRect.y) / svRect.height);
                UpdateRgbFromHsv();
                if (e.isMouse) e.Use();
            }
            cy += svSz + 8f;

            // Hue strip
            var hueRect = new Rect(ix + svX, cy, svSz, hueSz);
            GUI.DrawTexture(hueRect, EnsureHueTex());

            if (e.type == EventType.Repaint)
            {
                float hx = hueRect.x + _h * hueRect.width;
                var prev = GUI.color;
                GUI.color = Color.black;
                GUI.DrawTexture(new Rect(hx - 2, hueRect.y - 1, 4, hueRect.height + 2), _texWhite);
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(hx - 1, hueRect.y - 1, 2, hueRect.height + 2), _texWhite);
                GUI.color = prev;
            }

            bool huePressed = (e.type == EventType.MouseDown && PointerIn(hueRect, e)) || ControllerClick(hueRect);
            if (huePressed) _hueDragging = true;
            if (e.type == EventType.MouseUp) _hueDragging = false;
            if (_hueDragging && (e.type == EventType.MouseDrag || e.type == EventType.MouseDown))
            {
                var huep = PointerPos(e);
                _h = Mathf.Clamp01((huep.x - hueRect.x) / hueRect.width);
                _lastHueForSvTex = -1f;
                UpdateRgbFromHsv();
                if (e.isMouse) e.Use();
            }
            cy += hueSz + 10f;

            // Hex input
            GUI.Label(new Rect(ix, cy, 20f, hexH), "#", _labelStyle);
            string entered = GUI.TextField(new Rect(ix + 24f, cy, innerW - 24f, hexH), _hexInput, 6, _fieldStyle);
            if (entered != _hexInput)
            {
                _hexInput = entered;
                if (ColorUtility.TryParseHtmlString("#" + entered, out Color parsed))
                {
                    _r = parsed.r; _g = parsed.g; _b = parsed.b;
                    Color.RGBToHSV(parsed, out _h, out _s, out _v);
                    _lastHueForSvTex = -1f;
                }
            }
            cy += hexH + 8f;

            // Color swatch
            RefreshSwatch();
            GUI.DrawTexture(new Rect(ix, cy, innerW, swatchH), _texSwatch);
            cy += swatchH + 12f;

            // Buttons
            float totalBtnsW = 100f + 6f + 100f + 6f + 80f;
            float bx = ix + (innerW - totalBtnsW) / 2f;
            var useRect    = new Rect(bx,        cy, 100f, btnsH);
            var saveRect   = new Rect(bx + 106f, cy, 100f, btnsH);
            var cancelRect = new Rect(bx + 212f, cy,  80f, btnsH);
            bool useClicked    = GUI.Button(useRect,    "Use Color",  _actionBtnStyle) || ControllerClick(useRect);
            bool saveClicked   = GUI.Button(saveRect,   "Save Color", _actionBtnStyle) || ControllerClick(saveRect);
            bool cancelClicked = GUI.Button(cancelRect, "Cancel",     _actionBtnStyle) || ControllerClick(cancelRect);
            if (useClicked)    UseColor();
            if (saveClicked)   SaveColor();
            if (cancelClicked) _open = false;

            if (e.isMouse && e.type != EventType.Used)
                e.Use();
        }

        // ---- Canvas view ----

        void UpdateCanvasUi(bool colorsTabActive)
        {
            if (!colorsTabActive)
            {
                _open = false;
                SetPickerGraphicsVisible(false);
                SetCanvasVisible(false);
                return;
            }

            EnsureCanvasUi();
            if (!_uiBuilt) return;

            if (_uiCanvas != null && _uiCanvas.renderMode == RenderMode.ScreenSpaceCamera)
                ConfigureCanvasSortingBelowCursor(_uiCanvas);

            SetCanvasVisible(true);
            LayoutCanvasUi();
            SyncCanvasUi();
            HandleCanvasInput();
        }

        void EnsureCanvasUi()
        {
            InventoryPage page = GetColorPage();
            Camera inventoryCamera = _inventoryBookCache != null ? _inventoryBookCache.UiCamera : null;
            bool useInventoryCamera = inventoryCamera != null;
            Canvas canvas = _ownsUiCanvas ? _uiCanvas : null;

            if (_uiBuilt && _uiCanvas != null && _ownsUiCanvas && _uiPage == page && _uiUsesInventoryCamera == useInventoryCamera && (!useInventoryCamera || _uiCanvas.worldCamera == inventoryCamera))
                return;

            if (_uiBuilt || _uiRoot != null)
                DestroyCanvasUi();

            if (canvas == null)
            {
                var canvasGo = new GameObject("OssieCustomColors.Canvas");
                canvasGo.transform.SetParent(transform, false);
                canvasGo.layer = useInventoryCamera ? 5 : 0;
                canvas = canvasGo.AddComponent<Canvas>();
                _ownsUiCanvas = true;
            }

            _uiCanvas = canvas;
            _uiPage = page;
            _uiUsesInventoryCamera = useInventoryCamera;
            ConfigureCanvas(canvas, inventoryCamera);
            _canvasRect = canvas.GetComponent<RectTransform>();
            if (_canvasRect == null) return;

            if (canvas.GetComponent<GraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<GraphicRaycaster>();

            var rootGo = new GameObject("OssieCustomColors.CustomColorsCanvas");
            rootGo.transform.SetParent(canvas.transform, false);
            _uiRoot = rootGo.AddComponent<RectTransform>();
            _uiRoot.anchorMin = Vector2.zero;
            _uiRoot.anchorMax = Vector2.one;
            _uiRoot.offsetMin = Vector2.zero;
            _uiRoot.offsetMax = Vector2.zero;
            _uiRoot.pivot = new Vector2(0.5f, 0.5f);
            _uiRoot.SetAsLastSibling();

            BuildCanvasUi();
            if (useInventoryCamera)
                SetLayerRecursive(_uiRoot.gameObject, 5);
            _uiBuilt = true;
        }

        void ConfigureCanvas(Canvas canvas, Camera inventoryCamera)
        {
            if (inventoryCamera != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = inventoryCamera;
                canvas.planeDistance = Mathf.Clamp(250f, inventoryCamera.nearClipPlane + 0.01f, inventoryCamera.farClipPlane - 0.01f);
                canvas.overrideSorting = true;
                canvas.gameObject.layer = 5;
                ConfigureCanvasSortingBelowCursor(canvas);
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                canvas.overrideSorting = true;
                canvas.gameObject.layer = 0;
                canvas.sortingOrder = 32760;
            }

            if (!_loggedCanvasConfig)
            {
                _loggedCanvasConfig = true;
                string camName = inventoryCamera != null ? inventoryCamera.name : "<overlay>";
                Plugin.Log.LogInfo($"[OssieCustomColors] Canvas config: mode={canvas.renderMode}, camera={camName}, plane={canvas.planeDistance:F2}, layer={canvas.sortingLayerName}, order={canvas.sortingOrder}");
            }
        }

        void ConfigureCanvasSortingBelowCursor(Canvas canvas)
        {
            SpriteRenderer cursorRenderer;
            if (TryGetInventoryCursorRenderer(out cursorRenderer))
            {
                canvas.sortingLayerID = cursorRenderer.sortingLayerID;
                canvas.sortingOrder = Mathf.Max(short.MinValue, cursorRenderer.sortingOrder - 100);
                return;
            }

            if (_uiPage != null && _uiPage.textCanvas != null)
            {
                canvas.sortingLayerID = _uiPage.textCanvas.sortingLayerID;
                canvas.sortingOrder = _uiPage.textCanvas.sortingOrder + 1000;
                return;
            }

            canvas.sortingLayerName = "UI 1";
            canvas.sortingOrder = 29000;
        }

        void SetLayerRecursive(GameObject go, int layer)
        {
            if (go == null) return;
            var transforms = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
                transforms[i].gameObject.layer = layer;
        }

        bool TryGetInventoryCursorRenderer(out SpriteRenderer cursorRenderer)
        {
            cursorRenderer = null;
            if (_inventoryBookCache == null || _inventoryBookCache.cursors == null) return false;

            for (int i = 0; i < _inventoryBookCache.cursors.Count; i++)
            {
                var cursor = _inventoryBookCache.cursors[i];
                if (cursor == null) continue;
                var sr = cursor.GetComponentInChildren<SpriteRenderer>(true);
                if (sr == null) continue;
                cursorRenderer = sr;
                return true;
            }

            return false;
        }

        InventoryPage GetColorPage()
        {
            if (_allColorBtns == null || _allColorBtns.Length == 0 || _allColorBtns[0] == null)
                return null;

            var book = _allColorBtns[0].InventoryBook ?? _inventoryBookCache;
            if (book == null || book.InventoryPages == null) return null;
            int pageNum = _allColorBtns[0].PageNumber;
            if (pageNum < 0 || pageNum >= book.InventoryPages.Length) return null;
            return book.InventoryPages[pageNum];
        }

        void BuildCanvasUi()
        {
            EnsureCanvasResources();

            _toggleBg = NewImage("ToggleBackground", ColDark);
            _toggleRt = _toggleBg.rectTransform;
            _toggleText = NewText("ToggleText", "C", 16, TextAnchor.MiddleCenter, Color.white, _toggleRt);

            _plusBg = NewImage("PlusBackground", ColDark);
            _plusRt = _plusBg.rectTransform;
            _plusText = NewText("PlusText", "+", 24, TextAnchor.MiddleCenter, Color.white, _plusRt);

            var pickerGo = new GameObject("PickerRoot");
            pickerGo.transform.SetParent(_uiRoot, false);
            _pickerRoot = pickerGo.AddComponent<RectTransform>();
            _pickerRoot.anchorMin = Vector2.zero;
            _pickerRoot.anchorMax = Vector2.one;
            _pickerRoot.offsetMin = Vector2.zero;
            _pickerRoot.offsetMax = Vector2.zero;
            _pickerRoot.gameObject.SetActive(false);

            _pickerPanelBg = NewImage("PickerPanel", ColPanel, _pickerRoot);
            _pickerGraphics.Add(_pickerPanelBg);
            _pickerTitle = NewText("PickerTitle", "CUSTOM COLOR", 20, TextAnchor.MiddleCenter, ColTitle, _pickerRoot);
            _pickerGraphics.Add(_pickerTitle);
            _svRaw = NewRawImage("SvTexture", _pickerRoot);
            _pickerGraphics.Add(_svRaw);
            _svCursorOuter = NewImage("SvCursorOuter", Color.black, _pickerRoot);
            _pickerGraphics.Add(_svCursorOuter);
            _svCursorInner = NewImage("SvCursorInner", Color.white, _pickerRoot);
            _pickerGraphics.Add(_svCursorInner);
            _hueRaw = NewRawImage("HueTexture", _pickerRoot);
            _pickerGraphics.Add(_hueRaw);
            _hueMarkerOuter = NewImage("HueMarkerOuter", Color.black, _pickerRoot);
            _pickerGraphics.Add(_hueMarkerOuter);
            _hueMarkerInner = NewImage("HueMarkerInner", Color.white, _pickerRoot);
            _pickerGraphics.Add(_hueMarkerInner);
            _hexLabel = NewText("HexLabel", "#", 18, TextAnchor.MiddleLeft, ColBody, _pickerRoot);
            _pickerGraphics.Add(_hexLabel);

            _hexBg = NewImage("HexBackground", ColDark, _pickerRoot);
            _hexBg.raycastTarget = true;
            _pickerGraphics.Add(_hexBg);
            _hexField = _hexBg.gameObject.AddComponent<InputField>();
            _hexField.characterLimit = 6;
            _hexField.lineType = InputField.LineType.SingleLine;
            _hexField.onValueChanged.AddListener(OnHexFieldChanged);
            var hexText = NewText("HexInputText", _hexInput, 18, TextAnchor.MiddleLeft, ColBody, _hexBg.rectTransform);
            hexText.rectTransform.anchorMin = Vector2.zero;
            hexText.rectTransform.anchorMax = Vector2.one;
            hexText.rectTransform.offsetMin = new Vector2(8f, 2f);
            hexText.rectTransform.offsetMax = new Vector2(-8f, -2f);
            _hexField.textComponent = hexText;
            _hexField.text = _hexInput;

            _previewImage = NewImage("PreviewSwatch", Color.white, _pickerRoot);
            _pickerGraphics.Add(_previewImage);
            _useBg = NewImage("UseButton", ColBtn, _pickerRoot);
            _useRt = _useBg.rectTransform;
            _pickerGraphics.Add(_useBg);
            _useText = NewText("UseText", "Use Color", 14, TextAnchor.MiddleCenter, ColBtnText, _useRt);
            _pickerGraphics.Add(_useText);
            _saveBg = NewImage("SaveButton", ColBtn, _pickerRoot);
            _saveRt = _saveBg.rectTransform;
            _pickerGraphics.Add(_saveBg);
            _saveText = NewText("SaveText", "Save Color", 14, TextAnchor.MiddleCenter, ColBtnText, _saveRt);
            _pickerGraphics.Add(_saveText);
            _cancelBg = NewImage("CancelButton", ColBtn, _pickerRoot);
            _cancelRt = _cancelBg.rectTransform;
            _pickerGraphics.Add(_cancelBg);
            _cancelText = NewText("CancelText", "Cancel", 14, TextAnchor.MiddleCenter, ColBtnText, _cancelRt);
            _pickerGraphics.Add(_cancelText);

            SetPickerGraphicsVisible(false);

            Plugin.Log.LogInfo("[OssieCustomColors] Canvas UI built.");
        }

        void SetPickerGraphicsVisible(bool visible)
        {
            if (_pickerRoot != null && _pickerRoot.gameObject.activeSelf != visible)
                _pickerRoot.gameObject.SetActive(visible);
            for (int i = 0; i < _pickerGraphics.Count; i++)
                if (_pickerGraphics[i] != null) _pickerGraphics[i].gameObject.SetActive(visible);
        }

        void EnsureCanvasResources()
        {
            if (_font == null)
                _font = ResolveFont();
            EnsureUiSprite();
        }

        Image NewImage(string name, Color color, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : _uiRoot, false);
            var image = go.AddComponent<Image>();
            image.sprite = EnsureUiSprite();
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.color = color;
            image.raycastTarget = false;
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            return image;
        }

        Sprite EnsureUiSprite()
        {
            if (_uiSprite != null) return _uiSprite;
            _uiSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 100f);
            _uiSprite.hideFlags = HideFlags.HideAndDontSave;
            return _uiSprite;
        }

        RawImage NewRawImage(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : _uiRoot, false);
            var image = go.AddComponent<RawImage>();
            image.color = Color.white;
            image.raycastTarget = false;
            image.texture = null;
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            return image;
        }

        Text NewText(string name, string text, int fontSize, TextAnchor alignment, Color color, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : _uiRoot, false);
            var label = go.AddComponent<Text>();
            label.font = _font != null ? _font : ResolveFont();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            if (parent != null)
            {
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = Vector2.zero;
                label.rectTransform.offsetMax = Vector2.zero;
            }
            else
            {
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            }
            return label;
        }

        void DestroyCanvasUi()
        {
            _pickerGraphics.Clear();
            _swatchViews.Clear();
            _swatchGuiRects.Clear();
            _deleteGuiRects.Clear();

            if (_uiRoot != null)
                Destroy(_uiRoot.gameObject);
            if (_ownsUiCanvas && _uiCanvas != null)
                Destroy(_uiCanvas.gameObject);

            _uiCanvas = null;
            _canvasRect = null;
            _uiRoot = null;
            _uiPage = null;
            _pickerRoot = null;
            _uiBuilt = false;
            _ownsUiCanvas = false;
            _uiUsesInventoryCamera = false;
            _loggedCanvasConfig = false;
        }

        void SetCanvasVisible(bool visible)
        {
            if (_uiRoot != null && _uiRoot.gameObject.activeSelf != visible)
                _uiRoot.gameObject.SetActive(visible);
        }

        void HideCustomPaletteVisuals()
        {
            if (_plusRt != null)
                _plusRt.gameObject.SetActive(false);
            for (int i = 0; i < _swatchViews.Count; i++)
            {
                _swatchViews[i].Root.gameObject.SetActive(false);
                _swatchViews[i].Border.gameObject.SetActive(false);
                _swatchViews[i].DeleteRoot.gameObject.SetActive(false);
            }
        }

        void LayoutCanvasUi()
        {
            var cam = InventoryLayoutCamera();
            if (_allColorBtns == null || _allColorBtns.Length == 0 || cam == null)
            {
                _toggleRt.gameObject.SetActive(false);
                _plusRt.gameObject.SetActive(false);
                for (int i = 0; i < _swatchViews.Count; i++)
                {
                    _swatchViews[i].Root.gameObject.SetActive(false);
                    _swatchViews[i].Border.gameObject.SetActive(false);
                    _swatchViews[i].DeleteRoot.gameObject.SetActive(false);
                }
                _swatchGuiRects.Clear();
                _deleteGuiRects.Clear();
                SetPickerGraphicsVisible(false);
                return;
            }

            TryUpdateButtonSize(cam, keepLargest: true);

            _toggleRt.gameObject.SetActive(true);
            _toggleGuiRect = ToggleRect(cam);
            if (!_loggedCanvasConfig)
                Plugin.Log.LogInfo($"[OssieCustomColors] Toggle rect: {_toggleGuiRect}");
            SetRectFromGui(_toggleRt, _toggleGuiRect);

            EnsureSwatchViews();
            _swatchGuiRects.Clear();
            _deleteGuiRects.Clear();

            int shown = Mathf.Min(_savedColors != null ? _savedColors.Count : 0, _maxColors);
            for (int i = 0; i < _swatchViews.Count; i++)
            {
                bool active = _customMode && i < shown;
                _swatchViews[i].Root.gameObject.SetActive(active);
                _swatchViews[i].Border.gameObject.SetActive(false);
                _swatchViews[i].DeleteRoot.gameObject.SetActive(active);
                if (!active) continue;

                var swRect = SwatchRect(cam, i + 1);
                var xRect = new Rect(swRect.xMax - 14f, swRect.yMin, 14f, 14f);
                _swatchGuiRects.Add(swRect);
                _deleteGuiRects.Add(xRect);
                SetRectFromGui(_swatchViews[i].Root, swRect);
                SetRectFromGui(_swatchViews[i].Border.rectTransform, new Rect(swRect.x - 2f, swRect.y - 2f, swRect.width + 4f, swRect.height + 4f));
                SetRectFromGui(_swatchViews[i].DeleteRoot, xRect);
            }

            bool plusActive = _customMode && _savedColors != null && _savedColors.Count < _maxColors;
            _plusRt.gameObject.SetActive(plusActive);
            if (plusActive)
            {
                _plusGuiRect = PlusRect(cam);
                SetRectFromGui(_plusRt, _plusGuiRect);
            }

            if (_open)
                LayoutPickerCanvas();
        }

        void LayoutPickerCanvas()
        {
            const float pad     = 18f;
            const float svSz    = 260f;
            const float hueSz   = 18f;
            const float titleH  = 28f;
            const float hexH    = 28f;
            const float swatchH = 28f;
            const float btnsH   = 42f;
            const float pw      = 360f;
            float innerW = pw - pad * 2f;
            float svX    = (innerW - svSz) / 2f;
            float ph = pad + titleH + 12f + svSz + 8f + hueSz + 10f + hexH + 8f + swatchH + 12f + btnsH + pad;
            float px = (Screen.width  - pw) * 0.5f;
            float py = (Screen.height - ph) * 0.5f;
            float ix = px + pad;
            float cy = py + pad;

            _panelGuiRect = new Rect(px, py, pw, ph);
            SetRectFromGui(_pickerPanelBg.rectTransform, _panelGuiRect);
            SetRectFromGui(_pickerTitle.rectTransform, new Rect(ix, cy, innerW, titleH));
            cy += titleH + 12f;

            _svGuiRect = new Rect(ix + svX, cy, svSz, svSz);
            SetRectFromGui(_svRaw.rectTransform, _svGuiRect);
            float cx2 = _svGuiRect.x + _s * _svGuiRect.width;
            float cy2 = _svGuiRect.y + (1f - _v) * _svGuiRect.height;
            SetRectFromGui(_svCursorOuter.rectTransform, new Rect(cx2 - 6f, cy2 - 6f, 12f, 12f));
            SetRectFromGui(_svCursorInner.rectTransform, new Rect(cx2 - 4f, cy2 - 4f, 8f, 8f));
            cy += svSz + 8f;

            _hueGuiRect = new Rect(ix + svX, cy, svSz, hueSz);
            SetRectFromGui(_hueRaw.rectTransform, _hueGuiRect);
            float hx = _hueGuiRect.x + _h * _hueGuiRect.width;
            SetRectFromGui(_hueMarkerOuter.rectTransform, new Rect(hx - 2f, _hueGuiRect.y - 1f, 4f, _hueGuiRect.height + 2f));
            SetRectFromGui(_hueMarkerInner.rectTransform, new Rect(hx - 1f, _hueGuiRect.y - 1f, 2f, _hueGuiRect.height + 2f));
            cy += hueSz + 10f;

            SetRectFromGui(_hexLabel.rectTransform, new Rect(ix, cy, 20f, hexH));
            _hexGuiRect = new Rect(ix + 24f, cy, innerW - 24f, hexH);
            SetRectFromGui(_hexBg.rectTransform, _hexGuiRect);
            cy += hexH + 8f;

            SetRectFromGui(_previewImage.rectTransform, new Rect(ix, cy, innerW, swatchH));
            cy += swatchH + 12f;

            float totalBtnsW = 100f + 6f + 100f + 6f + 80f;
            float bx = ix + (innerW - totalBtnsW) / 2f;
            _useGuiRect = new Rect(bx, cy, 100f, btnsH);
            _saveGuiRect = new Rect(bx + 106f, cy, 100f, btnsH);
            _cancelGuiRect = new Rect(bx + 212f, cy, 80f, btnsH);
            SetRectFromGui(_useRt, _useGuiRect);
            SetRectFromGui(_saveRt, _saveGuiRect);
            SetRectFromGui(_cancelRt, _cancelGuiRect);
        }

        void SyncCanvasUi()
        {
            _toggleBg.color = _customMode ? ColAccent : ColDark;
            _toggleText.text = _customMode ? "S" : "C";

            if (!_customMode)
            {
                HideCustomPaletteVisuals();
                SetPickerGraphicsVisible(false);
                return;
            }

            Vector2 pointer;
            bool havePointer = TryGetCurrentGuiPointer(out pointer);
            _plusBg.color = havePointer && _plusGuiRect.Contains(pointer) ? ColAccent : ColDark;

            int shown = Mathf.Min(_savedColors != null ? _savedColors.Count : 0, _swatchViews.Count);
            for (int i = 0; i < shown; i++)
            {
                var view = _swatchViews[i];
                view.Fill.color = _savedColors[i];
                view.Border.gameObject.SetActive(i == _selectedIdx);
                bool hover = havePointer && i < _swatchGuiRects.Count && _swatchGuiRects[i].Contains(pointer);
                view.DeleteRoot.gameObject.SetActive(_customMode && hover);
            }

            bool pickerVisible = _open;
            if (!pickerVisible)
            {
                SetPickerGraphicsVisible(false);
                return;
            }

            RebuildSvTexIfNeeded();
            _svRaw.texture = _svTex;
            _hueRaw.texture = EnsureHueTex();
            _previewImage.color = new Color(_r, _g, _b, 1f);
            SetPickerGraphicsVisible(true);

            if (_hexField != null && !_hexField.isFocused && _hexField.text != _hexInput)
            {
                _syncingHexField = true;
                _hexField.text = _hexInput;
                _syncingHexField = false;
            }

            SetActionVisual(_useBg, _useText, _useGuiRect, havePointer, pointer);
            SetActionVisual(_saveBg, _saveText, _saveGuiRect, havePointer, pointer);
            SetActionVisual(_cancelBg, _cancelText, _cancelGuiRect, havePointer, pointer);
        }

        void SetActionVisual(Image bg, Text text, Rect rect, bool havePointer, Vector2 pointer)
        {
            bg.color = havePointer && rect.Contains(pointer) ? ColBtnHov : ColBtn;
            text.color = ColBtnText;
        }

        void EnsureSwatchViews()
        {
            int target = Mathf.Max(0, _maxColors);
            while (_swatchViews.Count < target)
            {
                var root = NewImage("SavedColor", Color.white);
                var border = NewImage("SavedColorBorder", Color.white);
                var delete = NewImage("SavedColorDelete", ColDark);
                var x = NewText("SavedColorDeleteText", "x", 11, TextAnchor.MiddleCenter, Color.white, delete.rectTransform);
                border.transform.SetSiblingIndex(root.transform.GetSiblingIndex());
                delete.transform.SetAsLastSibling();
                root.gameObject.SetActive(false);
                border.gameObject.SetActive(false);
                delete.gameObject.SetActive(false);
                _swatchViews.Add(new CanvasSwatch
                {
                    Root = root.rectTransform,
                    Fill = root,
                    Border = border,
                    DeleteRoot = delete.rectTransform,
                    DeleteBg = delete,
                    DeleteText = x
                });
            }
        }

        void SetRectFromGui(RectTransform rt, Rect guiRect)
        {
            if (rt == null || _canvasRect == null) return;
            Camera cam = CanvasCamera();
            Vector2 centerScreen = new Vector2(guiRect.x + guiRect.width * 0.5f, Screen.height - (guiRect.y + guiRect.height * 0.5f));
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, centerScreen, cam, out local))
                return;

            Vector2 left, right, top, bottom;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, centerScreen + Vector2.left * guiRect.width * 0.5f, cam, out left);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, centerScreen + Vector2.right * guiRect.width * 0.5f, cam, out right);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, centerScreen + Vector2.up * guiRect.height * 0.5f, cam, out top);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, centerScreen + Vector2.down * guiRect.height * 0.5f, cam, out bottom);

            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = local;
            rt.sizeDelta = new Vector2(Mathf.Abs(right.x - left.x), Mathf.Abs(top.y - bottom.y));
        }

        Camera CanvasCamera()
        {
            if (_uiCanvas == null || _uiCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
                return null;
            if (_uiCanvas.worldCamera != null) return _uiCanvas.worldCamera;
            if (_inventoryBookCache != null && _inventoryBookCache.UiCamera != null) return _inventoryBookCache.UiCamera;
            return Camera.main;
        }

        Camera InventoryLayoutCamera()
        {
            if (_inventoryBookCache != null && _inventoryBookCache.UiCamera != null)
                return _inventoryBookCache.UiCamera;
            return Camera.main;
        }

        void HandleCanvasInput()
        {
            if (Input.GetMouseButtonDown(0))
                HandlePointerPress(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y), isMouse: true);

            if (Input.GetMouseButton(0) && (_svDragging || _hueDragging))
                UpdatePickerDrag(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));

            if (Input.GetMouseButtonUp(0))
            {
                _svDragging = false;
                _hueDragging = false;
            }

            Vector2 controllerPointer;
            bool controllerPressed = _acceptPressedFrame >= Time.frameCount - 1 && _acceptConsumedFrame != _acceptPressedFrame;
            if (controllerPressed && TryGetControllerGuiPointer(out controllerPointer))
            {
                if (HandlePointerPress(controllerPointer, isMouse: false))
                    _acceptConsumedFrame = _acceptPressedFrame;
            }

            if (_acceptHeld && (_svDragging || _hueDragging) && TryGetControllerGuiPointer(out controllerPointer))
                UpdatePickerDrag(controllerPointer);

            if (_acceptReleasedFrame >= Time.frameCount - 1)
            {
                _svDragging = false;
                _hueDragging = false;
            }
        }

        bool HandlePointerPress(Vector2 guiPos, bool isMouse)
        {
            if (_open)
            {
                if (_svGuiRect.Contains(guiPos))
                {
                    _svDragging = true;
                    UpdatePickerDrag(guiPos);
                    return true;
                }
                if (_hueGuiRect.Contains(guiPos))
                {
                    _hueDragging = true;
                    UpdatePickerDrag(guiPos);
                    return true;
                }
                if (_hexGuiRect.Contains(guiPos))
                {
                    if (_hexField != null) _hexField.ActivateInputField();
                    return true;
                }
                if (_useGuiRect.Contains(guiPos)) { UseColor(); return true; }
                if (_saveGuiRect.Contains(guiPos)) { SaveColor(); return true; }
                if (_cancelGuiRect.Contains(guiPos)) { _open = false; return true; }
                if (_panelGuiRect.Contains(guiPos)) return true;
            }

            if (_toggleGuiRect.Contains(guiPos))
            {
                _customMode = !_customMode;
                if (_customMode) HideNativeButtons();
                else
                {
                    _open = false;
                    HideCustomPaletteVisuals();
                    ShowNativeButtons();
                }
                return true;
            }

            if (!_customMode) return false;

            for (int i = 0; i < _deleteGuiRects.Count; i++)
            {
                if (!_deleteGuiRects[i].Contains(guiPos)) continue;
                _savedColors.RemoveAt(i);
                if (_selectedIdx == i) _selectedIdx = -1;
                else if (_selectedIdx > i) _selectedIdx--;
                SaveColors();
                return true;
            }

            for (int i = 0; i < _swatchGuiRects.Count; i++)
            {
                if (!_swatchGuiRects[i].Contains(guiPos)) continue;
                SendColorChange(_savedColors[i]);
                _selectedIdx = i;
                return true;
            }

            if (_plusRt != null && _plusRt.gameObject.activeSelf && _plusGuiRect.Contains(guiPos))
            {
                Open(_lastDisplayColor);
                return true;
            }

            return false;
        }

        void UpdatePickerDrag(Vector2 guiPos)
        {
            if (_svDragging)
            {
                _s = Mathf.Clamp01((guiPos.x - _svGuiRect.x) / _svGuiRect.width);
                _v = Mathf.Clamp01(1f - (guiPos.y - _svGuiRect.y) / _svGuiRect.height);
                UpdateRgbFromHsv();
            }
            else if (_hueDragging)
            {
                _h = Mathf.Clamp01((guiPos.x - _hueGuiRect.x) / _hueGuiRect.width);
                _lastHueForSvTex = -1f;
                UpdateRgbFromHsv();
            }
        }

        void OnHexFieldChanged(string entered)
        {
            if (_syncingHexField) return;
            _hexInput = NormalizeHex(entered);
            if (_hexField != null && _hexField.text != _hexInput)
            {
                _syncingHexField = true;
                _hexField.text = _hexInput;
                _syncingHexField = false;
                _hexField.MoveTextEnd(false);
            }
            if (ColorUtility.TryParseHtmlString("#" + _hexInput, out Color parsed))
            {
                _r = parsed.r; _g = parsed.g; _b = parsed.b;
                Color.RGBToHSV(parsed, out _h, out _s, out _v);
                _lastHueForSvTex = -1f;
            }
        }

        bool TryGetCurrentGuiPointer(out Vector2 guiPos)
        {
            if (TryGetControllerGuiPointer(out guiPos)) return true;
            guiPos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return true;
        }

        bool TryGetControllerGuiPointer(out Vector2 guiPos)
        {
            guiPos = Vector2.zero;
            if (_inventoryBookCache == null || _inventoryBookCache.cursors == null) return false;

            PickCursor best = null;
            for (int i = 0; i < _inventoryBookCache.cursors.Count; i++)
            {
                var cursor = _inventoryBookCache.cursors[i];
                if (cursor == null || !cursor.gameObject.activeInHierarchy) continue;
                if (_acceptSender != null && cursor.LocalPlayer != null && cursor.LocalPlayer.UseController == _acceptSender)
                {
                    best = cursor;
                    break;
                }
                if (best == null) best = cursor;
            }

            if (best == null) return false;
            var cam = best.UseCamera != null ? best.UseCamera : (_inventoryBookCache.UiCamera != null ? _inventoryBookCache.UiCamera : Camera.main);
            if (cam == null) return false;
            Vector3 world = best.cursorPoint != null ? best.cursorPoint.position : best.transform.position;
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f) return false;
            guiPos = new Vector2(screen.x, Screen.height - screen.y);
            return true;
        }

        public void ReceiveEvent(InputEvent e)
        {
            if (e == null || e.Key != InputEvent.InputKey.Accept) return;
            if (e.Sender == null) return;
            if (e.Sender.IsKeyboard && e.Sender.IsUsingPosition()) return;

            _acceptSender = e.Sender;
            _acceptHeld = e.Valueb;
            if (!e.Changed) return;

            if (e.Valueb) _acceptPressedFrame = Time.frameCount;
            else _acceptReleasedFrame = Time.frameCount;
        }

        // ---- HSV helpers ----

        void UpdateRgbFromHsv()
        {
            var c = Color.HSVToRGB(_h, _s, _v);
            _r = c.r; _g = c.g; _b = c.b;
            _hexInput = ToHex(_r, _g, _b);
        }

        void RebuildSvTexIfNeeded()
        {
            if (_svTex != null && Mathf.Abs(_lastHueForSvTex - _h) < 0.002f) return;
            _lastHueForSvTex = _h;
            if (_svTex == null)
            {
                _svTex = new Texture2D(64, 64, TextureFormat.RGB24, false);
                _svTex.hideFlags  = HideFlags.HideAndDontSave;
                _svTex.filterMode = FilterMode.Bilinear;
                _svTex.wrapMode   = TextureWrapMode.Clamp;
            }
            var px = new Color[64 * 64];
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                px[y * 64 + x] = Color.HSVToRGB(_h, x / 63f, y / 63f);
            _svTex.SetPixels(px);
            _svTex.Apply();
        }

        Texture2D EnsureHueTex()
        {
            if (_hueTex != null) return _hueTex;
            _hueTex = new Texture2D(256, 16, TextureFormat.RGB24, false);
            _hueTex.hideFlags  = HideFlags.HideAndDontSave;
            _hueTex.filterMode = FilterMode.Bilinear;
            _hueTex.wrapMode   = TextureWrapMode.Clamp;
            var px = new Color[256 * 16];
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 256; x++)
                px[y * 256 + x] = Color.HSVToRGB(x / 255f, 1f, 1f);
            _hueTex.SetPixels(px);
            _hueTex.Apply();
            return _hueTex;
        }

        void UseColor()
        {
            SendColorChange(new Color(_r, _g, _b, 1f));
            _open = false;
        }

        void SaveColor()
        {
            var color = new Color(_r, _g, _b, 1f);
            _savedColors.Add(color);
            _selectedIdx = _savedColors.Count - 1;
            SaveColors();
            SendColorChange(color);
            _open = false;
            Plugin.Log.LogInfo($"[OssieCustomColors] Saved #{ToHex(_r, _g, _b)}, count={_savedColors.Count}");
        }

        void SendColorChange(Color displayColor)
        {
            _lastDisplayColor = displayColor;
            _lastDisplayHex = ToHex(displayColor.r, displayColor.g, displayColor.b);

            bool calibrated = TryGetCalibratedBackend(displayColor, out var gameColor);
            if (!calibrated)
                gameColor = ToGameColor(displayColor);

            _lastBackendColor = gameColor;
            _lastBackendHex = ToHex(gameColor.r, gameColor.g, gameColor.b);

            if (!_loggedColorSpace)
            {
                _loggedColorSpace = true;
                Plugin.Log.LogInfo($"[OssieCustomColors] Color space: {QualitySettings.activeColorSpace}");
            }
            Plugin.Log.LogInfo(calibrated
                ? $"[OssieCustomColors] Sending display #{_lastDisplayHex} as calibrated backend #{_lastBackendHex}"
                : $"[OssieCustomColors] Sending display #{_lastDisplayHex} as backend #{_lastBackendHex} using exponent {Plugin.ColorCorrectionExponent.Value:F2}, brightness {Mathf.Clamp01(Plugin.ColorCorrectionBrightness.Value):F2}");
            GameEventManager.SendEvent(new SetpieceColorChangeEvent(gameColor));
        }

        // ---- persistence ----

        void LoadColors()
        {
            _savedColors = new List<Color>();
            foreach (var hex in Plugin.SavedColors.Value.Split(','))
            {
                var h = hex.Trim();
                if (h.Length > 0 && ColorUtility.TryParseHtmlString("#" + h, out Color c))
                    _savedColors.Add(c);
            }
        }

        void SaveColors()
        {
            Plugin.SavedColors.Value = string.Join(",",
                _savedColors.ConvertAll(c => ToHex(c.r, c.g, c.b)));
        }

        void RefreshSwatch()
        {
            if (_texSwatch == null)
                _texSwatch = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            _texSwatch.SetPixel(0, 0, new Color(_r, _g, _b, 1f));
            _texSwatch.Apply();
        }

        // ---- LUT lookup ----

        bool TryGetCalibratedBackend(Color displayColor, out Color backendColor)
        {
            string displayHex = ToHex(displayColor.r, displayColor.g, displayColor.b);

            // Runtime calibration (CalibrationTools populated this at dev time) takes priority.
            if (_calibratedBackendColors.TryGetValue(displayHex, out backendColor)) return true;
            // Baked LUT compiled into the DLL.
            if (LutData.Entries.TryGetValue(displayHex, out backendColor)) return true;

            int totalEntries = _calibratedBackendColors.Count + LutData.Entries.Count;
            if (totalEntries < 4)
            {
                backendColor = Color.black;
                return false;
            }

            // Nearest-neighbour interpolation across both sources (runtime overrides baked for same key).
            var nearest = LutData.Entries
                .Concat(_calibratedBackendColors)
                .Select(pair => new LutNeighbor(pair.Key, pair.Value, ColorDistanceSq(displayColor, HexToColor(pair.Key))))
                .OrderBy(n => n.DistanceSq)
                .Take(8)
                .ToArray();

            if (nearest.Length == 0)
            {
                backendColor = Color.black;
                return false;
            }

            if (nearest[0].DistanceSq <= 0.000001f)
            {
                backendColor = nearest[0].Backend;
                return true;
            }

            float totalWeight = 0f;
            var sum = Color.black;
            for (int i = 0; i < nearest.Length; i++)
            {
                float weight = 1f / Mathf.Max(0.000001f, nearest[i].DistanceSq);
                sum += nearest[i].Backend * weight;
                totalWeight += weight;
            }

            backendColor = new Color(sum.r / totalWeight, sum.g / totalWeight, sum.b / totalWeight, 1f);
            return true;
        }

        // ---- utilities ----

        static string ToHex(float r, float g, float b) =>
            $"{Mathf.RoundToInt(Mathf.Clamp01(r) * 255f):X2}{Mathf.RoundToInt(Mathf.Clamp01(g) * 255f):X2}{Mathf.RoundToInt(Mathf.Clamp01(b) * 255f):X2}";

        static string NormalizeHex(string hex) =>
            new string(hex.Trim().TrimStart('#').Where(UriStyleHexChar).ToArray()).ToUpperInvariant();

        static bool UriStyleHexChar(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        static string SafeHex(string hex) => string.IsNullOrEmpty(hex) ? "------" : hex;

        static float ColorDistanceSq(Color a, Color b)
        {
            float dr = a.r - b.r;
            float dg = a.g - b.g;
            float db = a.b - b.b;
            return dr * dr + dg * dg + db * db;
        }

        static Color HexToColor(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var color);
            return color;
        }

        static Color ToGameColor(Color displayColor)
        {
            float exponent = Mathf.Max(0.01f, Plugin.ColorCorrectionExponent.Value);
            float brightness = Mathf.Clamp01(Plugin.ColorCorrectionBrightness.Value);
            return new Color(
                Mathf.Pow(displayColor.r, exponent) * brightness,
                Mathf.Pow(displayColor.g, exponent) * brightness,
                Mathf.Pow(displayColor.b, exponent) * brightness,
                displayColor.a);
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c); t.Apply();
            return t;
        }

        Font ResolveFont()
        {
            foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                if (f != null && f.name.IndexOf("KGNexttoMe", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return f;
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        void EnsureStyles()
        {
            if (_stylesReady) return;
            var f = ResolveFont();
            if (f == null) return;
            _font = f;

            _texPanel  = Solid(ColPanel);
            _texBtn    = Solid(ColBtn);
            _texBtnHov = Solid(ColBtnHov);
            _texWhite  = Solid(Color.white);
            _texDark   = Solid(ColDark);
            _texAccent = Solid(ColAccent);

            _titleStyle = new GUIStyle { font = _font, fontSize = 20, alignment = TextAnchor.MiddleCenter };
            _titleStyle.normal.textColor = ColTitle;

            _labelStyle = new GUIStyle { font = _font, fontSize = 18, alignment = TextAnchor.MiddleLeft };
            _labelStyle.normal.textColor = ColBody;

            _fieldStyle = new GUIStyle(GUI.skin.textField) { font = _font, fontSize = 18, alignment = TextAnchor.MiddleLeft };
            _fieldStyle.normal.textColor  = ColBody;
            _fieldStyle.focused.textColor = ColTitle;

            _btnStyle = new GUIStyle { font = _font, fontSize = 20, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(10, 10, 6, 6) };
            _btnStyle.normal.background  = _texBtn;    _btnStyle.normal.textColor  = ColBtnText;
            _btnStyle.hover.background   = _texBtnHov; _btnStyle.hover.textColor   = ColBtnText;
            _btnStyle.active.background  = _texBtnHov; _btnStyle.active.textColor  = ColBtnText;

            _actionBtnStyle = new GUIStyle(_btnStyle) { fontSize = 14 };

            _toggleStyle = new GUIStyle { font = _font, fontSize = 16, alignment = TextAnchor.MiddleCenter };
            _toggleStyle.normal.textColor = Color.white;

            _plusStyle = new GUIStyle { font = _font, fontSize = 24, alignment = TextAnchor.MiddleCenter };
            _plusStyle.normal.textColor = Color.white;

            _xStyle = new GUIStyle { font = _font, fontSize = 11, alignment = TextAnchor.MiddleCenter };
            _xStyle.normal.textColor = Color.white;

            _stylesReady = true;
        }

        // ---- Controller pointer helpers ----

        // Returns the best available pointer position: controller cursor tip when a controller
        // is active, IMGUI mouse position otherwise. Use this everywhere instead of e.mousePosition
        // so controller and mouse work identically for hit-testing.
        static Vector2 PointerPos(Event e)
            => e.mousePosition;

        // Returns true if the pointer (mouse or controller) is inside rect.
        static bool PointerIn(Rect r, Event e) => r.Contains(e.mousePosition);

        // Returns true exactly once when the controller Accept button is pressed while the
        // pointer is over rect. Returns false immediately in mouse-only sessions.
        static bool ControllerClick(Rect r)
        {
            return false;
        }

        class LutNeighbor
        {
            public readonly string DisplayHex;
            public readonly Color Backend;
            public readonly float DistanceSq;

            public LutNeighbor(string displayHex, Color backend, float distanceSq)
            {
                DisplayHex = displayHex;
                Backend = backend;
                DistanceSq = distanceSq;
            }
        }

        class CanvasSwatch
        {
            public RectTransform Root;
            public Image Fill;
            public Image Border;
            public RectTransform DeleteRoot;
            public Image DeleteBg;
            public Text DeleteText;
        }
    }
}
