# Regulamento de Maintainers

Documento interno de responsabilidades, revisão, engenharia, conteúdo e manutenção da Whiskey Station 14.

Aplica-se a Maintainers, ao Lead Maintainer e a qualquer pessoa com permissão elevada no repositório. A leitura é obrigatória.

Para quem contribui sem ser Maintainer, veja [CONTRIBUTING.pt-BR.md](CONTRIBUTING.pt-BR.md).

## 1. Princípios gerais

Ser Maintainer significa possuir responsabilidade sobre a qualidade, estabilidade, organização e direção da Whiskey Station 14.

Permissão de escrita não significa liberdade irrestrita. O acesso ao repositório existe para permitir que o Maintainer cumpra suas funções.

O objetivo da manutenção é proteger o projeto contra código ruim, regressões, exploits, problemas de desempenho, conteúdo mal balanceado, dívida técnica e alterações que prejudiquem a experiência dos jogadores.

Decisões devem priorizar a estabilidade do servidor, qualidade do código, qualidade do conteúdo, coerência com a proposta da Whiskey, experiência dos jogadores e facilidade de manutenção.

## 2. Perfil e competência do Maintainer

Nem todo Maintainer precisa saber programar. A equipe pode possuir pessoas especializadas em código, gameplay, balanceamento, mapping, testes, organização, documentação ou outras áreas.

Todo Maintainer deve reconhecer os próprios limites. Quando não possuir conhecimento suficiente para avaliar uma alteração técnica, deve solicitar revisão de alguém qualificado.

Não é permitido aprovar algo apenas porque compila, parece funcionar ou porque o autor afirma que está correto.

Um Maintainer pode contribuir em várias áreas, mas deve evitar tomar sozinho decisões que dependam de conhecimento técnico que não possui.

## 3. Pull Requests

Alterações relevantes devem ser submetidas por Pull Request.

Todo PR deve explicar claramente o que foi alterado, por que foi alterado, impacto no gameplay ou balanceamento, plano de testes e riscos conhecidos.

PRs não triviais devem apresentar screenshots ou vídeos quando isso for útil para provar o funcionamento.

Quando forem solicitadas mudanças, o autor deve realizar as correções, resolver os comentários correspondentes e solicitar nova revisão.

Depois de modificar um PR, ele deve ser testado novamente.

PR de sistema novo deve ser aberta como draft e permanecer assim até o conjunto estar completo. Peça solta de sistema inacabado não deve ser mesclada, mesmo funcionando sozinha.

Draft desliga todo o CI deste repositório: build, testes e verificação de fim de linha só rodam com a PR marcada como pronta. Enquanto estiver em draft, a verificação é responsabilidade do autor, e o que foi rodado localmente deve estar escrito na PR. Antes de qualquer merge a PR precisa ser marcada como pronta e o CI precisa rodar.

PRs claramente incompletos, sem contexto, sem testes suficientes ou que não sigam os requisitos podem ser devolvidos ou fechados.

## 4. Revisão e aprovação

A revisão começa pelo estado do CI, antes de ler o código. Check vermelho ignorado já custou dias com PR sem compilar em Release sem ninguém notar.

Nenhum PR deve ser aprovado apenas porque compila.

A revisão deve considerar correção, regressões, balanceamento, desempenho, segurança, exploits, arquitetura, manutenção futura e experiência do jogador.

Todo PR está sujeito à revisão do Lead Maintainer.

PRs de alto impacto podem exigir aprovação explícita do Lead Maintainer antes do merge. Isso inclui, por exemplo, sistemas centrais, combate, economia, ciência, antagonistas, administração, networking, prediction, infraestrutura ou grandes mudanças de gameplay.

Uma aprovação de outro Maintainer não autoriza a ignorar uma decisão explícita do Lead Maintainer.

## 5. PRs feitos por Maintainers

Maintainers não recebem aprovação automática para os próprios PRs.

O mesmo padrão de revisão, teste e qualidade aplicado aos demais contribuidores deve ser aplicado aos Maintainers.

O cargo não pode ser usado como argumento para evitar revisão, testes, correções ou discussão técnica.

## 6. Merge e proteção do master

O master deve ser tratado como código de produção, não como laboratório.

Antes do merge, o Maintainer deve ter segurança razoável de que o PR foi revisado, testado e não possui problemas relevantes conhecidos.

Não se deve realizar merge apenas para esvaziar a fila de PRs.

Alterações grandes devem ser preferencialmente divididas quando isso melhorar a revisão ou permitir reversões mais seguras.

