using System.Globalization;
using System.Text;
using ParkStats.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ParkStats.Plugin;

/// <summary>
/// Writes one text file describing what the mod sees: panel geometry, raw game values, every
/// tab as plain text, and the layout of the tablet page. Used to adjust the mod after game updates.
/// </summary>
internal static class Diagnostics
{
    public static void Write(string path, TabletUI tablet, GameReader reader, TabletPanel panel, IReadOnlyList<string> panelLines)
    {
        var sb = new StringBuilder();

        sb.AppendLine("== Panel ==");
        foreach (var line in panelLines) sb.AppendLine(line);

        sb.AppendLine().AppendLine("== Raw game values ==");
        var raw = new List<string>();
        var park = Guard(sb, () => reader.Read(raw));
        foreach (var line in raw) sb.AppendLine(line);
        sb.AppendLine("park key = " + reader.ParkKey());

        sb.AppendLine().AppendLine("== Recent complaints (thought | guest state | state before | attraction) ==");
        foreach (var line in reader.Complaints.Recent) sb.AppendLine(line);

        if (park != null)
        {
            foreach (var tab in TabletPanel.ModTabs)
            {
                sb.AppendLine().AppendLine($"== Tab: {tab} ==");
                Guard(sb, () =>
                {
                    foreach (var row in panel.Rows(panel.TabIndex(tab))) sb.AppendLine($"[{row.Kind}] {row.Text}");
                    return true;
                });
            }
        }

        sb.AppendLine().AppendLine("== Tablet elements ==");
        Element(sb, "TicketPrice", tablet.TicketPrice);
        Element(sb, "TicketPriceInfo", tablet.TicketPriceInfo);
        Element(sb, "VisitorsAmount", tablet.VisitorsAmount);
        Element(sb, "TeensAmount", tablet.TeensAmount);
        Element(sb, "AdultsAmount", tablet.AdultsAmount);
        Element(sb, "SeniorsAmount", tablet.SeniorsAmount);
        Element(sb, "OverallSlider", tablet.OverallSlider);
        Element(sb, "FunSlider", tablet.FunSlider);
        Element(sb, "EnergySlider", tablet.EnergySlider);
        Element(sb, "HygeineSlider", tablet.HygeineSlider);
        Element(sb, "ThirstSlider", tablet.ThirstSlider);
        Element(sb, "HungerSlider", tablet.HungerSlider);
        Element(sb, "ToiletSlider", tablet.ToiletSlider);
        Element(sb, "TrashSlider", tablet.TrashSlider);
        Element(sb, "ManagementPage.firstButton", tablet.ManagementPage?.firstButton);

        var root = tablet.ManagementPage?.mainObject?.transform;
        if (root != null)
        {
            sb.AppendLine().AppendLine("== Parents of the management page (nearest first) ==");
            for (var parent = root.parent; parent != null; parent = parent.parent) Node(sb, parent, 0);

            sb.AppendLine().AppendLine("== Management page ==");
            Tree(sb, root, 0);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, sb.ToString());
    }

    private static void Element(StringBuilder sb, string name, Component component)
    {
        sb.Append(name).Append(" = ");
        try
        {
            if (component == null)
            {
                sb.AppendLine("null");
                return;
            }

            var names = new List<string>();
            for (var t = component.transform; t != null; t = t.parent) names.Add(t.name);
            names.Reverse();
            sb.Append(string.Join("/", names));

            var image = component.TryCast<Image>();
            if (image != null) sb.Append(" | fill ").Append(N(image.fillAmount));
            var text = component.TryCast<TMP_Text>();
            if (text != null) sb.Append(" | \"").Append(text.text).Append('"');
            sb.AppendLine();
        }
        catch (Exception e)
        {
            sb.AppendLine($"<{e.GetType().Name}: {e.Message}>");
        }
    }

    private static void Tree(StringBuilder sb, Transform transform, int depth)
    {
        Node(sb, transform, depth);
        for (var i = 0; i < transform.childCount; i++) Tree(sb, transform.GetChild(i), depth + 1);
    }

    private static void Node(StringBuilder sb, Transform transform, int depth)
    {
        sb.Append(' ', depth * 2);
        try
        {
            var gameObject = transform.gameObject;
            sb.Append(gameObject.name);
            if (!gameObject.activeSelf) sb.Append(" (inactive)");

            var rect = transform.TryCast<RectTransform>();
            if (rect != null)
            {
                var area = rect.rect;
                sb.Append(" | anchors ").Append(V(rect.anchorMin)).Append('-').Append(V(rect.anchorMax))
                  .Append(" pivot ").Append(V(rect.pivot))
                  .Append(" pos ").Append(V(rect.anchoredPosition))
                  .Append(" sizeDelta ").Append(V(rect.sizeDelta))
                  .Append(" size (").Append(N(area.width)).Append(',').Append(N(area.height)).Append(')');
            }

            sb.Append(" |");
            var components = gameObject.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i] != null) sb.Append(' ').Append(Label(components[i])).Append(';');
            }
        }
        catch (Exception e)
        {
            sb.Append($" <{e.GetType().Name}: {e.Message}>");
        }
        sb.AppendLine();
    }

    private static string Label(Component component)
    {
        // The proxy is typed as Component; the real class comes from the Il2Cpp side.
        var type = component.GetIl2CppType().FullName;
        try
        {
            var text = component.TryCast<TMP_Text>();
            if (text != null)
            {
                var content = text.text ?? "";
                if (content.Length > 40) content = content[..40] + "...";
                return $"{type}[\"{content.Replace("\n", "\\n")}\" size {N(text.fontSize)} auto {text.enableAutoSizing} " +
                       $"align {text.alignment} font {text.font?.name} color {C(text.color)}]";
            }

            var image = component.TryCast<Image>();
            if (image != null)
            {
                return $"{type}[sprite {image.sprite?.name ?? "none"} type {image.type} fill {N(image.fillAmount)} color {C(image.color)}]";
            }

            var canvas = component.TryCast<Canvas>();
            if (canvas != null) return $"{type}[{canvas.renderMode} scale {N(canvas.scaleFactor)}]";

            var scaler = component.TryCast<CanvasScaler>();
            if (scaler != null) return $"{type}[{scaler.uiScaleMode} reference {V(scaler.referenceResolution)}]";
        }
        catch (Exception e)
        {
            return $"{type}[<{e.GetType().Name}: {e.Message}>]";
        }
        return type;
    }

    private static T Guard<T>(StringBuilder sb, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception e)
        {
            sb.AppendLine($"<{e.GetType().Name}: {e.Message}>");
            return default;
        }
    }

    private static string V(Vector2 vector) => $"({N(vector.x)},{N(vector.y)})";

    private static string C(Color color) => $"({N(color.r)},{N(color.g)},{N(color.b)},{N(color.a)})";

    private static string N(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
