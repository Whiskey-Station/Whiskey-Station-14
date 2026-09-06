// ReSharper disable ArrangeTrailingCommaInMultilineLists
namespace Content.Server.Entry
{
    public static class IgnoredComponents
    {
        public static string[] List => new[] {
            // <Whiskey> - os seis do Trauma sairam daqui de proposito: eles
            // migraram para Content.Trauma.Server/Entry/EntryPoint.cs, que usa
            // RegisterIgnore e veio neste upstream. Manter aqui duplicaria.
            //
            // Estes tres continuam porque sao do cliente e nao tem dono do lado
            // do servidor: eles vivem em Content.Client/_ES. O certo seria um
            // EntryPoint do Whiskey com RegisterIgnore, igual ao do Trauma, mas
            // isso e mudanca de estrutura e nao cabe num merge de upstream.
            "ESTimedDespawnLightFade",
            "ESTimedDespawnSpriteFade",
            "ESGenericPointLightVisualizer",
            // </Whiskey>
            "ConstructionGhost",
            "IconSmooth",
            "InteractionOutline",
            "Marker",
            "GuidebookControlsTest",
            "GuideHelp",
            "Clickable",
            "Icon",
            "CableVisualizer",
            "SolutionItemStatus",
            "UIFragment",
            "PdaBorderColor",
            "InventorySlots",
            "LightFade",
            "HolidayRsiSwap",
            "OptionsVisualizer",
            "AnomalyScannerScreen",
            "MultipartMachineGhost",
            "DirectionalArrowIndicator"
        };
    }
}