Quando uma entrega tem um sistema novo e o conteúdo que usa esse sistema, os dois devem ir em Pull Requests separados: um para o sistema, outro para o conteúdo. O sistema entra primeiro, e o conteúdo vem depois, apoiado nele.

Isso existe por três motivos práticos. Sistema e conteúdo falham de formas diferentes, e separados podem ser revertidos separadamente: se o conteúdo estiver mal balanceado, ele volta atrás sem levar o sistema junto. Sistema costuma ser porte ou código novo, e conteúdo costuma ser YAML e tradução, que pedem olhos e critérios diferentes na revisão. E PR de mil linhas não é revisada de verdade, é aprovada no olho.

A mesma divisão vale quando o sistema vem portado de outro fork: o porte fica numa PR, com a checagem de licença e as diferenças de engine, e o que a Whiskey escreve em cima fica em outra.

Merge direto é exceção e deve ser reservado para situações justificadas, especialmente emergências.

## 7. Emergências

Em casos críticos, um Maintainer pode agir imediatamente para reduzir dano ao projeto ou ao servidor.

Exemplos incluem exploit ativo, falha grave do servidor, corrupção de dados ou vulnerabilidade severa.

Após uma correção emergencial, a alteração deve ser documentada e revisada posteriormente.

## 8. Código próprio e upstream

Todo código novo deve ser colocado nos módulos próprios da Whiskey conforme a estrutura do projeto.

Quando for necessário alterar arquivos upstream, a alteração deve ser identificada de forma clara para facilitar futuras sincronizações.

Alterações de uma linha em arquivos upstream devem utilizar comentários no padrão // Trauma - explicação ou # Trauma - explicação em YML.

Alterações maiores devem utilizar marcações como // <Trauma> e // </Trauma>. Quando for removida uma seção inteira, pode-se usar o formato de comentário indicado pelo projeto.

Código escrito pela Whiskey dentro de arquivo herdado usa a marcação da Whiskey, // Whiskey - explicação para uma linha e // <Whiskey> com // </Whiskey> para bloco.

Marcação existente não deve ser apagada ao alterar o bloco que ela envolve. Ela é o que faz o próximo upstream dar conflito na linha certa em vez de passar por cima sem aviso. Ao substituir lógica marcada como do Trauma por lógica nossa, troque a marcação, nunca remova.

Quando novas entradas são adicionadas a listas onde a ordem não é importante, elas devem ser agrupadas no topo quando isso reduzir conflitos com upstream.

## 9. Ports de outros forks

Conteúdo de outro fork não deve ser copiado cegamente.

O Maintainer deve verificar diferenças de arquitetura, componentes, eventos, sistemas, prototypes, convenções e balanceamento antes de aceitar um port.

Deve ser analisado se a funcionalidade realmente pertence à Whiskey e se existe alguma dependência que o projeto não possui.

Também devem ser verificadas licença, atribuição e origem de sprites, sons, código e demais recursos.

Portar uma funcionalidade existente em outro projeto não é prova de que ela esteja correta ou adequada para a Whiskey.

## 10. C# e organização de código

Todo novo código C# deve utilizar os módulos próprios previstos pelo projeto.

Quando possível, eventos devem ser mantidos nos módulos apropriados para preservar desacoplamento entre upstream e lógica da Whiskey.

Quando for necessário adicionar métodos, campos ou funcionalidades em arquivos upstream, deve-se preferir a estrutura de partials e marcações recomendadas pelo projeto.

Não se deve adicionar novos event handlers diretamente a sistemas upstream quando a lógica puder ser colocada em um sistema próprio.

Devem ser utilizadas APIs proxy quando disponíveis, como TryComp, em vez de acessar diretamente o EntityManager dentro de EntitySystem ou BUI quando isso não for necessário.

## 11. Resources, prototypes e estrutura de arquivos

Recursos próprios devem permanecer nas subpastas específicas previstas pela estrutura do projeto. Conteúdo escrito pela Whiskey vai em _Whiskey. As pastas _Trauma, _Goobstation, _DV, _ES e demais pastas de fork guardam conteúdo herdado e não devem receber conteúdo novo nosso.

Prototypes próprios devem permanecer em suas áreas correspondentes e não devem poluir arquivos upstream sem necessidade.

Alterações devem manter separação clara entre conteúdo da Whiskey e conteúdo herdado de outras fontes.

## 12. Localization

English (en-US) é a língua padrão e canônica do projeto.

Conteúdo novo ou portado deve possuir sua localização base em Resources/Locale/en-US.

Traduções para outros idiomas podem existir, mas não devem substituir nem ser requisito para o catálogo em inglês.

PRs não devem depender de tradução opcional para funcionar corretamente.

## 13. Update logic e desempenho

