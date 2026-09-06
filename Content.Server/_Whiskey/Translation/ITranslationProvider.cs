using System.Threading;
using System.Threading.Tasks;

namespace Content.Server._Whiskey.Translation;

/// <summary>
/// Contrato de quem sabe traduzir texto. Assíncrono de propósito: motor real
/// demora, e esperar no tick travaria o servidor.
/// </summary>
public interface ITranslationProvider
{
    /// <summary>
    /// Nome curto para aparecer em log e em comando de admin.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Falso no provedor vazio, para o jogo avisar em vez de fingir que traduziu.
    /// </summary>
    bool CanTranslate { get; }

    /// <summary>
    /// Traduz <paramref name="text"/> de <paramref name="from"/> para
    /// <paramref name="to"/>, usando códigos de idioma como "pt", "en", "ru".
    /// </summary>
    /// <remarks>
    /// Nunca lança. Falha devolve <see cref="TranslationResult.Failed"/> com o
    /// texto original, porque frase sem traduzir é melhor que frase perdida.
    /// </remarks>
    Task<TranslationResult> TranslateAsync(
        string text,
        string from,
        string to,
        CancellationToken cancel = default);
}

/// <summary>
/// Resultado da tradução. Sempre traz texto utilizável, mesmo em falha, para
/// ninguém precisar tratar nulo.
/// </summary>
public readonly record struct TranslationResult(string Text, bool Success, string? Error = null)
{
    /// <summary>
    /// Tradução concluída.
    /// </summary>
    public static TranslationResult Ok(string text) => new(text, true);

    /// <summary>
    /// Falhou. Devolve o texto original para que a mensagem chegue mesmo assim.
    /// </summary>
    public static TranslationResult Failed(string original, string error) =>
        new(original, false, error);
}
