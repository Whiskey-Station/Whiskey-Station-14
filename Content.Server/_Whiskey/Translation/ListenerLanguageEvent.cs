namespace Content.Server._Whiskey.Translation;

/// <summary>
/// Pergunta ao ouvinte em que idioma ele quer a fala. Nulo manda como está.
/// </summary>
public sealed class ListenerLanguageEvent : EntityEventArgs
{
    public ListenerLanguageEvent(EntityUid ouvinte)
    {
        Ouvinte = ouvinte;
    }

    public EntityUid Ouvinte { get; }

    /// <summary>
    /// Idioma em que este ouvinte quer receber, ou nulo para não traduzir.
    /// </summary>
    public string? Idioma { get; set; }
}
