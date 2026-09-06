using System.Linq;
using Content.Server._Whiskey.Translation;
using Content.Shared.Chat;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.PowerCell;
using Content.Trauma.Shared.Language.Components.Translators;
using Robust.Shared.Containers;

namespace Content.Trauma.Server._Whiskey.Translation;

/// <summary>
/// Traduz a fala de quem está com um tradutor de verdade ligado. Engole o
/// original e reenvia pelo caminho normal do chat uns 300ms depois, então a
/// frase ainda passa por sotaque, alcance e log.
/// </summary>
/// <remarks>
/// Só fala local: rádio já retornou antes deste ponto, e traduzir junto
/// quebraria o prefixo de canal.
/// </remarks>
public sealed partial class AutoTranslateSystem : EntitySystem
{
    [Dependency] private TranslationSystem _translation = default!;
    [Dependency] private PowerCellSystem _powerCell = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    /// <summary>
    /// Ligado enquanto a frase já traduzida está sendo reenviada.
    /// </summary>
    /// <remarks>
    /// Sem isto o reenvio seria interceptado de novo, traduzido de novo, e o
    /// servidor entraria em laço até cair. Um campo simples basta porque o
    /// reenvio acontece dentro do <c>Update</c>, na thread do jogo, e termina
    /// antes de qualquer outra fala ser processada.
    /// </remarks>
    private bool _reemitindo;

    public override void Initialize()
    {
        base.Initialize();

        // Só quem tem tradutor é perguntado. Assinar por componente em vez de
        // ouvir toda fala da estação faz o custo ser zero para quem não usa,
        // que é a maioria esmagadora.
        SubscribeLocalEvent<HoldsTranslatorComponent, SpeechInterceptEvent>(OnFalaSeguraTradutor);
        SubscribeLocalEvent<IntrinsicTranslatorComponent, SpeechInterceptEvent>(OnFalaTradutorInterno);

        SubscribeLocalEvent<HoldsTranslatorComponent, ListenerLanguageEvent>(OnOuvinteSeguraTradutor);
        SubscribeLocalEvent<IntrinsicTranslatorComponent, ListenerLanguageEvent>(OnOuvinteTradutorInterno);
    }

    private void OnOuvinteSeguraTradutor(EntityUid uid, HoldsTranslatorComponent comp, ListenerLanguageEvent args)
    {
        Responder(args);
    }

    private void OnOuvinteTradutorInterno(EntityUid uid, IntrinsicTranslatorComponent comp, ListenerLanguageEvent args)
    {
        Responder(args);
    }

    /// <summary>
    /// Diz em que idioma este ouvinte quer receber a fala.
    /// </summary>
    /// <remarks>
    /// É o mesmo tradutor que cuida dos dois lados, então o idioma é o mesmo
    /// que ele usa para falar. O russo com o tradutor de russo fala e escuta em
    /// russo, e é por isso que um item só resolve as duas direções.
    /// </remarks>
    private void Responder(ListenerLanguageEvent args)
    {
        // Quem tem tradutor na mão e implante ao mesmo tempo recebe o evento
        // duas vezes.
        if (args.Idioma != null)
            return;

        if (TryIdiomaDoTradutor(args.Ouvinte, out var idioma))
            args.Idioma = idioma;
    }

    private void OnFalaSeguraTradutor(EntityUid uid, HoldsTranslatorComponent comp, SpeechInterceptEvent args)
    {
        Tentar(args);
    }

    private void OnFalaTradutorInterno(EntityUid uid, IntrinsicTranslatorComponent comp, SpeechInterceptEvent args)
    {
        Tentar(args);
    }

    private void Tentar(SpeechInterceptEvent args)
    {
        // Quem tem tradutor na mão e implante ao mesmo tempo recebe o evento
        // duas vezes. Sem esta guarda a frase seria traduzida e entregue duas
        // vezes, e o jogador falaria em dobro.
        if (args.Interceptado)
            return;

        if (TryInterceptar(args.Falante, args.Mensagem, args.Tipo, args.Reenviar))
            args.Interceptado = true;
    }

