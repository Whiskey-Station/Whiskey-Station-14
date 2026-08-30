# Naipes de cartas
rmc-playing-card-suit-spades = Espadas
rmc-playing-card-suit-hearts = Copas
rmc-playing-card-suit-diamonds = Ouros
rmc-playing-card-suit-clubs = Paus

# Valores de cartas
rmc-playing-card-rank-ace = Ás
rmc-playing-card-rank-jack = Valete
rmc-playing-card-rank-queen = Dama
rmc-playing-card-rank-king = Rei

# Exame de carta
rmc-playing-card-examine = É o {$rank} de {$suit}.
rmc-playing-card-examine-face-down = A carta está virada para baixo.
rmc-playing-card-tooltip = {$rank} de {$suit}

# Ações de carta
rmc-playing-card-flip = Você vira a carta para {$direction}.
rmc-playing-card-draw = Você compra o {$rank} de {$suit}.
rmc-playing-card-draw-hidden = Você compra uma carta.
rmc-playing-card-draw-deck = Você compra uma carta do baralho.
rmc-playing-card-add-to-hand = Você adiciona a carta à mão. ({$count} cartas no total)

# Exame de baralho
rmc-playing-card-deck-examine = O baralho tem {$count} cartas restantes.
rmc-playing-card-deck-examine-verb = Controles
rmc-playing-card-deck-examine-verb-message = Ver controles do baralho.
rmc-playing-card-deck-examine-shuffle = [bold][color=cyan]Alt[/color]+[color=yellow]Ativar[/color][/bold] — Embaralha o baralho.
rmc-playing-card-deck-examine-draw = [bold][color=yellow]Ativar[/color] (E) ou [color=yellow]Ativar na mão[/color] (Z)[/bold] — Compra uma carta.
rmc-playing-card-deck-examine-pickup = [bold][color=cyan]Na mão e clicar no chão vazio[/color][/bold] — Recolhe cartas próximas.

# Ações de baralho
rmc-playing-card-deck-empty = O baralho está vazio!
rmc-playing-card-deck-full = O baralho está cheio!
rmc-playing-card-hand-full = A mão está cheia!
rmc-playing-card-deck-shuffle = Você embaralha {THE($deck)}.
rmc-playing-card-added-to-deck = Você adiciona a carta ao baralho.
rmc-playing-card-added-cards-to-deck = Você adiciona {$count} cartas ao baralho.
rmc-playing-card-deck-pickup = Você recolhe {$count} cartas para o baralho.
rmc-playing-card-draw-multiple = Você compra { $count ->
    [one] 1 carta
   *[other] {$count} cartas
}.

# Mão de cartas
rmc-playing-card-hand-name = mão de cartas
rmc-playing-card-stack-name = pilha de cartas
rmc-playing-card-hand-examine = Uma mão de {$count} cartas.
rmc-playing-card-hand-examine-hidden = Uma mão de {$count} cartas, virada para baixo.
rmc-playing-card-hand-card = - {$rank} de {$suit}

# Exame de mão
rmc-playing-card-hand-examine-verb = Controles
rmc-playing-card-hand-examine-verb-message = Ver controles da mão.
rmc-playing-card-hand-examine-face-down = [bold][color=cyan]Virada para baixo[/color] [color=yellow]E[/color] ou [color=yellow]Z[/color][/bold] — Compra uma carta do topo.
rmc-playing-card-hand-examine-face-up = [bold][color=cyan]Virada para cima[/color] [color=yellow]E[/color] ou [color=yellow]Z[/color][/bold] — Abre a mão para escolher uma carta específica.
rmc-playing-card-hand-examine-flip = [bold][color=cyan]Virar[/color] [color=yellow]ALT + Ativar[/color][/bold] — Vira as cartas.

# Ações de mão
rmc-playing-card-hand-flip = Você vira a mão para {$direction}.
rmc-playing-card-hand-shuffle = Você embaralha {THE($hand)}.
rmc-playing-card-hand-empty = A mão está vazia!
rmc-playing-card-merge-hands = Você junta as mãos. ({$count} cartas no total)

# Verbos
rmc-playing-card-verb-flip = Virar
rmc-playing-card-verb-category-draw = Comprar
rmc-playing-card-verb-draw = Comprar carta
rmc-playing-card-verb-draw-5 = Comprar 5
rmc-playing-card-verb-draw-half = Comprar metade
rmc-playing-card-verb-draw-all = Comprar todas
rmc-playing-card-verb-pick = Escolher carta
rmc-playing-card-verb-shuffle = Embaralhar

# Nomes de entidades
ent-RMCPlayingCardBase = Carta de baralho
ent-RMCPlayingCard = Carta de baralho
ent-RMCPlayingCardHand = Mão de cartas
ent-RMCPlayingCardDeck = Baralho de cartas
desc-RMCPlayingCardBase = Uma carta de baralho padrão.
desc-RMCPlayingCard = Uma carta de baralho padrão.
desc-RMCPlayingCardHand = Uma mão de cartas de baralho.
desc-RMCPlayingCardDeck = Um baralho padrão de 52 cartas de baralho.
