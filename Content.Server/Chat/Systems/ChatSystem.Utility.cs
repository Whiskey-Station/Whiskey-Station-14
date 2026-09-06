// <Trauma>
using Content.Shared.Speech;
using Content.Trauma.Common.Chat;
using Content.Trauma.Common.Language;
// </Trauma>
using System.Linq;
using System.Text;
using Content.Shared.Chat;
using Content.Shared.Ghost.Components;
using Content.Shared.Players;
using Robust.Shared.Console;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server.Chat.Systems;

public sealed partial class ChatSystem
{
    private enum MessageRangeCheckResult
    {
        Disallowed,
        HideChat,
        Full
    }

    /// <summary>
    ///     If hideChat should be set as far as replays are concerned.
    /// </summary>
    private bool MessageRangeHideChatForReplay(ChatTransmitRange range)
    {
        return range == ChatTransmitRange.HideChat;
    }

    /// <summary>
    ///     Checks if a target as returned from GetRecipients should receive the message.
    ///     Keep in mind data.Range is -1 for out of range observers.
    /// </summary>
    private MessageRangeCheckResult MessageRangeCheck(ICommonSession session, ICChatRecipientData data, ChatTransmitRange range)
    {
        var initialResult = MessageRangeCheckResult.Full;
        switch (range)
        {
            case ChatTransmitRange.Normal:
                initialResult = MessageRangeCheckResult.Full;
                break;
            case ChatTransmitRange.GhostRangeLimit:
                initialResult = (data.Observer && data.Range < 0 && !_adminManager.IsAdmin(session)) ? MessageRangeCheckResult.HideChat : MessageRangeCheckResult.Full;
                break;
            case ChatTransmitRange.HideChat:
                initialResult = MessageRangeCheckResult.HideChat;
                break;
            case ChatTransmitRange.NoGhosts:
                initialResult = (data.Observer && !_adminManager.IsAdmin(session)) ? MessageRangeCheckResult.Disallowed : MessageRangeCheckResult.Full;
                break;
        }
        var insistHideChat = data.HideChatOverride ?? false;
        var insistNoHideChat = !(data.HideChatOverride ?? true);
        if (insistHideChat && initialResult == MessageRangeCheckResult.Full)
            return MessageRangeCheckResult.HideChat;
        if (insistNoHideChat && initialResult == MessageRangeCheckResult.HideChat)
            return MessageRangeCheckResult.Full;
        return initialResult;
    }

    /// <summary>
    ///     Sends a chat message to the given players in range of the source entity.
    /// </summary>
    // Trauma - added name and obfuscated strings
    private void SendInVoiceRange(ChatChannel channel, string name, string message, string wrappedMessage, string obfuscated, string obfuscatedWrappedMessage, EntityUid source, ChatTransmitRange range, NetUserId? author = null,
        LanguagePrototype? languageOverride = null, bool checkLOS = false, SpeechVerbPrototype? speech = null, Color? colorOverride = null,
        string? speechStyleClass = null) // Whiskey - CMSS runechat
    {
        var language = languageOverride ?? _language.GetLanguage(source); // Trauma

        // <Whiskey> - ouvintes que querem tradução, agrupados por idioma de
        // destino. Agrupar importa: cinco russos na mesma sala pedem a mesma
        // tradução do mesmo texto, e traduzir uma vez por ouvinte multiplicaria
        // o custo pelo número de gente sem nenhum ganho.
        Dictionary<string, List<(ICommonSession Sessao, bool EsconderChat)>>? porIdioma = null;
        // </Whiskey>

        foreach (var (session, data) in GetRecipients(source, VoiceRange))
        {
            var entRange = MessageRangeCheck(session, data, range);
            if (entRange == MessageRangeCheckResult.Disallowed)
                continue;
            var entHideChat = entRange == MessageRangeCheckResult.HideChat;
            // <Trauma> - completely different send logic and LOS check
            if (session.AttachedEntity is not { Valid: true } playerEntity)
                continue;
            if (checkLOS && !data.Observer && !data.InLOS)
                continue; // Some things don't go through walls, but they can go through windows!
            EntityUid listener = session.AttachedEntity.Value;

            // Raises a event for the deaf component
            var ev = new ChatMessageOverrideInVoiceRangeEvent(source, name, language.ID, speech, colorOverride, obfuscated, obfuscatedWrappedMessage);
            RaiseLocalEvent(listener, ref ev);
            if (channel == ChatChannel.Local
                && language.SpeechOverride.RequireSpeech // Check for whether speech is required.
                && ev.Cancelled)
                continue;

            // If the channel does not support languages, or the entity can understand the message, send the original message, otherwise send the obfuscated version
            if (channel == ChatChannel.LOOC || channel == ChatChannel.Emotes || _language.CanUnderstand(listener, language.ID))
            {
                // <Whiskey> - quem está com tradutor recebe no idioma dele.
                // Dentro deste ramo de propósito: só quem já entenderia a fala
                // recebe tradução. Quem não entende o idioma do jogo continua
                // recebendo a versão embaralhada, senão o tradutor de idioma
                // real furaria o sistema de idiomas fictícios.
                if (TryIdiomaDoOuvinte(listener, source, channel, message, speech, out var destino))
                {
                    porIdioma ??= new Dictionary<string, List<(ICommonSession, bool)>>();

                    if (!porIdioma.TryGetValue(destino, out var fila))
                        porIdioma[destino] = fila = new List<(ICommonSession, bool)>();

                    fila.Add((session, entHideChat));
                    continue;
                }
                // </Whiskey>

                _chatManager.ChatMessageToOne(channel, message, wrappedMessage, source, entHideChat, session.Channel, author: author, speechStyleClass: speechStyleClass); // Whiskey - CMSS runechat
            }
            else
                _chatManager.ChatMessageToOne(channel, ev.Message, ev.WrappedMessage, source, entHideChat, session.Channel, author: author, speechStyleClass: speechStyleClass); // Whiskey - CMSS runechat
            // </Trauma>
        }

        // <Whiskey> - uma tradução por idioma presente, e não por ouvinte.
        if (porIdioma != null && speech != null)
        {
            foreach (var (destino, fila) in porIdioma)
                EntregarTraduzido(destino, fila, channel, source, name, message, speech, language, colorOverride, author, speechStyleClass); // Whiskey - CMSS runechat
        }
        // </Whiskey>

        _replay.RecordServerMessage(new ChatMessage(channel, message, wrappedMessage, GetNetEntity(source), null, MessageRangeHideChatForReplay(range), speechStyleClass: speechStyleClass)); // Whiskey - CMSS runechat
    }

