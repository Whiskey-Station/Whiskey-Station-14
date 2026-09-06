// <Trauma>
using Content.Shared.Whitelist;
using Content.Trauma.Common.Language.Systems;
// </Trauma>
using Content.Shared.Chat;
using Content.Shared.Inventory.Events;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Radio.EntitySystems;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.Radio.EntitySystems;

public sealed partial class HeadsetSystem : SharedHeadsetSystem
{
    // <Trauma>
    [Dependency] private CommonLanguageSystem _language = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    // </Trauma>
    [Dependency] private INetManager _netMan = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private _Whiskey.Translation.TranslationSystem _translation = default!; // Whiskey

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HeadsetComponent, RadioReceiveEvent>(OnHeadsetReceive);
        SubscribeLocalEvent<HeadsetComponent, EncryptionChannelsChangedEvent>(OnKeysChanged);

        SubscribeLocalEvent<WearingHeadsetComponent, EntitySpokeEvent>(OnSpeak);
    }

    private void OnKeysChanged(EntityUid uid, HeadsetComponent component, EncryptionChannelsChangedEvent args)
    {
        UpdateRadioChannels(uid, component, args.Component);
    }

    private void UpdateRadioChannels(EntityUid uid, HeadsetComponent headset, EncryptionKeyHolderComponent? keyHolder = null)
    {
        // make sure to not add ActiveRadioComponent when headset is being deleted
        if (!headset.Enabled || MetaData(uid).EntityLifeStage >= EntityLifeStage.Terminating)
            return;

        if (!Resolve(uid, ref keyHolder))
            return;

        if (keyHolder.Channels.Count == 0)
            RemComp<ActiveRadioComponent>(uid);
        else
            EnsureComp<ActiveRadioComponent>(uid).Channels = new(keyHolder.Channels);
    }

    private void OnSpeak(EntityUid uid, WearingHeadsetComponent component, EntitySpokeEvent args)
    {
        if (args.Channel != null
            && TryComp(component.Headset, out EncryptionKeyHolderComponent? keys)
            && keys.Channels.Contains(args.Channel.ID)
            && _whitelist.IsWhitelistPassOrNull(args.Channel.SendWhitelist, uid)) // Goobstation - Whitelisted channels
        {
            _radio.SendRadioMessage(uid, args.Message, args.Channel, component.Headset);
            args.Channel = null; // prevent duplicate messages from other listeners.
        }
    }

    protected override void OnGotEquipped(EntityUid uid, HeadsetComponent component, GotEquippedEvent args)
    {
        base.OnGotEquipped(uid, component, args);
        if (component.IsEquipped && component.Enabled)
        {
            EnsureComp<WearingHeadsetComponent>(args.EquipTarget).Headset = uid;
            UpdateRadioChannels(uid, component);
        }
    }

    protected override void OnGotUnequipped(EntityUid uid, HeadsetComponent component, GotUnequippedEvent args)
    {
        base.OnGotUnequipped(uid, component, args);
        RemComp<ActiveRadioComponent>(uid);
        RemComp<WearingHeadsetComponent>(args.EquipTarget);
    }

    public void SetEnabled(EntityUid uid, bool value, HeadsetComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (component.Enabled == value)
            return;

        component.Enabled = value;
        Dirty(uid, component);

        if (!value)
        {
            RemCompDeferred<ActiveRadioComponent>(uid);

            if (component.IsEquipped)
                RemCompDeferred<WearingHeadsetComponent>(Transform(uid).ParentUid);
        }
        else if (component.IsEquipped)
        {
            EnsureComp<WearingHeadsetComponent>(Transform(uid).ParentUid).Headset = uid;
            UpdateRadioChannels(uid, component);
        }
    }

    private void OnHeadsetReceive(EntityUid uid, HeadsetComponent component, ref RadioReceiveEvent args)
    {
        // TODO: change this when a code refactor is done
        // this is currently done this way because receiving radio messages on an entity otherwise requires that entity
        // to have an ActiveRadioComponent

        var parent = Transform(uid).ParentUid;

        if (parent.IsValid())
        {
            var relayEvent = new HeadsetRadioReceiveRelayEvent(args);
            RaiseLocalEvent(parent, ref relayEvent);
        }

        if (TryComp(parent, out ActorComponent? actor))
        // <Trauma> - check if the mob understands the language and choose the message to show
        {
            var canUnderstand = _language.CanUnderstand(parent, args.Language.ID);

            // <Whiskey> - quem está com tradutor recebe o rádio no idioma dele.
            // Só quando já entenderia a fala, senão o tradutor de idioma real
            // furaria o sistema de idiomas fictícios.
            if (canUnderstand && TryEntregarTraduzido(parent, actor.PlayerSession, ref args))
                return;
            // </Whiskey>

            var msg = new MsgChatMessage
            {
                Message = canUnderstand ? args.OriginalChatMsg : args.LanguageObfuscatedChatMsg
            };
            _netMan.ServerSendMessage(msg, actor.PlayerSession.Channel);
        }
        // </Trauma>
    }

    /// <summary>
    ///     Whiskey: entrega a mensagem de rádio traduzida, se este ouvinte
    ///     estiver com tradutor.
    /// </summary>
    /// <remarks>
    ///     Devolvendo verdadeiro, quem chamou não deve mandar nada: a mensagem
    ///     chega uns três décimos depois. Só quem tem tradutor paga o atraso, e
    ///     falha de tradução não engole a fala, porque o resultado sempre
    ///     carrega o texto original dentro.
    /// </remarks>
    private bool TryEntregarTraduzido(EntityUid ouvinte, ICommonSession sessao, ref RadioReceiveEvent args)
    {
        if (args.Remontar is not { } remontar || !_translation.CanTranslate)
            return false;

        // Quem falou não recebe a própria fala traduzida de volta.
        if (ouvinte == args.MessageSource)
            return false;

        var pergunta = new _Whiskey.Translation.ListenerLanguageEvent(ouvinte);
        RaiseLocalEvent(ouvinte, pergunta);

        if (pergunta.Idioma is not { } destino)
            return false;

        var texto = args.OriginalChatMsg.Message;
        var origem = _translation.DetectarIdioma(texto);

        if (origem == destino)
            return false;

        _translation.Translate(texto, origem, destino, resultado =>
        {
            if (sessao.Status != SessionStatus.InGame)
                return;

            _netMan.ServerSendMessage(new MsgChatMessage { Message = remontar(resultado.Text) }, sessao.Channel);
        });

        return true;
    }
}
