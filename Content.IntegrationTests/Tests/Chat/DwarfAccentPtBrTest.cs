using System.Globalization;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Speech.EntitySystems;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests.Chat;

public sealed class DwarfAccentPtBrTest : GameTest
{
    [Test]
    public void DwarfAccentUsesCoherentPtBrVocabulary()
    {
        var localization = Pair.Server.ResolveDependency<ILocalizationManager>();
        var originalCulture = localization.DefaultCulture;

        try
        {
            localization.SetCulture(CultureInfo.GetCultureInfo("pt-BR"));

            var accent = Pair.Server.System<ReplacementAccentSystem>();
            var result = accent.ApplyReplacements(
                "O novato é um humano covarde e idiota.",
                "dwarf");

            Assert.That(result,
                Is.EqualTo("O barba-verde é um grandalhão amante-de-folhas e cabeça-oca."));
        }
        finally
        {
            if (originalCulture != null)
                localization.SetCulture(originalCulture);
        }
    }
}