    /// <summary>
    ///     Whiskey: entrega a fala traduzida a quem tem tradutor. Verdadeiro
    ///     significa que quem chamou não manda nada, a mensagem chega depois.
    /// </summary>
    private bool TryIdiomaDoOuvinte(
        EntityUid listener,
        EntityUid source,
        ChatChannel channel,
        string message,
        SpeechVerbPrototype? speech,
        out string destino)
    {
        destino = string.Empty;

        // Sem verbo de fala não dá para remontar o "Fulano diz", e é o caso de
        // canal que não é fala, onde traduzir não faria sentido de qualquer
        // jeito.
        if (channel != ChatChannel.Local || speech == null || !_translation.CanTranslate)
            return false;

        // Quem falou não recebe a própria fala traduzida de volta. Se a fala
        // dele já foi traduzida na saída, traduzir de novo aqui seria ida e
        // volta pelo modelo, que estraga a frase e ainda mostra para ele uma
        // versão diferente da que ele digitou.
        if (listener == source)
            return false;

        var pergunta = new _Whiskey.Translation.ListenerLanguageEvent(listener);
        RaiseLocalEvent(listener, pergunta);

        if (pergunta.Idioma is not { } idioma)
            return false;

        if (_translation.DetectarIdioma(message) == idioma)
            return false;

        destino = idioma;
        return true;
    }

    private void EntregarTraduzido(
        string destino,
        List<(ICommonSession Sessao, bool EsconderChat)> fila,
        ChatChannel channel,
        EntityUid source,
        string name,
        string message,
        SpeechVerbPrototype speech,
        LanguagePrototype language,
        Color? colorOverride,
        NetUserId? author,
        string? speechStyleClass) // Whiskey - CMSS runechat
    {
        var origem = _translation.DetectarIdioma(message);

        _translation.Translate(message, origem, destino, resultado =>
        {
            var embrulhada = WrapPublicMessage(source, name, resultado.Text, speech, language, colorOverride);

            foreach (var (sessao, esconderChat) in fila)
            {
                // A resposta chega décimos depois, e nesse meio tempo o jogador
                // pode ter saído.
                if (sessao.Status != SessionStatus.InGame)
                    continue;

                _chatManager.ChatMessageToOne(channel, resultado.Text, embrulhada, source, esconderChat, sessao.Channel, author: author, speechStyleClass: speechStyleClass); // Whiskey - CMSS runechat
            }
        });
    }

    /// <summary>
    ///     Returns true if the given player is 'allowed' to send the given message, false otherwise.
    /// </summary>
    private bool CanSendInGame(string message, IConsoleShell? shell = null, ICommonSession? player = null)
    {
        // Non-players don't have to worry about these restrictions.
        if (player == null)
            return true;

        var mindContainerComponent = player.ContentData()?.Mind;

        if (mindContainerComponent == null)
        {
            shell?.WriteError("You don't have a mind!");
            return false;
        }

        if (player.AttachedEntity is not { Valid: true } _)
        {
            shell?.WriteError("You don't have an entity!");
            return false;
        }

        // <Trauma>
        var attemptEv = new PlayerMessageAttemptEvent(player, message);
        RaiseLocalEvent(ref attemptEv);
        if (attemptEv.Cancelled)
            return false;
        // </Trauma>

        return !_chatManager.MessageCharacterLimit(player, message);
    }

