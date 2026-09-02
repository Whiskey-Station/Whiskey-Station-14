<!-- SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com> -->
<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->

# Pressão mental: o que é herdado e o que é novo

Escrevi este documento porque a pergunta apareceu antes da revisão e ela é
justa: a pressão mental é um sistema novo, ou é melhoria de um que já existe?

A resposta curta é que é novo em arquitetura e herdado em ideia. Nenhuma das
peças conceituais foi inventada aqui. O que é novo é o arranjo delas, e um
arranjo que muda o que o sistema entrega para quem joga.

Deixo por escrito para a discussão acontecer aqui, e não espalhada pelos
comentários de cinco PRs.

## O que já existia antes

### O humor do Einstein Engines, que está no fork

`Content.Server/_EinsteinEngines/Mood/`. É o sistema mais próximo, e ele já faz
mais coisa do que costuma ser lembrado:

- guarda as origens ativas em `CategorisedEffects` e `UncategorisedEffects`,
  então não é uma barra anônima;
- cada `MoodEffectPrototype` tem `Description`, ou seja o texto da origem já
  existe;
- cada efeito tem `Timeout` próprio, então o prazo já é por origem;
- o balanceamento já mora em prototype, e não em código.

Isso importa dizer com todas as letras: quem afirmar que a pressão "trouxe
origem nomeada para o fork" está errado. Isso já estava aqui.

### A sanidade do /tg/station

`code/datums/mood.dm`. É de onde vem a ideia de um estado mental que degrada
devagar e cobra o preço depois, separado do humor que muda na hora. O fork tem o
humor e nunca teve a sanidade.

### Paracusia e alucinação

`Content.Shared/Traits/Assorted/ParacusiaComponent.cs` e
`Content.Shared/_Whiskey/Hallucinations/`. São a prova de que sintoma isolado
por jogador já é possível aqui, e a pressão usa a mesma ideia em vez de
reinventar.

## O que a pressão faz de diferente

São três coisas, e as três saem da mesma decisão: **a origem não é um detalhe
interno, é a coisa principal.**

### 1. A origem sai para fora da pessoa

Este é o ponto que decide. O `MoodSystem` não assina `ExaminedEvent`, e dá para
conferir com um grep: o estado mental de alguém nunca chega a outro jogador.
Toda a superfície do humor é privada, entre o popup e o painel do próprio dono.

A pressão assina o examinar e mostra a origem para quem chega perto: "tem o
olhar de quem viu algo morrer há pouco tempo". Mostra a origem e não o peso, de
propósito. "Ele viu alguém morrer" é uma deixa para conversar; "pressão 34" é um
número para otimizar.

Isso é o que dá ao Psicólogo alguma coisa para fazer além de dispensar
comprimido no escuro, e é a razão de o sistema existir.

### 2. Cada origem sai no seu ritmo

O `Timeout` do humor é prazo fixo: o efeito dura N segundos e cai de uma vez,
inteiro. Na pressão cada fonte tem `Decay` próprio e escorre aos poucos, porque
as coisas saem da cabeça em ritmos diferentes. Susto passa rápido, ter visto
alguém morrer não.

Medido: a dor sai do teto até zero em 60 segundos, a morte leva 600.

Cada fonte também tem teto próprio, o que o humor não tem. Sem isso, uma fonte
fraca e repetida o suficiente empata com trauma, e a origem deixa de significar
alguma coisa.

### 3. Cada origem escolhe o canal do sintoma

No humor tudo converge para `CurrentMoodLevel`, e as consequências são sempre as
mesmas três: velocidade, limiar de crítico e shader. Uma barra única não tem
como escolher canal, porque no fim ela só sabe um número.

Na pressão a fonte declara o que causa. Ver morte aperta o cone de visão, porque
quem acabou de ver alguém morrer fica olhando para um ponto e perde o resto da
sala. Dor sai pela fala como gagueira, porque é o canal que os outros percebem
sem precisar examinar.

Há teste que reprova se duas fontes usarem o mesmo sintoma. Não é preciosismo:
se duas fontes causam a mesma coisa, a origem virou enfeite e o sistema podia
ser uma barra.

## O que ficou de fora, de propósito

**Sintoma não tira o controle do jogador.** Fechar o olho sozinho, lentidão e
desajeitado foram considerados e descartados. Estreitar a visão e gaguejar
assustam sem tirar o controle. Desajeitado tira o item da mão no meio de uma
cirurgia, que é o tipo de coisa que gera raiva e não tensão.

**Não vale para a estação inteira.** A pressão vive num traço, o Sensível, igual
ao humor que só existe na Depressão e no Alegre. Estado mental obrigatório mexe
na rodada de quem não pediu por isso, e a decisão da equipe já foi essa.

Isso limita a paridade com o /tg/station de propósito: lá todo mundo tem humor e
sanidade o tempo todo. Aqui é opt-in, e a paridade que se busca é a dos números
e do comportamento dentro do traço, não a do alcance.

## Então é novo ou não

É um sistema novo que reaproveita ideias velhas, e essa é a leitura honesta.

Se a pergunta for "isso já existia no SS13 ou no SS14", a resposta é que quase
tudo já existia em algum lugar: origem nomeada e prazo por origem no humor do
Einstein, degradação lenta na sanidade do TG, sintoma isolado por jogador na
paracusia.

Se a pergunta for "dá para chegar nisso ajustando o que já está aqui", a
resposta é não. Um `Decay` por fonte, um teto por fonte e um canal de sintoma
por fonte não cabem num componente que guarda um float e converge tudo nele. E
a superfície social, que é o motivo de o sistema existir, não é um ajuste de
número: é um evento que o humor não assina.

## Onde está o código

| PR | O que |
|---|---|
| #78 | o sistema: pressão com origem, decaimento e examinar |
| #94 | o motor: a fonte declara o sintoma que causa |
| #95 | morte aperta a visão |
| #96 | dor sai pela fala |
| #101 | o traço Sensível, que é quem sente tudo isso |

Cada PR de conteúdo se apoia na anterior, seguindo a regra 6 do regulamento de
maintainers: sistema e conteúdo não sobem juntos.

## Uma armadilha para quem for mexer no balanceamento

O `Decay` de uma fonte **não é por segundo**. É por ciclo de
`MentalPressureComponent.DecayInterval`, que hoje são cinco segundos. Ou seja
`decay: 3` tira três pontos a cada cinco segundos.

Eu já errei essa leitura uma vez e ela contaminou três coisas ao mesmo tempo sem
dar erro nenhum: um teste que media em ciclos e afirmava segundos, comentários
que publicavam durações cinco vezes menores que as reais, e um rebalanceamento
decidido em cima desses números. Está tudo corrigido, e os comentários das
fontes agora dizem a unidade na cara.
