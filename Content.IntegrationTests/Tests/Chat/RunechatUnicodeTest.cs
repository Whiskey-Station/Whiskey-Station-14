using System.Text;
using Content.Client.Chat.UI;
using Content.Client.Resources;
using Content.IntegrationTests.Fixtures;
using Robust.Client.ResourceManagement;

namespace Content.IntegrationTests.Tests.Chat;

public sealed class RunechatUnicodeTest : GameTest
{
    [Test]
    public async Task RunechatFontSupportsCyrillicText()
    {
        var client = Pair.Client;

        await client.WaitAssertion(() =>
        {
            var resourceCache = client.ResolveDependency<IResourceCache>();
            var preferredFont = resourceCache.GetFont("/Fonts/Grand9K_Pixel.ttf", 10);
            var font = RunechatSpeechBubble.AddUnicodeFallback(resourceCache, preferredFont, 10);

            foreach (var rune in "Привет, станция!".EnumerateRunes())
            {
                if (Rune.IsWhiteSpace(rune))
                    continue;

                Assert.That(font.GetCharMetrics(rune, 1f, fallback: false), Is.Not.Null,
                    $"A fonte do runechat não contém o caractere cirílico '{rune}'.");
            }
        });
    }
}