    // ReSharper disable once InconsistentNaming
    private string SanitizeInGameICMessage(EntityUid source, string message, out string? emoteStr, bool capitalize = true, bool punctuate = false, bool capitalizeTheWordI = true)
    {
        var newMessage = SanitizeMessageReplaceWords(message.Trim());

        GetRadioKeycodePrefix(source, newMessage, out newMessage, out var prefix);

        // Sanitize it first as it might change the word order
        _sanitizer.TrySanitizeEmoteShorthands(newMessage, source, out newMessage, out emoteStr);

        if (capitalize)
            newMessage = SanitizeMessageCapital(newMessage);
        if (capitalizeTheWordI)
            newMessage = SanitizeMessageCapitalizeTheWordI(newMessage, "i");
        if (punctuate)
            newMessage = SanitizeMessagePeriod(newMessage);

        return prefix + newMessage;
    }

    private string SanitizeInGameOOCMessage(string message)
    {
        var newMessage = message.Trim();
        newMessage = FormattedMessage.EscapeText(newMessage);

        return newMessage;
    }

    public string TransformSpeech(EntityUid sender, string message,
        LanguagePrototype language) // Trauma
    {
        // <Trauma> - Do not apply speech accents if there's no speech involved.
        if (!language.SpeechOverride.RequireSpeech)
            return message;
        // </Trauma>

        var ev = new TransformSpeechEvent(sender, message);
        RaiseLocalEvent(sender, ev, true);

        return ev.Message;
    }

    public bool CheckIgnoreSpeechBlocker(EntityUid sender, bool ignoreBlocker)
    {
        if (ignoreBlocker)
            return ignoreBlocker;

        var ev = new CheckIgnoreSpeechBlockerEvent(sender, ignoreBlocker);
        RaiseLocalEvent(sender, ev, true);

        return ev.IgnoreBlocker;
    }

    private IEnumerable<INetChannel> GetDeadChatClients()
    {
        // <Trauma>
        if (_ghostVisibility.GhostsVisible())
            return Filter.Broadcast().Recipients.Select(p => p.Channel);
        // </Trauma>

        return Filter.Empty()
            .AddWhereAttachedEntity(HasComp<GhostComponent>)
            .AddWhereAttachedEntity(_scrying.IsScryingOrbEquipped) // Trauma
            .Recipients
            .Union(_adminManager.ActiveAdmins)
            .Select(p => p.Channel);
    }

    private string SanitizeMessagePeriod(string message)
    {
        if (string.IsNullOrEmpty(message))
            return message;
        // Adds a period if the last character is a letter.
        if (char.IsLetter(message[^1]))
            message += ".";
        return message;
    }

    // Whiskey: automatic word rewriting is intentionally disabled. These replacements are driven
    // by localized keys, so a translated trigger can silently turn ordinary words such as "eu",
    // "você" and "sim" into unrelated phrases for every IC message.
    public string SanitizeMessageReplaceWords(string message) => message;

    /// <summary>
    ///     Returns list of players and ranges for all players withing some range. Also returns observers with a range of -1.
    /// </summary>
    private Dictionary<ICommonSession, ICChatRecipientData> GetRecipients(EntityUid source, float voiceGetRange)
    {
        // TODO proper speech occlusion

        var recipients = new Dictionary<ICommonSession, ICChatRecipientData>();

        var transformSource = Transform(source);
        var sourceMapId = transformSource.MapID;
        var sourceCoords = transformSource.Coordinates;

        foreach (var player in _playerManager.Sessions)
        {
            if (player.AttachedEntity is not { Valid: true } playerEntity)
                continue;

            var transformEntity = Transform(playerEntity);

            if (transformEntity.MapID != sourceMapId)
                continue;

            var observer = _ghostHearingQuery.HasComponent(playerEntity);
            // <Trauma> - seperate out range check for LOS check to use it
            sourceCoords.TryDistance(EntityManager, transformEntity.Coordinates, out var distance);

            // InRangeUnOccluded does this check, but it also checks for occlusion
            // which doesn't really work for modes that are supposed to go through walls, like Speak
            var inRange = distance <= voiceGetRange;

            var isVisible = observer || (inRange && _examineSystem.InRangeUnOccluded(source, playerEntity, voiceGetRange));

            // even if they are a ghost hearer, in some situations we still need the range
            if (inRange)
            {
                recipients.Add(player, new ICChatRecipientData(distance, observer, InLOS: isVisible));
                continue;
            }

            if (observer)
                recipients.Add(player, new ICChatRecipientData(-1, true, InLOS: isVisible));
            // <Trauma>
        }

        RaiseLocalEvent(new ExpandICChatRecipientsEvent(source, voiceGetRange, recipients));
        return recipients;
    }

    public readonly record struct ICChatRecipientData(float Range, bool Observer, bool? HideChatOverride = null,
        bool InLOS = true) // Trauma
    {
    }

    // Trauma - moved ObfuscateMessageReadability to shared

    public string BuildGibberishString(IReadOnlyList<char> charOptions, int length)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < length; i++)
        {
            sb.Append(_random.Pick(charOptions));
        }
        return sb.ToString();
    }
}
