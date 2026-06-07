using System.Collections.Generic;
using System.Linq;
using Cinnamon.UI;
using GameEvent;
using UnityEngine;

namespace OssieCustomColors
{
    public partial class ColorPickerUI : MonoBehaviour
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
        bool _wasFreeplayLastFrame;
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
            LoadColors();
            LoadCalibrationData();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
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
                                && _inventoryBookCache.gameObject.activeInHierarchy;

            if (bookActiveNow && !_bookOpen)
            {
                _bookOpen = true;
                _allColorBtns = null;
                _layoutReady = false;
            }
            else if (!bookActiveNow && _bookOpen)
            {
                _bookOpen = false;
                _customMode = false;
                _open = false;
                SyncNativesToMode();
            }

            if (!freeplay || !_bookOpen)
            {
                if (_wasFreeplayLastFrame)
                {
                    _customMode = false;
                    _open = false;
                    SyncNativesToMode();
                }
                _wasFreeplayLastFrame = false;
                return;
            }
            _wasFreeplayLastFrame = true;

            if (_open && Input.GetKeyDown(KeyCode.Escape))
                _open = false;

            bool colorsTabActive = _layoutReady
                                  && _allColorBtns != null
                                  && _allColorBtns.Length > 0
                                  && _allColorBtns[0] != null
                                  && _allColorBtns[0].transform.parent != null
                                  && _allColorBtns[0].transform.parent.gameObject.activeInHierarchy;

            SyncNativesToMode();

            bool uiVisible = colorsTabActive || _open;
            if (uiVisible) CursorOverlay.Request();

            CheckCalibrationInput();
        }

        public void OnBookShown()
        {
            _bookOpen = true;
            _inventoryBookCache = null;
            _allColorBtns = null;
            _layoutReady = false;
        }

        public void OnBookHidden()
        {
            _bookOpen = false;
            _customMode = false;
            _open = false;
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

            GUI.depth = -100;
            RefreshButtons();

            if (_layoutReady)
            {
                bool colorsTabActive = _allColorBtns != null
                    && _allColorBtns.Length > 0
                    && _allColorBtns[0] != null
                    && _allColorBtns[0].transform.parent != null
                    && _allColorBtns[0].transform.parent.gameObject.activeInHierarchy;

                if (colorsTabActive)
                {
                    DrawToggleButton();
                    if (_customMode) DrawCustomPalette();
                }
            }

            if (_open) DrawPickerPanel();
            DrawCalibrationUI();
        }

        // ---- native button cache ----

        void RefreshButtons()
        {
            if (_allColorBtns != null && _allColorBtns.Length > 0 && _allColorBtns[0] != null)
                return;

            _layoutReady = false;

            var found = Object.FindObjectsOfType<PickableCustomizationButton>()
                .Where(b => b.customizationType == CustomizationType.BlockColors)
                .OrderByDescending(b => b.transform.position.y)
                .ThenBy(b => b.transform.position.x)
                .ToArray();

            Plugin.Log.LogInfo($"[OssieCustomColors] RefreshButtons: {found.Length} BlockColors buttons found");
            if (found.Length == 0) return;

            _allColorBtns = found;

            var cam = Camera.main;
            if (cam == null) return;

            var sr = _allColorBtns[0].GetComponentInChildren<SpriteRenderer>();
            if (sr != null)
            {
                var sMin = cam.WorldToScreenPoint(sr.bounds.min);
                var sMax = cam.WorldToScreenPoint(sr.bounds.max);
                _btnW = Mathf.Abs(sMax.x - sMin.x);
                _btnH = Mathf.Abs(sMax.y - sMin.y);
            }

            if (_btnW <= 0f) _btnW = _btnH = 40f;

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

        // ---- show / hide native buttons ----

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
            float cx = p.x + _btnW * 1.1f;
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

            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                _customMode = !_customMode;
                if (_customMode) HideNativeButtons();
                else ShowNativeButtons();
                e.Use();
            }

            if (e.isMouse && rect.Contains(e.mousePosition)) e.Use();
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
                bool hovered = swRect.Contains(e.mousePosition);
                var xRect = new Rect(swRect.xMax - 14f, swRect.yMin, 14f, 14f);
                bool xHovered = hovered && xRect.Contains(e.mousePosition);

                if (e.type == EventType.MouseDown)
                {
                    if (xHovered)
                    {
                        _savedColors.RemoveAt(i);
                        if (_selectedIdx == i) _selectedIdx = -1;
                        else if (_selectedIdx > i) _selectedIdx--;
                        SaveColors();
                        e.Use();
                        return;
                    }
                    else if (hovered)
                    {
                        SendColorChange(_savedColors[i]);
                        _selectedIdx = i;
                        e.Use();
                    }
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

                if (e.isMouse && hovered) e.Use();
            }

            if (_savedColors.Count < _maxColors)
            {
                var plusRect = PlusRect(cam);
                bool hovered = plusRect.Contains(e.mousePosition);

                if (isRepaint)
                {
                    GUI.DrawTexture(plusRect, _texDark);
                    GUI.Label(plusRect, "+", _plusStyle);
                }

                if (e.type == EventType.MouseDown && hovered)
                {
                    Open(_lastDisplayColor);
                    e.Use();
                }

                if (e.isMouse && hovered) e.Use();
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

            if (e.type == EventType.MouseDown && svRect.Contains(e.mousePosition)) _svDragging = true;
            if (e.type == EventType.MouseUp) _svDragging = false;
            if (_svDragging && (e.type == EventType.MouseDrag || e.type == EventType.MouseDown))
            {
                _s = Mathf.Clamp01((e.mousePosition.x - svRect.x) / svRect.width);
                _v = Mathf.Clamp01(1f - (e.mousePosition.y - svRect.y) / svRect.height);
                UpdateRgbFromHsv();
                e.Use();
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

            if (e.type == EventType.MouseDown && hueRect.Contains(e.mousePosition)) _hueDragging = true;
            if (e.type == EventType.MouseUp) _hueDragging = false;
            if (_hueDragging && (e.type == EventType.MouseDrag || e.type == EventType.MouseDown))
            {
                _h = Mathf.Clamp01((e.mousePosition.x - hueRect.x) / hueRect.width);
                _lastHueForSvTex = -1f;
                UpdateRgbFromHsv();
                e.Use();
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
            if (GUI.Button(new Rect(bx,        cy, 100f, btnsH), "Use Color",  _actionBtnStyle)) UseColor();
            if (GUI.Button(new Rect(bx + 106f, cy, 100f, btnsH), "Save Color", _actionBtnStyle)) SaveColor();
            if (GUI.Button(new Rect(bx + 212f, cy,  80f, btnsH), "Cancel",     _actionBtnStyle)) _open = false;

            if (e.isMouse && e.type != EventType.Used)
                e.Use();
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
    }
}
