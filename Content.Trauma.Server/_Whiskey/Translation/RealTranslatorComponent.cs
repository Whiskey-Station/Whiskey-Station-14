namespace Content.Trauma.Server._Whiskey.Translation;

/// <summary>
/// Traduz de verdade em vez de embaralhar sílaba. Acompanha o
/// <c>HandheldTranslator</c> e companhia, não substitui.
/// </summary>
[RegisterComponent]
public sealed partial class RealTranslatorComponent : Component
{
    /// <summary>
    /// Idioma DO DONO, não o de destino: "ru" faz o russo falar estação e ouvir russo.
    /// </summary>
    [DataField]
    public string Idioma = "ru";
}
