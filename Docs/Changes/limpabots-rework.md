<!-- SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com> -->
<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->

# Rework dos limpabots: análise e contraproposta de escopo

A Punkzebu propôs transformar os limpabots numa equipe com IA: cones antes de
limpar, reclamação de quem pisa no molhado, conversa entre bots, caça a pragas
em grupo, memória de quem ajuda ou atrapalha, e uma salinha onde eles ficam
tomando chá quando não há tarefa. Veio com arte pronta e um rascunho detalhado.

**A ideia é boa e eu quero ela no servidor.** É exatamente o tipo de coisa que
dá identidade a um fork, e limpabot com personalidade é memorável de um jeito
que mais um sistema de combate nunca vai ser.

O que eu não aprovo é o tamanho da entrega de uma vez. Este documento explica
por quê, com os números, e propõe como cortar.

## O que existe hoje

O limpabot atual é um arquivo de 40 linhas em `Resources/Prototypes/NPCs/cleanbot.yml`.
Ele acha uma poça, anda até lá e interage. Só isso.

Ele **não fala nada** hoje, e é **fabricado** por construction graph, não spawna
sozinho. Ou seja a quantidade em jogo não tem teto: depende de quem decide
montar, e nada impede alguém de montar dez.

## O escopo proposto, em números

| | Hoje | Proposta |
|---|---|---|
| Arquivos de definição | 1 YAML, 40 linhas | 14 componentes, 13 sistemas |
| Falas | 0 | 350 |

Para dimensionar: **o jogo inteiro tem 21 arquivos de NPC**. A proposta cria 13
sistemas só para o limpabot. É maior que o sistema de pressão mental inteiro.

Isso esbarra na regra 6 do regulamento por um motivo prático, não burocrático:
PR desse tamanho não é revisada de verdade, é aprovada no olho. E se o
balanceamento das falas ficar chato em jogo, reverter significa derrubar os 13
sistemas juntos, inclusive as partes que estavam boas.

## Três coisas para acertar antes de começar

### A arquitetura citada não é a que o fork usa

O rascunho fala em "behavior tree/utility AI". O SS14 **não tem behavior tree**.
A IA de NPC aqui é HTN, em `Content.Server/NPC/HTN`, e o utility existe como
operador dentro dela: o próprio limpabot já usa `UtilityOperator` com
`NearbyPuddles`.

Isso não é detalhe de nomenclatura. Behavior tree e HTN organizam decisão de
formas diferentes, e a estrutura de componentes do rascunho foi desenhada para a
primeira. Vale refazer esse pedaço em cima do HTN antes de escrever código.

### O rascunho foi gerado em bloco, e os detalhes não foram conferidos

São 350 falas com apenas **65 linhas de `obs` distintas**, e as categorias vêm em
blocos de exatamente 25. A condição de disparo foi colada por bloco, e em alguns
casos ela contradiz o gatilho da própria fala. Exemplo:

```
[012] "Licença." | "Concedida."
  -> quando: Bots se cruzando em corredor estreito.
  obs: Habilitar somente se CurrentTask == null...
```

Bots se cruzam justamente quando estão ocupados indo fazer tarefa. Com essa
condição, essa fala nunca dispara em jogo.

Isso não invalida o rascunho, que é um bom ponto de partida. Só quer dizer que
as condições precisam de uma passada individual antes de virarem código, senão a
gente implementa 350 falas e descobre em jogo que metade não acontece.

### O maior risco não é técnico, é o chat

Bot conversando entre si polui o canal principal do jogo. É o jeito mais rápido
de a comunidade odiar uma feature que, no papel, todo mundo achou legal.

O rascunho já traz regras anti-spam, e boas: 30 a 60 segundos entre falas, não
repetir a mesma seguidas, cancelar conversa em perigo. Isso mostra que o risco
foi considerado. Mas como a quantidade de bots não tem teto, essas regras
precisam ser medidas com vários bots na estação, e não com dois.

## O caminho que eu proponho

Fatiar em fases, cada uma revisável e reversível sozinha. A ordem começa pelo que
o jogador percebe e termina no que é só charme.

**Fase 1, o cone.** Colocar cone antes de limpar, recolher depois, e reclamar de
quem pisa no molhado ou rouba o cone. Poucas falas, sem memória, sem rede entre
bots. É a fatia que interage com jogador de verdade e prova o valor da ideia
inteira.

**Fase 2, a rede.** Bots compartilham setor, tarefa e perigo, e chamam ajuda em
incidente grande. Aqui entra o comportamento de equipe, que é o coração da
proposta.

**Fase 3, personalidade e memória.** Modificadores que mudam falas e
prioridades, e memória de quem ajuda ou atrapalha.

**Fase 4, a salinha.** Dock, mesa, o chá, a conversa em idle. É a parte mais
querida da proposta e a que menos risco tem, porque acontece longe do jogador.

## O que decide se cada fase continua

Depois da fase 1, medir em jogo com pelo menos cinco bots ativos ao mesmo tempo,
que é o cenário ruim e plausível:

- quantas linhas de bot aparecem no chat por minuto;
- se dá para acompanhar a conversa dos jogadores com os bots falando junto;
- o custo do tique do servidor com os bots ativos, comparado com hoje.

Se o chat ficar ilegível na fase 1, que é a de menos falas, as fases seguintes
mudam de desenho antes de existirem. Melhor descobrir isso com um sistema no ar
do que com treze.

## Resumo

Aprovo a ideia e o rascunho como base. O que eu peço antes de virar código é
refazer a parte de arquitetura em cima do HTN, dar uma passada nas condições das
falas, e entregar pela fase 1 em vez de tudo de uma vez.
