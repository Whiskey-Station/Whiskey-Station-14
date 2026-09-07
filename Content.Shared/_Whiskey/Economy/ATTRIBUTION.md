<!--
SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
SPDX-FileCopyrightText: 2026 Whiskey Station Contributors

SPDX-License-Identifier: AGPL-3.0-or-later
-->

# Economia da Whiskey: autoria e licença

**Isto não é porte.** A economia da Whiskey Station foi escrita aqui, entre 6 e
7 de setembro de 2026, e não veio de fork nenhum. Este arquivo existe para que
isso seja verificável por quem chegar depois, e para que qualquer reuso seja
rastreável.

## Autoria

| | |
| --- | --- |
| Autor | Zequinza, `felipe828218@gmail.com` |
| Repositório de origem | `Whiskey-Station/Whiskey-Station-14` |
| Período | 6 e 7 de setembro de 2026 |
| Licença | AGPL-3.0-or-later |

O `git log` do intervalo mostra autor único. Todo arquivo listado abaixo carrega
`SPDX-FileCopyrightText` e `SPDX-License-Identifier` no cabeçalho.

## O que é original e o que é reuso

Original: o desenho e todo o código. As decisões que definem esta economia e
que não têm equivalente em outro fork de SS14:

- o salário é **transferido do orçamento do departamento**, e não criado. Sem
  cunhagem não existe inflação, e por isso não existe imposto nem
  rebalanceamento de moeda neste sistema
- o saldo mora no **cartão de identificação**, é roubável, e **não persiste
  entre rodadas**, de propósito
- a **comissão de venda com teto** é a única entrada de dinheiro novo
- a loja é um **app do PDA** que cobra do cartão de dentro do próprio aparelho

Reuso, e é reuso de código que já estava neste repositório, todo AGPL:

| Peça reaproveitada | Origem |
| --- | --- |
| `ShopVendorComponent` e os eventos de saldo e compra | Trauma Station |
| `DeliveryComponent`, a encomenda trancada por digital | upstream do Space Station 14 |
| `CurrencyPrototype` Spesos e o `StoreComponent` | upstream, via Trauma |
| `MiningPointsComponent`, precedente de saldo no cartão | Trauma Station |

Nenhuma linha foi copiada do Frontier Station, do Monolith, do Dumont ou de
qualquer fork restritivo. O caminho é o inverso: onde esses projetos
construíram banco e caixa eletrônico próprios, aqui a moeda nova responde aos
eventos que a máquina de loja do Trauma já levantava.

## Desenho vindo da comunidade

Três decisões não são minhas e o crédito é de quem sugeriu, no Discord da
Whiskey, em 6 e 7 de setembro de 2026:

- **Oloko_Bicho**: a loja ser um app do PDA em vez de uma máquina física. É
  essa ideia que elimina a ambiguidade de qual cartão paga
- **Punkzebu**: a compra chegar embalada e trancada, e poder ser enviada de
  presente, abrindo só para o destinatário
- **Beronha1** e **Cleitosvaldo**: a entrega por pod, que ainda não está
  implementada

## Se você quer portar isto

Pode. É AGPL-3.0-or-later e a licença não exige pedir permissão. Ela exige
outras três coisas, e essas não são opcionais:

1. **Manter a licença.** O código portado continua AGPL-3.0-or-later. Não dá
   para relicenciar como MIT, como proprietário, nem como "todos os direitos
   reservados"
2. **Manter a atribuição.** Os cabeçalhos `SPDX-FileCopyrightText` ficam. Apagar
   o crédito não é falta de educação, é violação de licença, e se demonstra com
   um diff
3. **Publicar a fonte.** É a letra A do AGPL: quem hospeda um servidor com uma
   versão modificada disto tem que disponibilizar o código modificado a quem
   usa o serviço

Um aviso prático para quem for portar: boa parte disto se apoia no
`ShopVendorComponent` do Trauma e no `DeliveryComponent` do upstream. Fork que
não tenha essas peças vai precisar portá-las junto ou reescrever a ponte.

## Arquivos cobertos

```
Content.Shared/_Whiskey/Economy/
Content.Server/_Whiskey/Economy/
Content.Client/_Whiskey/Economy/
Content.Trauma.Shared/_Whiskey/Economy/
Content.Trauma.Server/_Whiskey/Economy/
Content.Trauma.Client/_Whiskey/Economy/
Content.IntegrationTests/Tests/Whiskey/CreditAccountTest.cs
Content.IntegrationTests/Tests/Whiskey/CreditVendorTest.cs
Content.IntegrationTests/Tests/Whiskey/CreditWalletTest.cs
Content.IntegrationTests/Tests/Whiskey/PayrollTest.cs
Content.IntegrationTests/Tests/Whiskey/SalesCommissionTest.cs
Content.IntegrationTests/Tests/Whiskey/StoreCartridgeTest.cs
Resources/Locale/*/_Whiskey/economy/
Resources/Prototypes/_Whiskey/Catalog/loja_geral.yml
Resources/Prototypes/_Whiskey/Catalog/Cargo/cargo_loja.yml
Resources/Prototypes/_Whiskey/Entities/Structures/Machines/loja_geral.yml
Resources/Prototypes/_Whiskey/Entities/Objects/Devices/cartridges.yml
```

As alterações em arquivo herdado estão marcadas com `<Whiskey>` no próprio
arquivo, conforme a regra 8 do `REGULAMENTO-MAINTAINERS.md`.