A primeira componente em um EntityQueryEnumerator deve ser a menos comum quando isso reduzir o número de entidades verificadas.

Não deve ser utilizado um EntityQueryEnumerator que percorra praticamente todas as entidades quando um componente mais específico puder filtrar primeiro.

Frametime não deve ser usado para lógica de jogo. Deve-se trabalhar com IGameTiming.CurTime e AutoPausedField quando apropriado.

Em caminhos de execução frequente, devem ser utilizadas EntityQuery e outras otimizações simples quando elas reduzirem o custo.

Um código que funciona localmente pode ainda ser inadequado se escalar mal com muitas entidades ou jogadores.

## 14. Component networking

Componentes que podem ser compartilhados devem ficar em shared quando apropriado e devem ser networkados salvo uma razão válida para não fazê-lo.

Campos precisam ser networkados quando são alterados pelo código ou quando o componente é adicionado com valores modificados.

Quando houver muitos campos, deve-se considerar fieldDeltas e DirtyField para reduzir tráfego.

Alterações de networking devem ser analisadas quanto a sincronização, tráfego, diferenças entre cliente e servidor e possíveis estados inconsistentes.

## 15. Prediction e shared

Interações devem ser predicted salvo existir uma razão forte para não serem.

Código deve ficar em shared quando não houver uma dependência rígida de servidor ou cliente.

Alterações que funcionem apenas por causa de comportamento local devem receber atenção especial durante a revisão.

Problemas de prediction não devem ser aceitos apenas porque desaparecem em testes simplificados.

## 16. UI

Quando for necessário adicionar elementos a uma UI upstream, deve-se preferir injeção e extensão desacoplada sempre que possível.

A lógica específica da Whiskey não deve ser acoplada ao upstream sem necessidade.

Alterações de interface devem considerar cliente, manutenção e compatibilidade com futuras atualizações upstream.

## 17. Tags

Tags novas em Recursos próprios devem seguir ordem alfabética quando essa for a convenção do arquivo.

Cada tag deve possuir documentação de como é utilizada.

Antes de criar uma tag nova, deve-se verificar se uma existente já resolve o problema.

## 18. YML e estilo de prototypes

Entity prototypes devem manter a ordem de campos definida pelas convenções do projeto: type, abstract, parent, id, name, suffix, description, placement, categories.

Outros prototypes devem, no mínimo, manter type, abstract, parent e id nessa ordem quando aplicável.

Listas YML devem seguir a indentação utilizada pelas diretrizes do projeto.

Quando um bloco de dados se repete muitas vezes no mesmo arquivo, devem ser considerados anchors e referências em vez de copiar e colar o mesmo conteúdo.

## 19. Sons

Sons globais, como música de lobby e briefings de antagonistas, devem ser estéreo.

Sons posicionais de objetos normalmente devem ser mono.

Quando um asset externo estiver sendo utilizado, a licença e a origem devem ser verificadas.

## 20. Gameplay e balanceamento

Uma funcionalidade não deve ser aprovada apenas porque é divertida ou tecnicamente interessante.

Devem ser avaliados acesso, custo, recompensa, risco, progressão, contrajogo, frequência e impacto sobre outros departamentos e sistemas.

Alterações em ciência, medicina, engenharia, economia, crafting, combate, antagonistas ou progressão devem receber análise proporcional ao impacto.

Uma mecânica que funciona tecnicamente pode ser rejeitada por design, balanceamento, manutenção ou direção do projeto.

## 21. Ciência, máquinas e economia

Alterações em ciência devem considerar tier, custo, recursos, tempo de pesquisa, acesso e impacto sobre a progressão.

Máquinas e produção devem ser avaliadas contra loops de criação de recursos, automação excessiva e vantagens econômicas.

Receitas, reciclagem, materiais e preços devem ser analisados para evitar arbitragem ou geração infinita de recursos.

Alterações aparentemente pequenas podem causar grande impacto econômico e devem ser testadas quando houver risco.

## 22. Antagonistas e habilidades

Novos antagonistas e poderes devem possuir contrajogo razoável.

Devem ser considerados aviso, reação possível, custo, frequência, alcance, mobilidade, furtividade e possibilidade de abuso.

Uma habilidade que permite ao próprio antagonista fabricar facilmente a condição necessária para ficar permanentemente mais forte deve ser analisada com cautela.

Balanceamento deve considerar também quem enfrenta a mecânica, e não apenas quem a utiliza.

## 23. Mapas e Maptainers

Mapas possuem responsabilidade especializada no repositório.

Alterações de mapa devem considerar fluxo da estação, departamentos, acesso, segurança, spawning, desempenho e experiência da rodada.

