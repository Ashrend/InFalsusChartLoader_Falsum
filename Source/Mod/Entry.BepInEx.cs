#if BEPINEX
extern alias UnityEngineCore;

using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngineCore::UnityEngine;

namespace InFalsusChartLoader
{
    /// <summary>
    /// BepInEx's entry point.
    ///
    /// <para>
    /// `BasePlugin.Load` is the one mandatory callback (measured on 6.0.0-be.789: `Load` is
    /// abstract, `Unload` is virtual, and the class has no `Update` of its own). The work is the
    /// same three stages MelonLoader's entry calls; the per-frame half is a component added to the
    /// scene here, which is what `BasePlugin.AddComponent&lt;T&gt;` is for.
    /// </para>
    /// </summary>
    [BepInPlugin("MagicNeko-InFalsusChartLoader", "InFalsusChartLoader",
                 ModCore.Version)]
    [BepInProcess("infalsus.exe")]
    public class ChartLoaderPlugin : BasePlugin
    {
        public override void Load()
        {
            ModCore.Loaded();
            ModCore.Initialize();
            AddComponent<ChartLoaderTick>();
        }

        public override bool Unload()
        {
            ModCore.Shutdown();
            return true;
        }
    }

    /// <summary>
    /// The per-frame half of <see cref="ModCore.Update"/>, for the loader that has no `Update`.
    ///
    /// `AddComponent` injects the type with the IL2CPP type system; the base class is the
    /// generated `UnityEngine.MonoBehaviour`, which is what makes it an `Il2CppObjectBase`.
    /// </summary>
    public sealed class ChartLoaderTick : MonoBehaviour
    {
        private void Update() => ModCore.Update();
    }
}
#endif
