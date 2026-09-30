using System.Globalization;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ParkStats.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ParkStats.Plugin;

/// <summary>
/// The tab bar and content area added to the tablet's Park Management page. Positions are
/// derived at runtime from the game's own elements rather than from fixed coordinates, so the
/// panel follows the page if a game update moves things around.
/// </summary>
internal sealed class TabletPanel
{
    private static readonly string[] TabNames = { "Park", "Today", "Money", "Rides", "Guests", "History", "Advice" };

    // The first tab shows the game's own page untouched.
    private const int ParkTab = 0;
    private const long RefreshIntervalMs = 1000;
    private const float Padding = 12f;
    private const float MinTabHeight = 36f;
    private const float MaxTabHeight = 80f;
    // Roughly how many characters of the widest table fit on one line.
    private const float CharactersPerLine = 32f;

    private static readonly Color PanelColor = new(0.04f, 0.17f, 0.32f, 1f);
    private static readonly Color TabColor = new(0.02f, 0.55f, 0.72f, 1f);
    private static readonly Color SelectedTabColor = new(0.96f, 0.78f, 0.08f, 1f);

    private readonly ManualLogSource _log;
    private readonly GameReader _reader;
    private readonly Func<HistoryStore> _history;
    private readonly float _fontScale;
    private readonly Action<TabletUI, IReadOnlyList<string>> _diagnose;
    private readonly Dictionary<string, int> _events = new();
    private readonly List<string> _geometry = new();

    private IntPtr _tabletPointer;
    private TabletUI _tablet;
    private bool _broken;
    private bool _laidOut;
    private bool _diagnosed;
    private long _lastLayout;
    private RectTransform _root;
    private GameObject _bar;
    private GameObject _content;
    private TextMeshProUGUI _text;
    private ScrollRect _scroll;
    private Image[] _tabImages = Array.Empty<Image>();
    private int _selected = ParkTab;
    private long _lastRefresh;

    public TabletPanel(ManualLogSource log, GameReader reader, Func<HistoryStore> history, float fontScale,
        Action<TabletUI, IReadOnlyList<string>> diagnose)
    {
        _log = log;
        _reader = reader;
        _history = history;
        _fontScale = fontScale;
        _diagnose = diagnose;
    }

    /// <summary>Called from every tablet hook; builds the panel when needed and keeps it current.</summary>
    public void OnTabletEvent(TabletUI tablet, string source)
    {
        _events[source] = _events.GetValueOrDefault(source) + 1;

        var page = tablet?.ManagementPage?.mainObject;
        if (tablet == null || page == null || !page.activeInHierarchy) return;

        // A new scene creates a new tablet, and the old panel went with the old one.
        if (_tabletPointer != tablet.Pointer)
        {
            _tabletPointer = tablet.Pointer;
            _tablet = tablet;
            _diagnosed = false;
            _laidOut = false;
            _selected = ParkTab;
            try
            {
                Build(tablet, page);
                _broken = false;
            }
            catch (Exception e)
            {
                _broken = true;
                _log.LogError($"Could not build the stats panel: {e}");
            }
        }

        // Some hooks fire very often; measuring the page once a second is enough to follow it.
        var now = Environment.TickCount64;
        if (!_broken && (!_laidOut || now - _lastLayout >= RefreshIntervalMs))
        {
            _lastLayout = now;
            _laidOut = Layout(tablet);
        }

        if (!_diagnosed && (_laidOut || _broken))
        {
            _diagnosed = true;
            Diagnose(tablet);
        }
        if (_laidOut) Refresh(force: false);
    }

    private void Build(TabletUI tablet, GameObject page)
    {
        _root = page.GetComponent<RectTransform>();
        var template = tablet.TicketPrice;
        var layer = page.layer;

        _bar = NewObject("ParkStats Tabs", _root, layer);
        var barRect = _bar.GetComponent<RectTransform>();
        _tabImages = new Image[TabNames.Length];
        for (var i = 0; i < TabNames.Length; i++)
        {
            var tab = NewObject("Tab " + TabNames[i], barRect, layer);
            var tabRect = tab.GetComponent<RectTransform>();
            tabRect.anchorMin = new Vector2(i / (float)TabNames.Length, 0f);
            tabRect.anchorMax = new Vector2((i + 1) / (float)TabNames.Length, 1f);
            tabRect.offsetMin = new Vector2(2f, 0f);
            tabRect.offsetMax = new Vector2(-2f, 0f);

            var image = tab.AddComponent<Image>();
            var button = tab.AddComponent<Button>();
            button.targetGraphic = image;
            var index = i;
            button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(() => Select(index))));
            _tabImages[i] = image;