Quando uma alteração estiver especificamente dentro do escopo de mapping, deve-se respeitar a revisão dos Maptainers.

## 24. Testes

Todo autor é responsável por testar o próprio PR.

O teste deve ser proporcional ao risco. Dependendo da alteração, pode incluir build, linter, testes automatizados, servidor local, multiplayer, regressão e desempenho.

Quando a revisão exigir alterações, o PR deve ser testado novamente antes de nova aprovação.

Deve haver instruções suficientes para outra pessoa reproduzir os testes.

Teste automatizado que existe para guardar uma regra deve ser provado: apague a guarda de propósito, rode de novo e confirme que ele reprova. Teste que continua passando com o código removido não está guardando nada, e é pior que não ter teste, porque dá confiança falsa.

Não testado não significa necessariamente rejeitado, mas deve significar que o risco da falta de teste foi explicitamente considerado.

## 25. Changelog e mídia

Alterações perceptíveis para jogadores devem possuir changelog.

Refactors e alterações extremamente internas podem ser isentos quando não houver impacto perceptível.

PRs de mapping devem utilizar o changelog específico quando a alteração não for uma mudança ampla do pool de mapas.

Alterações visuais e de gameplay devem apresentar mídia quando necessária para demonstrar o resultado.

## 26. IA e autoria

Ferramentas de IA podem ser utilizadas como auxílio, mas o autor continua responsável pelo código.

Não é aceitável enviar código que o autor não consegue explicar ou manter.

Modelos de linguagem não substituem testes, revisão ou conhecimento do projeto.

## 27. Revisão construtiva

A revisão deve criticar código, proposta ou resultado, e não a pessoa.

Comentários devem, quando possível, explicar o problema, o risco e a alternativa.

O objetivo da revisão é melhorar o PR e também ajudar o contribuidor a evoluir.

Quando a ideia for válida, mas a implementação ruim, deve-se preferir solicitar mudanças em vez de rejeitar automaticamente todo o trabalho.

## 28. Bloqueio de PR

Qualquer Maintainer pode solicitar bloqueio temporário quando identificar problema relevante.

O bloqueio deve possuir justificativa técnica ou de projeto.

Não é permitido bloquear PR por antipatia pessoal, perseguição ou disputa de autoridade.

Em caso de desacordo entre Maintainers, o Lead Maintainer poderá tomar a decisão final.

## 29. Conduta e abuso de autoridade

Maintainers representam a equipe do projeto e devem agir profissionalmente.

Não é permitido usar o cargo para humilhar, intimidar, perseguir ou favorecer membros.

Também é proibido apagar trabalho de outras pessoas por vingança, inserir exploits deliberadamente, contornar decisões da equipe ou utilizar permissões em benefício pessoal.

Abuso de permissões pode resultar em remoção ou redução do acesso de Maintainer.

## 30. Regra de decisão

Antes de aprovar uma alteração, o Maintainer deve conseguir explicar o que ela faz, por que existe, quais riscos possui, como foi testada e por que merece entrar no master.

Se não houver conhecimento suficiente para responder isso, deve-se pedir revisão adicional.

Quando houver conflito entre preferência pessoal e necessidade técnica do projeto, a decisão deve ser baseada em evidências, convenções, balanceamento, manutenção e direção definida para a Whiskey.

## Regra de ouro

> "O Maintainer não existe para aumentar a quantidade de código no repositório. O Maintainer existe para garantir que o código que entra no repositório seja código que vale a pena manter."

> "Permissão não significa liberdade absoluta. Aprovação significa responsabilidade."

## Referências

As regras acima foram elaboradas a partir da estrutura e das práticas observadas no repositório, sem inserir citações no corpo do regulamento.

- [`CONTRIBUTING.md`](CONTRIBUTING.md), herdado do Trauma. Regras de PR, C#, Resources, localization, update logic, networking, sons, UI, prediction, tags, YML, comentários em upstream e changelogs.
- [`CONTRIBUTING.pt-BR.md`](CONTRIBUTING.pt-BR.md), as diretrizes da Whiskey.
- [`.github/PULL_REQUEST_TEMPLATE.md`](.github/PULL_REQUEST_TEMPLATE.md), com descrição da alteração, balanceamento, plano de testes, mídia, requisitos e changelog.
- `.github/CODEOWNERS`, que define os Maintainers como responsáveis pelo repositório e os Maptainers pelos caminhos de mapas.
- Pull requests do próprio repositório, usados para observar prática real de ports, testes, balanceamento, licenciamento, changelogs e separação de mudanças.

Este regulamento complementa as normas técnicas existentes no repositório e não substitui a documentação upstream do Space Station 14.