    /// <summary>
    /// Decide se esta fala vai ser traduzida. Devolvendo verdadeiro, quem
    /// chamou precisa parar: a frase será entregue depois, por
    /// <paramref name="reenviar"/>.
    /// </summary>
    private bool TryInterceptar(
        EntityUid falante,
        string mensagem,
        InGameICChatType tipo,
        Action<string> reenviar)
    {
        if (_reemitindo)
            return false;

        // Emote descreve ação, não fala, então traduzir estragaria.
        if (tipo != InGameICChatType.Speak && tipo != InGameICChatType.Whisper)
            return false;

        if (!_translation.CanTranslate)
            return false;

        if (!TryIdiomaDoTradutor(falante, out var idiomaDono))
            return false;

        var destino = _translation.IdiomaDaEstacao;

        // Quem já fala o idioma da estação não precisa de tradução na saída. O
        // tradutor dele continua servindo para o outro lado, o de escutar.
        if (idiomaDono == destino)
            return false;

        // Trava para o caso do dono arriscar falar outro idioma: sem isso a
        // frase já certa seria traduzida de novo e estragada.
        //
        // Só vale quando a detecção tem certeza. Em dúvida ela devolve falso, e
        // aí seguimos com o idioma configurado no tradutor, que é a melhor
        // informação que existe sobre quem está falando.
        if (_translation.TryDetectarIdioma(mensagem, out var detectado) && detectado != idiomaDono)
            return false;

        _translation.Translate(mensagem, idiomaDono, destino, resultado =>
        {
            if (Deleted(falante))
                return;

            // Em caso de falha o resultado carrega o texto original, então a
            // frase sai mesmo assim, sem tradução.
            _reemitindo = true;
            try
            {
                reenviar(resultado.Text);
            }
            finally
            {
                _reemitindo = false;
            }
        });

        return true;
    }

    /// <summary>
    /// Descobre o idioma do tradutor ativo desta entidade, se houver algum.
    /// </summary>
    /// <remarks>
    /// As condições de "ativo" são as mesmas que o <c>TranslatorSystem</c> do
    /// Trauma usa para os idiomas fictícios, de propósito: se o tradutor está
    /// apagado, sem célula ou não está mais na mão, ele tem que parar de
    /// funcionar aqui pelo mesmo motivo e no mesmo instante. Duas noções
    /// diferentes de "ligado" no mesmo item seria bug garantido.
    /// </remarks>
    public bool TryIdiomaDoTradutor(EntityUid entidade, out string idioma)
    {
        idioma = string.Empty;

        // Implante e tradutor intrínseco moram na própria entidade.
        if (TryComp<RealTranslatorComponent>(entidade, out var interno)
            && TryComp<IntrinsicTranslatorComponent>(entidade, out var intrinseco)
            && intrinseco.Enabled
            && intrinseco.LifeStage < ComponentLifeStage.Removing
            && _powerCell.HasActivatableCharge(entidade))
        {
            idioma = interno.Idioma;
            return true;
        }

        // O de mão é alcançado pelo componente que o fork já mantém em quem
        // está segurando, e que também cobre o que está vestido no pescoço.
        if (!TryComp<HoldsTranslatorComponent>(entidade, out var segura))
            return false;

        // Duas passadas, e a ordem é a regra: quem está com dois tradutores
        // ligados, um vestido e outro na mão, quis usar o da mão. Sem esta
        // ordem a escolha sairia da ordem interna de um HashSet, que não tem
        // garantia nenhuma, e a pessoa falaria russo numa frase e inglês na
        // seguinte sem entender o motivo.
        foreach (var naMao in new[] { true, false })
        {
            foreach (var (tradutor, comp) in segura.Translators.ToArray())
            {
                if (_hands.IsHolding(entidade, tradutor) != naMao)
                    continue;

                if (!comp.Enabled || !_powerCell.HasActivatableCharge(tradutor))
                    continue;

                if (!_containers.TryGetContainingContainer(tradutor, out var recipiente)
                    || recipiente.Owner != entidade)
                    continue;

                if (!TryComp<RealTranslatorComponent>(tradutor, out var real))
                    continue;

                idioma = real.Idioma;
                return true;
            }
        }

        return false;
    }
}
