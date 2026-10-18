using MelonLoader;

[assembly: MelonInfo(typeof(InFalsusChartLoader.ChartLoaderMod), "InFalsusChartLoader",
                     InFalsusChartLoader.ModCore.Version, "MagicNeko",
                     "https://github.com/Its-MagicNeko/InFalsusChartLoader/releases/latest")]
[assembly: MelonGame("lowiro", "infalsus")]

namespace InFalsusChartLoader
{
    /// <summary>
    /// MelonLoader's entry point: four callbacks, each handing over to <see cref="ModCore"/>.
    ///
    /// <para>
    /// The work itself lives in <see cref="ModCore"/> rather than here so that a second loader can
    /// call the same three stages without copying them. This class carries only what MelonLoader
    /// itself needs: the base class, the assembly attributes, and the callback names.
    /// </para>
    /// </summary>
    public class ChartLoaderMod : MelonMod
    {
        public override void OnInitializeMelon() => ModCore.Loaded();

        public override void OnLateInitializeMelon() => ModCore.Initialize();

        public override void OnUpdate() => ModCore.Update();

        public override void OnDeinitializeMelon() => ModCore.Shutdown();
    }
}
