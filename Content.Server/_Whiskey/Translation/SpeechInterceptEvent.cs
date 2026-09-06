using Content.Shared.Chat;

namespace Content.Server._Whiskey.Translation;

/// <summary>
/// Oferece a fala local para alguém segurar e entregar depois. Quem intercepta
/// assume a entrega: marca <see cref="Interceptado"/> e tem que chamar
/// <see cref="Reenviar"/>, senão a fala se perde.
/// </summary>
public sealed class SpeechInterceptEvent : EntityEventArgs
{
    public SpeechInterceptEvent(EntityUid falante, string mensagem, InGameICChatType tipo, Action<string> reenviar)
    {
        Falante = falante;
        Mensagem = mensagem;
        Tipo = tipo;
        Reenviar = reenviar;
    }

    public EntityUid Falante { get; }

    public string Mensagem { get; }

    public InGameICChatType Tipo { get; }

    /// <summary>
    /// Entrega a frase, já tratada, pelo caminho normal do chat.
    /// </summary>
    public Action<string> Reenviar { get; }

    /// <summary>
    /// Marcado por quem assumiu a entrega. Verdadeiro faz o <c>ChatSystem</c>
    /// parar aqui.
    /// </summary>
    public bool Interceptado { get; set; }
}
