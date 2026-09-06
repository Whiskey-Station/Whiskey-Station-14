using System.Collections.Generic;
using System.Linq;
using System.Text;
using Content.Server.Radio.EntitySystems;
using Content.Shared.Chat;
using Robust.Shared.Timing;

namespace Content.Server._Whiskey.Translation;

/// <summary>
/// Mede volume de fala por idioma para dimensionar custo de tradução. Só
/// escuta e loga, não altera nada.
/// </summary>
/// <remarks>
/// Sussurro não é contado: o <c>IsWhisper</c> chega sempre falso neste fork, e
/// contador que só marca zero engana mais que a ausência dele.
/// </remarks>
public sealed partial class SpeechVolumeMeterSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>
    /// Intervalo entre resumos no log. Dez minutos dá amostra suficiente sem
    /// encher o log de um servidor que roda a noite toda.
    /// </summary>
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMinutes(10);

    private sealed class LanguageTally
    {
        public int Messages;
        public int Characters;
        public int RadioMessages;
    }

    private readonly Dictionary<string, LanguageTally> _tallies = new();

    private TimeSpan _windowStart;
    private TimeSpan _nextReport;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = Logger.GetSawmill("whiskey.speech_meter");

        // Antes do HeadsetSystem de proposito: ele zera args.Channel logo depois
        // de enviar a mensagem pelo radio, para outros ouvintes nao duplicarem.
        // Se o contador rodasse depois, toda fala de radio chegaria aqui como
        // fala local e a contagem de radio ficaria sempre em zero.
        SubscribeLocalEvent<EntitySpokeEvent>(OnEntitySpoke, before: new[] { typeof(HeadsetSystem) });

        _windowStart = _timing.CurTime;
        _nextReport = _windowStart + ReportInterval;
    }

    private void OnEntitySpoke(EntitySpokeEvent args)
    {
        // O idioma nunca é nulo aqui: o ChatSystem sempre resolve um antes de
        // disparar o evento, caindo no Universal quando não há outro.
        var id = args.Language.ID;

        if (!_tallies.TryGetValue(id, out var tally))
        {
            tally = new LanguageTally();
            _tallies[id] = tally;
        }

        tally.Messages++;
        tally.Characters += args.Message.Length;


        if (args.Channel != null)
            tally.RadioMessages++;
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextReport)
            return;

        Report();

        _tallies.Clear();
        _windowStart = _timing.CurTime;
        _nextReport = _windowStart + ReportInterval;
    }

    /// <summary>
    /// Escreve o resumo da janela atual. Público para que um comando de admin
    /// possa pedir o relatório sem esperar o intervalo.
    /// </summary>
    public void Report()
    {
        var minutes = (_timing.CurTime - _windowStart).TotalMinutes;
        if (minutes <= 0)
            return;

        if (_tallies.Count == 0)
        {
            _sawmill.Info($"Nenhuma fala nos ultimos {minutes:F1} minutos.");
            return;
        }

        var totalMessages = _tallies.Values.Sum(t => t.Messages);
        var totalChars = _tallies.Values.Sum(t => t.Characters);

        var linha = new StringBuilder();
        linha.Append($"Janela de {minutes:F1} min: {totalMessages} mensagens ");
        linha.Append($"({totalMessages / minutes:F1}/min), {totalChars} caracteres ");
        linha.Append($"({totalChars / minutes:F0}/min). Por idioma: ");

        foreach (var (id, tally) in _tallies.OrderByDescending(p => p.Value.Messages))
        {
            linha.Append($"{id}={tally.Messages} msg/{tally.Characters} car");
            if (tally.RadioMessages > 0)
                linha.Append($"/{tally.RadioMessages} radio");
            linha.Append("; ");
        }

        _sawmill.Info(linha.ToString());
    }
}
