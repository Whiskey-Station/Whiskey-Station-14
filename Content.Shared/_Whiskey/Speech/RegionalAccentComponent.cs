// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Speech.Components;
using Robust.Shared.GameStates;

namespace Content.Shared._Whiskey.Speech;

/// <summary>
/// Sotaque regional brasileiro: troca palavras pela gíria da região, às vezes
/// abre a frase com bordão e às vezes fecha com outro, variando conforme a
/// pontuação.
/// </summary>
/// <remarks>
/// É genérico de propósito. Acrescentar uma região nova deve ser prototype mais
/// arquivo de texto, sem código novo. As contagens ficam aqui porque o Fluent
/// não sabe dizer quantas chaves existem, então alguém precisa contar.
/// </remarks>
[RegisterComponent, NetworkedComponent]
[Access(typeof(RegionalAccentSystem))]
public sealed partial class RegionalAccentComponent : BaseAccentComponent
{
    /// <summary>
    /// Nome do prototype de troca de palavras, e também o prefixo usado nas
    /// chaves de texto, como em accent-carioca-prefix-1.
    /// </summary>
    [DataField(required: true)]
    public string Accent = string.Empty;

    [DataField]
    public float PrefixChance = 0.3f;

    [DataField]
    public float SuffixChance = 0.35f;

    [DataField]
    public int PrefixCount = 5;

    [DataField]
    public int SuffixCount = 5;

    [DataField]
    public int QuestionCount = 3;

    [DataField]
    public int ExclamationCount = 3;
}
