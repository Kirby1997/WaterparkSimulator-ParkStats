using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using CayplayAI;
using Il2CppInterop.Runtime;

namespace ParkStats.Plugin;

/// <summary>
/// Writes where the game's own code for a handful of classes sits in GameAssembly.dll, and
/// where their fields sit in memory. The mod reads values; how the game combines them (the
/// visitor cap, tips, the ticket price) lives in native code, and this is what makes that
/// code findable. A development aid, written once per session.
/// </summary>
internal static class MethodAddresses
{
    private const string MethodPrefix = "NativeMethodInfoPtr_";
    private const string FieldPrefix = "NativeFieldInfoPtr_";

    private static readonly Type[] Types =
    {
        typeof(GameManager), typeof(ParkSettings), typeof(HistorySystem), typeof(FinanceSystem),
        typeof(TicketSettings), typeof(TicketPrestigeSettings), typeof(StaffManager), typeof(AINetManager),
        typeof(AttractionManager), typeof(AttractionInteraction), typeof(AttractionData), typeof(SaunaInteraction),
        typeof(HotTubInteraction), typeof(ShopInteraction), typeof(VisitorThoughtsSystem), typeof(TrackerSystem),
        typeof(AIBrain), typeof(AIDataStorage), typeof(VisitorSettings), typeof(SpawnSettings),
        typeof(UnityEngine.AnimationCurve), typeof(UnityEngine.Mathf),
    };

    public static void Write(string path)
    {
        var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
            .FirstOrDefault(m => string.Equals(m.ModuleName, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
        if (module == null) return;
        var imageBase = module.BaseAddress.ToInt64();

        var sb = new StringBuilder();
        sb.AppendLine($"# GameAssembly.dll base {imageBase:x}, size {module.ModuleMemorySize:x}. Addresses are relative to the base.");
        foreach (var type in Types)
        {
            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType != typeof(IntPtr)) continue;
                try
                {
                    var info = (IntPtr)field.GetValue(null);
                    if (info == IntPtr.Zero) continue;

                    if (field.Name.StartsWith(MethodPrefix))
                    {
                        // The first word of an il2cpp MethodInfo is the address of its code.
                        var code = Marshal.ReadIntPtr(info).ToInt64();
                        if (code != 0) sb.AppendLine($"M {type.FullName}::{field.Name[MethodPrefix.Length..]} {code - imageBase:x}");
                    }
                    else if (field.Name.StartsWith(FieldPrefix))
                    {
                        sb.AppendLine($"F {type.FullName}::{field.Name[FieldPrefix.Length..]} {IL2CPP.il2cpp_field_get_offset(info):x}");
                    }
                }
                catch (Exception e)
                {
                    sb.AppendLine($"# {type.FullName}::{field.Name} <{e.GetType().Name}>");
                }
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, sb.ToString());
    }
}
