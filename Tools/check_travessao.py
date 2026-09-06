#!/usr/bin/env python3
# SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
# SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
#
# SPDX-License-Identifier: AGPL-3.0-or-later

"""Reprova travessão em texto escrito pela Whiskey.

Nao e frescura de estilo. Em setembro de 2026 um travessao numa descricao de
PR foi apresentado publicamente como prova de que o codigo tinha sido gerado,
e o argumento pegou porque ninguem aqui usa travessao quando escreve de
verdade. Conferir por script custa dois segundos e vale mais que discutir.

Vale para o que a Whiskey escreve. Arquivo herdado do upstream fica de fora:
mudar pontuacao de codigo dos outros so gera conflito no proximo merge.
"""

import subprocess
import sys
from typing import Iterable

TRAVESSAO = "—"

# so o que e nosso. Herdado nao entra, ver o docstring.
PREFIXOS = (
    "Content.Shared/_Whiskey/",
    "Content.Server/_Whiskey/",
    "Content.Client/_Whiskey/",
    "Content.Trauma.Server/_Whiskey/",
    "Content.IntegrationTests/Tests/Whiskey/",
    "Resources/Prototypes/_Whiskey/",
    "Resources/Locale/pt-BR/_Whiskey/",
    "Resources/Locale/en-US/_Whiskey/",
    "Resources/Changelog/WhiskeyChangelog.yml",
    "Tools/_Whiskey/",
)

# Docs/Changes fica de fora de proposito: sao worklogs assinados por quem
# portou, em ingles, e la o travessao separa data de descricao. Reescrever
# pontuacao de texto assinado por outra pessoa nao e trabalho de linter.
#
# Locale/*/paper tambem: e literatura in-game, livro escrito por personagem e
# assinado por autor de verdade. O travessao la e escolha de quem escreveu, e
# o check existe para o texto que a Whiskey produz, nao para prosa importada.

EXCECOES = ("Resources/Locale/pt-BR/_Whiskey/paper/",
            "Resources/Locale/en-US/_Whiskey/paper/")


def arquivos_de_texto() -> Iterable[str]:
    # -I pula binario, senao PNG entra por coincidencia de bytes
    processo = subprocess.run(
        ["git", "grep", "--cached", "-Il", ""],
        check=True,
        encoding="utf-8",
        stdout=subprocess.PIPE)

    for linha in processo.stdout.splitlines():
        caminho = linha.strip()
        if caminho.startswith(PREFIXOS) and not caminho.startswith(EXCECOES):
            yield caminho


def linhas_com_travessao(caminho: str) -> list[int]:
    with open(caminho, encoding="utf-8", errors="replace") as arquivo:
        return [n for n, linha in enumerate(arquivo, 1) if TRAVESSAO in linha]


def main() -> int:
    falhou = False
    for caminho in arquivos_de_texto():
        for linha in linhas_com_travessao(caminho):
            print(
                f"::error file={caminho},line={linha},"
                f"title=Travessao encontrado::"
                f"Use virgula, dois pontos, parenteses ou ponto final. "
                f"Ver Tools/check_travessao.py para o porque.")
            falhou = True

    if not falhou:
        print("Nenhum travessao. Pontuacao de gente.")

    return 1 if falhou else 0


sys.exit(main())