            var label = NewText("Label", tabRect, layer, template);
            Stretch(label.GetComponent<RectTransform>(), 4f);
            label.text = TabNames[i];
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = true;
            label.fontSizeMin = 8f;
            label.fontSizeMax = template.fontSize;
        }

        _content = NewObject("ParkStats Content", _root, layer);
        var contentRect = _content.GetComponent<RectTransform>();
        // Opaque, so it covers the game's own stats while a mod tab is selected.
        _content.AddComponent<Image>().color = PanelColor;

        var viewport = NewObject("Viewport", contentRect, layer);
        var viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect, 16f);
        viewport.AddComponent<RectMask2D>();

        _text = NewText("Text", viewportRect, layer, template);
        var textRect = _text.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0f, 1f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.pivot = new Vector2(0.5f, 1f);
        textRect.anchoredPosition = new Vector2(0f, 0f);
        textRect.sizeDelta = new Vector2(0f, 100f);
        _text.alignment = TextAlignmentOptions.TopLeft;
        _text.richText = true;
        _text.enableWordWrapping = true;
        _text.enableAutoSizing = false;
        _text.overflowMode = TextOverflowModes.Overflow;
        var fitter = _text.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _scroll = _content.AddComponent<ScrollRect>();
        _scroll.content = textRect;
        _scroll.viewport = viewportRect;
        _scroll.horizontal = false;
        _scroll.vertical = true;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 40f;

        _content.SetActive(false);
        PaintTabs();
        _log.LogInfo("Stats panel added to the tablet's Park Management page.");
    }

    /// <summary>Positions the bar and content area. False while the page has no size yet.</summary>
    private bool Layout(TabletUI tablet)
    {
        var area = StatsArea(tablet, out var areaSource);
        if (area.Width < 50f || area.Height < 50f) return false;

        var ticket = LocalBox(tablet.TicketPrice.GetComponent<RectTransform>());
        var gap = ticket.YMin - area.YMax;
        var tabHeight = Math.Clamp(gap - 2 * Padding, MinTabHeight, MaxTabHeight);

        // The bar goes in the empty space above the game's stats, or over their top edge if there is none.
        var fitsInGap = gap >= tabHeight + Padding;
        var barBottom = fitsInGap ? area.YMax + Padding / 2 : area.YMax - tabHeight;
        var bar = new Box(area.XMin, barBottom, area.XMax, barBottom + tabHeight);
        var content = fitsInGap ? area : area with { YMax = barBottom };

        Place(_bar.GetComponent<RectTransform>(), bar);
        Place(_content.GetComponent<RectTransform>(), content);
        _bar.transform.SetAsLastSibling();
        _content.transform.SetAsLastSibling();
        _text.fontSize = Math.Clamp(content.Width / CharactersPerLine * _fontScale, 10f, 60f);

        _geometry.Clear();
        var rootRect = _root.rect;
        _geometry.Add($"root rect = x {N(rootRect.x)} y {N(rootRect.y)} w {N(rootRect.width)} h {N(rootRect.height)}");
        _geometry.Add($"stats area ({areaSource}) = {area}");
        _geometry.Add($"ticket price text = {ticket}");
        _geometry.Add($"gap above stats = {N(gap)}, tab bar in gap = {fitsInGap}");
        _geometry.Add($"tab bar = {bar}");
        _geometry.Add($"content = {content}");
        _geometry.Add($"font size = {N(_text.fontSize)} (template {N(tablet.TicketPrice.fontSize)})");
        return true;
    }

    /// <summary>The part of the page holding the game's demographics and need bars, in page space.</summary>
    private Box StatsArea(TabletUI tablet, out string source)
    {
        var parts = new List<Transform>();
        void Add(Component component)
        {
            if (component != null) parts.Add(component.transform);
        }

        Add(tablet.TeensAmount);
        Add(tablet.AdultsAmount);
        Add(tablet.SeniorsAmount);
        Add(tablet.VisitorsAmount);
        Add(tablet.OverallSlider);
        Add(tablet.FunSlider);
        Add(tablet.EnergySlider);
        Add(tablet.HygeineSlider);
        Add(tablet.ThirstSlider);
        Add(tablet.HungerSlider);
        Add(tablet.ToiletSlider);
        Add(tablet.TrashSlider);

        source = "no stats elements found";
        if (parts.Count == 0) return default;

        // Prefer the smallest object containing all of them: normally the white card itself.
        var container = parts[0].parent;
        while (container != null && container.Pointer != _root.Pointer && !parts.All(p => p.IsChildOf(container)))
        {
            container = container.parent;
        }

        var ticket = tablet.TicketPrice.transform;
        if (container != null && container.Pointer != _root.Pointer && container.IsChildOf(_root) && !ticket.IsChildOf(container))
        {
            var containerRect = container.TryCast<RectTransform>();
            if (containerRect != null)
            {
                source = "container '" + container.name + "'";
                return LocalBox(containerRect);
            }
        }

        source = "union of stats elements";
        var union = LocalBox(parts[0].TryCast<RectTransform>());
        foreach (var part in parts.Skip(1))
        {
            var box = LocalBox(part.TryCast<RectTransform>());
            union = new Box(Math.Min(union.XMin, box.XMin), Math.Min(union.YMin, box.YMin),
                Math.Max(union.XMax, box.XMax), Math.Max(union.YMax, box.YMax));
        }
        return new Box(union.XMin - 2 * Padding, union.YMin - 2 * Padding, union.XMax + 2 * Padding, union.YMax + 2 * Padding);
    }

    private void Select(int index)
    {
        try
        {
            _selected = index;
            _content.SetActive(index != ParkTab);
            PaintTabs();
            if (index == ParkTab) return;

            Refresh(force: true);
            _scroll.verticalNormalizedPosition = 1f;
            Diagnose(_tablet);
        }
        catch (Exception e)
        {
            _log.LogError($"Could not switch stats tab: {e}");
        }
    }

    private void Refresh(bool force)
    {
        if (_selected == ParkTab) return;

        var now = Environment.TickCount64;
        if (!force && now - _lastRefresh < RefreshIntervalMs) return;
        _lastRefresh = now;

        _text.text = TmpMarkup.Render(Rows(_selected));
    }

    /// <summary>Rows for a tab; also used by diagnostics to write every tab as plain text.</summary>
    public IReadOnlyList<Row> Rows(int tab)
    {
        var park = _reader.Read();
        if (park == null) return new[] { Row.Of(RowKind.Muted, "No park loaded") };

        return TabNames[tab] switch
        {
            "Today" => Pages.Overview(park, Advisor.Evaluate(park)),
            "Money" => Pages.Money(park),
            "Rides" => Pages.Rides(park),
            "Guests" => Pages.Guests(park),
            "History" => Pages.History(_history()?.Records ?? Array.Empty<DayRecord>()),
            "Advice" => Pages.Advisor(Advisor.Evaluate(park)),
            _ => Array.Empty<Row>(),
        };
    }

    public static IReadOnlyList<string> ModTabs => TabNames.Skip(1).ToArray();

    public int TabIndex(string name) => Array.IndexOf(TabNames, name);

    private void Diagnose(TabletUI tablet)
    {
        if (_diagnose == null) return;
        var lines = new List<string> { "built = " + !_broken };
        lines.AddRange(_geometry);
        lines.Add("hook calls = " + string.Join(", ", _events.Select(p => $"{p.Key}:{p.Value}")));
        _diagnose(tablet, lines);
    }

    private void PaintTabs()
    {
        for (var i = 0; i < _tabImages.Length; i++)
        {
            _tabImages[i].color = i == _selected ? SelectedTabColor : TabColor;
        }
    }

    private static GameObject NewObject(string name, Transform parent, int layer)
    {
        var gameObject = new GameObject(name) { layer = layer };
        gameObject.AddComponent<RectTransform>().SetParent(parent, false);
        return gameObject;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, int layer, TMP_Text template)
    {
        var text = NewObject(name, parent, layer).AddComponent<TextMeshProUGUI>();
        // The game's font and material give the same outlined look as the rest of the tablet.
        text.font = template.font;
        text.fontSharedMaterial = template.fontSharedMaterial;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    /// <summary>Bounds of a rect in the page's local space.</summary>
    private Box LocalBox(RectTransform rect)
    {
        var corners = new Il2CppStructArray<Vector3>(4);
        rect.GetWorldCorners(corners);

        float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
        for (var i = 0; i < 4; i++)
        {
            var point = _root.InverseTransformPoint(corners[i]);
            xMin = Math.Min(xMin, point.x);
            yMin = Math.Min(yMin, point.y);
            xMax = Math.Max(xMax, point.x);
            yMax = Math.Max(yMax, point.y);
        }
        return new Box(xMin, yMin, xMax, yMax);
    }

    /// <summary>Gives a child of the page the given bounds in the page's local space.</summary>
    private void Place(RectTransform rect, Box box)
    {
        var rootRect = _root.rect;
        var rootCenterX = rootRect.x + rootRect.width / 2;
        var rootCenterY = rootRect.y + rootRect.height / 2;

        var middle = new Vector2(0.5f, 0.5f);
        rect.anchorMin = middle;
        rect.anchorMax = middle;
        rect.pivot = middle;
        rect.anchoredPosition = new Vector2(box.CenterX - rootCenterX, box.CenterY - rootCenterY);
        rect.sizeDelta = new Vector2(box.Width, box.Height);
    }

    private static string N(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private readonly record struct Box(float XMin, float YMin, float XMax, float YMax)
    {
        public float Width => XMax - XMin;
        public float Height => YMax - YMin;
        public float CenterX => (XMin + XMax) / 2;
        public float CenterY => (YMin + YMax) / 2;

        public override string ToString() => $"x {N(XMin)}..{N(XMax)} y {N(YMin)}..{N(YMax)} ({N(Width)} x {N(Height)})";
    }
}
