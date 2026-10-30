using MelonLoader;

[assembly: MelonInfo(typeof(InFalsusChartLoader_falsum.ChartLoaderMod), "InFalsusChartLoader_falsum",
                     InFalsusChartLoader_falsum.ModCore.Version, "MagicNeko, (Ashrend_Fork)",
                     "https://github.com/Its-MagicNeko/InFalsusChartLoader_falsum/releases/latest")]
[assembly: MelonGame("lowiro", "infalsus")]

namespace InFalsusChartLoader_falsum
{
    /// <summary>MelonLoader's entry point; every callback hands over to <see cref="ModCore"/>.</summary>
    public class ChartLoaderMod : MelonMod
    {
        public override void OnInitializeMelon() => ModCore.Loaded();

        public override void OnLateInitializeMelon() => ModCore.Initialize();

        public override void OnUpdate() => ModCore.Update();

        public override void OnDeinitializeMelon() => ModCore.Shutdown();
    }
}
