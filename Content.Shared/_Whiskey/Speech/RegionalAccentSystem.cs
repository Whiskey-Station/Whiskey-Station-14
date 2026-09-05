// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using System.Text.RegularExpressions;
using Content.Shared.Random.Helpers;
using Content.Shared.Speech.EntitySystems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._Whiskey.Speech;

/// <summary>
/// Troca palavra por gíria e às vezes acrescenta bordão. O fecho muda conforme
/// a pontuação, senão soa robótico.
/// </summary>
/// <remarks>
/// Trocar palavra ANTES do bordão: invertido, o bordão vira candidato a ser
/// trocado também.
/// </remarks>
public sealed partial class RegionalAccentSystem : RelayAccentSystem<RegionalAccentComponent>
{
    private static readonly Regex RegexPrimeiraPalavra = new(@"^(\S+)");
    private static readonly Regex RegexUltimaPalavra = new(@"(\S+)$");
    private static readonly Regex RegexTerminaPergunta = new(@"\?+\s*$");
    private static readonly Regex RegexTerminaExclamacao = new(@"!+\s*$");
    private static readonly Regex RegexPontuacaoFinal = new(@"([.!?]+$)(?!.*[.!?])|(?<![.!?])$");

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ReplacementAccentSystem _replacement = default!;

    public override string Accentuate(string message, Entity<RegionalAccentComponent>? ent = null)
    {
        if (ent is not { } sotaque)
            return message;

        var random = SharedRandomExtensions.PredictedRandom(_timing, GetNetEntity(sotaque));
        var comp = sotaque.Comp;

        var msg = _replacement.ApplyReplacements(message, comp.Accent, sotaque.Owner);

        if (string.IsNullOrWhiteSpace(msg))
            return msg;

        var chave = comp.Accent.ToLowerInvariant();

        if (comp.PrefixCount > 0 && random.Prob(comp.PrefixChance))
        {
            var prefixo = Loc.GetString($"accent-{chave}-prefix-{random.Next(1, comp.PrefixCount + 1)}");

            // Quem estava gritando continua gritando: se a primeira palavra vem
            // toda em maiúscula, o bordão sobe junto em vez de a frase descer.
            // Mesmo tratamento que o Fechar faz do outro lado, e que o
            // PirateAccentSystem do jogo base já fazia.
            if (RegexPrimeiraPalavra.Match(msg).Value.Any(char.IsLower))
            {
                // A frase perde a maiúscula inicial porque agora vem depois do
                // bordão, senão sai "Pô, Preciso de ajuda".
                msg = msg[0].ToString().ToLower() + msg.Remove(0, 1);
            }
            else
            {
                prefixo = prefixo.ToUpper();
            }

            msg = $"{prefixo} {msg}";
        }

        if (random.Prob(comp.SuffixChance))
            msg = Fechar(msg, comp, chave, random);

        return msg[0].ToString().ToUpper() + msg.Remove(0, 1);
    }

    private string Fechar(string msg, RegionalAccentComponent comp, string chave, IRobustRandom random)
    {
        string bordao;

        if (RegexTerminaPergunta.IsMatch(msg) && comp.QuestionCount > 0)
            bordao = Loc.GetString($"accent-{chave}-suffix-pergunta-{random.Next(1, comp.QuestionCount + 1)}");
        else if (RegexTerminaExclamacao.IsMatch(msg) && comp.ExclamationCount > 0)
            bordao = Loc.GetString($"accent-{chave}-suffix-exclamacao-{random.Next(1, comp.ExclamationCount + 1)}");
        else if (comp.SuffixCount > 0)
            bordao = Loc.GetString($"accent-{chave}-suffix-{random.Next(1, comp.SuffixCount + 1)}");
        else
            return msg;

        // Quem estava gritando continua gritando até o fim da frase.
        if (!RegexUltimaPalavra.Match(msg).Value.Any(char.IsLower))
            bordao = bordao.ToUpper();

        return RegexPontuacaoFinal.Replace(msg, bordao);
    }
}
