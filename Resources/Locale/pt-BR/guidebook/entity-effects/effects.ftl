-create-3rd-person =
    { $chance ->
        [1] Cria
        *[other] criar
    }

-cause-3rd-person =
    { $chance ->
        [1] Causas
        *[other] causa
    }

-satiate-3rd-person =
    { $chance ->
        [1] Sacia
        *[other] saciar
    }

entity-effect-guidebook-spawn-entity =
    { $chance ->
        [1] Cria
        *[other] criar
    } { $amount ->
        [1] {INDEFINITE($entname)}
        *[other] {$amount} {MAKEPLURAL($entname)}
    }

entity-effect-guidebook-destroy =
    { $chance ->
        [1] Destrói
        *[other] destruir
    } o objeto

entity-effect-guidebook-break =
    { $chance ->
        [1] Pausas
        *[other] quebrar
    } o objeto

entity-effect-guidebook-explosion =
    { $chance ->
        [1] Causas
        *[other] causa
    } uma explosão

entity-effect-guidebook-emp =
    { $chance ->
        [1] Causas
        *[other] causa
    } um pulso eletromagnético

entity-effect-guidebook-flash =
    { $chance ->
        [1] Causas
        *[other] causa
    } um flash ofuscante

entity-effect-guidebook-foam-area =
    { $chance ->
        [1] Cria
        *[other] criar
    } grandes quantidades de espuma

entity-effect-guidebook-smoke-area =
    { $chance ->
        [1] Cria
        *[other] criar
    } grandes quantidades de fumaça

entity-effect-guidebook-satiate-thirst =
    { $chance ->
        [1] Sacia
        *[other] saciar
    } { $relative ->
        [1] sede medianamente
        *[other] sede em {NATURALFIXED($relative, 3)}x a taxa média
    }

entity-effect-guidebook-satiate-hunger =
    { $chance ->
        [1] Sacia
        *[other] saciar
    } { $relative ->
        [1] fome em média
        *[other] fome em {NATURALFIXED($relative, 3)}x a taxa média
    }

entity-effect-guidebook-health-change =
    { $chance ->
        [1] { $healsordeals ->
                [heals] Cura
                [deals] Ofertas
                *[both] Modifica a saúde por
             }
        *[other] { $healsordeals ->
                    [heals] curar
                    [deals] negócio
                    *[both] modificar a saúde por
                 }
    } { $changes }

entity-effect-guidebook-even-health-change =
    { $chance ->
        [1] { $healsordeals ->
            [heals] Cura uniformemente
            [deals] Ofertas uniformes
            *[both] Modifica uniformemente a saúde por
        }
        *[other] { $healsordeals ->
            [heals] curar uniformemente
            [deals] lidar uniformemente
            *[both] modificar uniformemente a saúde,
        }
    } { $changes }

# Trauma - removed LOC() from all of these, its already localized
entity-effect-guidebook-status-effect-old =
    { $type ->
        [update]{ $chance ->
                    [1] Causas
                     *[other] causa
                  } {$key} por pelo menos {NATURALFIXED($time, 3)} { $time ->
                        [1] segundo
                        *[other] segundos
                    } sem acumulação
        [add]   { $chance ->
                    [1] Causas
                    *[other] causa
                } {$key} por pelo menos {NATURALFIXED($time, 3)} { $time ->
                        [1] segundo
                        *[other] segundos
                    } com acumulação
        [set]  { $chance ->
                    [1] Causas
                    *[other] causa
                } {$key} por {NATURALFIXED($time, 3)} { $time ->
                        [1] segundo
                        *[other] segundos
                    } sem acumulação
        *[remove]{ $chance ->
                    [1] Remove
                    *[other] remover
                } {NATURALFIXED($time, 3)} { $time ->
                        [1] segundo
                        *[other] segundos
                    } de {$key}
    }

# Trauma - removed LOC() from all of these, its already localized
entity-effect-guidebook-status-effect =
    { $type ->
        [update]{ $chance ->
                    [1] Causas
                     *[other] causa
                  } {$key} por pelo menos {NATURALFIXED($time, 3)} { $time ->
                        [1] segundo
                        *[other] segundos
                    } sem acumulação
        [add]   { $chance ->
                    [1] Causas
                    *[other] causa
                } {$key} por pelo menos {NATURALFIXED($time, 3)} { $time ->
                        [1] segundo
                        *[other] segundos
                    } com acumulação
        [set]  { $chance ->
                    [1] Causas
                    *[other] causa
                } {$key} por pelo menos {NATURALFIXED($time, 3)} { $time ->
                        [1] segundo
                        *[other] segundos
                    } sem acumulação
        *[remove]{ $chance ->
                    [1] Remove
                    *[other] remover
                } {NATURALFIXED($time, 3)} { $time ->
                        [1] segundo
                        *[other] segundos
                    } de {$key}
    } { $delay ->
        [0] imediatamente
        *[other] após um atraso de {NATURALFIXED($delay, 3)} segundo
    }

# Trauma - removed LOC() from all of these, its already localized
entity-effect-guidebook-status-effect-indef =
    { $type ->
        [update]{ $chance ->
                    [1] Causas
                    *[other] causa
                 } permanente {$key}
        [add]   { $chance ->
                    [1] Causas
                    *[other] causa
                } permanente {$key}
        [set]  { $chance ->
                    [1] Causas
                    *[other] causa
                } permanente {$key}
        *[remove]{ $chance ->
                    [1] Remove
                    *[other] remover
                } {$key}
    } { $delay ->
        [0] imediatamente
        *[other] após um atraso de {NATURALFIXED($delay, 3)} segundo
    }

# Trauma - LOC($key) -> knockdown, copy paste major
entity-effect-guidebook-knockdown =
    { $type ->
        [update]{ $chance ->
                    [1] Causas
                    *[other] causa
                    } knockdown por pelo menos {NATURALFIXED($time, 3)} { $time ->
                            [1] segundo
                            *[other] segundos
                        } sem acumulação
        [add]   { $chance ->
                    [1] Causas
                    *[other] causa
                } knockdown por pelo menos {NATURALFIXED($time, 3)} { $time ->
                        [1] segundo
                        *[other] segundos
                    } com acumulação
        *[set]  { $chance ->
                    [1] Causas
                    *[other] causa
                } knockdown por pelo menos {NATURALFIXED($time, 3)} { $time ->
                        [1] segundo
                        *[other] segundos
                    } sem acumulação
        [remove]{ $chance ->
                    [1] Remove
                    *[other] remover
                } {NATURALFIXED($time, 3)} { $time ->
                        [1] segundo
                        *[other] segundos
                    } de knockdown
    }

entity-effect-guidebook-set-solution-temperature-effect =
    { $chance ->
        [1] Conjuntos
        *[other] definir
    } a temperatura da solução para exatamente {NATURALFIXED($temperature, 2)}k

entity-effect-guidebook-adjust-solution-temperature-effect =
    { $chance ->
        [1] { $deltasign ->
                [1] Adiciona
                *[-1] Remove
            }
        *[other]
            { $deltasign ->
                [1] adicionar
                *[-1] remover
            }
    } heat { $deltasign ->
                [1] para
                *[-1] de
           } the solution until it reaches { $deltasign ->
                [1] no máximo {NATURALFIXED($maxtemp, 2)}k
                *[-1] pelo menos {NATURALFIXED($mintemp, 2)}k
            }

entity-effect-guidebook-adjust-reagent-reagent =
    { $chance ->
        [1] { $deltasign ->
                [1] Adiciona
                *[-1] Remove
            }
        *[other]
            { $deltasign ->
                [1] adicionar
                *[-1] remover
            }
    } {NATURALFIXED($amount, 2)}u of {$reagent} { $deltasign ->
        [1] para
        *[-1] de
    } a solução

entity-effect-guidebook-adjust-reagent-group =
    { $chance ->
        [1] { $deltasign ->
                [1] Adiciona
                *[-1] Remove
            }
        *[other]
            { $deltasign ->
                [1] adicionar
                *[-1] remover
            }
    } {NATURALFIXED($amount, 2)}u of reagents in the group {$group} { $deltasign ->
            [1] para
            *[-1] de
        } a solução

entity-effect-guidebook-adjust-temperature =
    { $chance ->
        [1] { $deltasign ->
                [1] Adiciona
                *[-1] Remove
            }
        *[other]
            { $deltasign ->
                [1] adicionar
                *[-1] remover
            }
    } {POWERJOULES($amount)} of heat { $deltasign ->
            [1] para
            *[-1] de
        } o corpo em que está

entity-effect-guidebook-chem-cause-disease =
    { $chance ->
        [1] Causas
        *[other] causa
    } a doença { $disease }

entity-effect-guidebook-chem-cause-random-disease =
    { $chance ->
        [1] Causas
        *[other] causa
    } as doenças { $diseases }

entity-effect-guidebook-jittering =
    { $chance ->
        [1] Causas
        *[other] causa
    } tremor

entity-effect-guidebook-clean-bloodstream =
    { $chance ->
        [1] Limpa
        *[other] limpar
    } a corrente sanguínea de outros produtos químicos

entity-effect-guidebook-cure-disease =
    { $chance ->
        [1] Curas
        *[other] cura
    } doenças

entity-effect-guidebook-eye-damage =
    { $chance ->
        [1] { $deltasign ->
                [1] Ofertas
                *[-1] Cura
            }
        *[other]
            { $deltasign ->
                [1] negócio
                *[-1] curar
            }
    } dano ocular

entity-effect-guidebook-vomit =
    { $chance ->
        [1] Causas
        *[other] causa
    } vômito

entity-effect-guidebook-create-gas =
    { $chance ->
        [1] Cria
        *[other] criar
    } { $moles } { $moles ->
        [1] verruga
        *[other] toupeiras
    } de { $gas }

entity-effect-guidebook-drunk =
    { $chance ->
        [1] Causas
        *[other] causa
    } embriaguez

entity-effect-guidebook-electrocute =
    { $chance ->
        [1] { $stuns ->
            [true] Eletrocuta
            *[false] Choques
            }
        *[other] { $stuns ->
            [true] eletrocutar
            *[false] choque
            }
    } o metabolizador para {NATURALFIXED($time, 3)} { $time ->
            [1] segundo
            *[other] segundos
    }

entity-effect-guidebook-emote =
    { $chance ->
        [1] Forçará
        *[other] vigor
    } the metabolizer to [bold][color=white]{$emote}[/color][/bold]

entity-effect-guidebook-extinguish-reaction =
    { $chance ->
        [1] Extingue
        *[other] extinguir
    } fogo

# Trauma - $direction is set from the Flammable effect's multiplier sign, so negative Flammable reagents read "Decreases flammability"; defaults to increase
entity-effect-guidebook-flammable-reaction =
    { $chance ->
        [1] { $direction ->
                [decrease] Diminui
                *[increase] Aumenta
            }
        *[other] { $direction ->
                [decrease] diminuir
                *[increase] aumentar
            }
    } inflamabilidade

entity-effect-guidebook-ignite =
    { $chance ->
        [1] Acende
        *[other] inflamar
    } o metabolizador

entity-effect-guidebook-make-sentient =
    { $chance ->
        [1] Faz
        *[other] fazer
    } o metabolizador senciente

entity-effect-guidebook-make-polymorph =
    { $chance ->
        [1] Polimorfos
        *[other] polimorfo
    } o metabolizador em um { $entityname }

entity-effect-guidebook-modify-bleed-amount =
    { $chance ->
        [1] { $deltasign ->
                [1] Induz
                *[-1] Reduz
            }
        *[other] { $deltasign ->
                    [1] induzir
                    *[-1] reduzir
                 }
    } sangramento

entity-effect-guidebook-modify-blood-level =
    { $chance ->
        [1] { $deltasign ->
                [1] Aumenta
                *[-1] Diminui
            }
        *[other] { $deltasign ->
                    [1] Aumenta
                    *[-1] Diminui
                 }
    } nível sanguíneo

entity-effect-guidebook-paralyze =
    { $chance ->
        [1] Paralisa
        *[other] paralisar
    } o metabolizador por pelo menos {NATURALFIXED($time, 3)} { $time ->
            [1] segundo
            *[other] segundos
    }

entity-effect-guidebook-movespeed-modifier =
    { $chance ->
        [1] Modifica
        *[other] modificar
    } velocidade de movimento em {NATURALFIXED($sprintspeed, 3)}x por pelo menos {NATURALFIXED($time, 3)} { $time ->
            [1] segundo
            *[other] segundos
    }

entity-effect-guidebook-reset-narcolepsy =
    { $chance ->
        [1] Pautas temporárias
        *[other] afastar temporariamente
    } fora da narcolepsia

entity-effect-guidebook-wash-cream-pie-reaction =
    { $chance ->
        [1] Lavagens
        *[other] lavar
    } tirar torta de creme do rosto

entity-effect-guidebook-cure-zombie-infection =
    { $chance ->
        [1] Curas
        *[other] cura
    } uma infecção zumbi contínua

entity-effect-guidebook-cause-zombie-infection =
    { $chance ->
        [1] Dá
        *[other] dar
    } um indivíduo a infecção zumbi

entity-effect-guidebook-innoculate-zombie-infection =
    { $chance ->
        [1] Curas
        *[other] cura
    } uma infecção zumbi contínua e fornece imunidade a infecções futuras

entity-effect-guidebook-reduce-rotting =
    { $chance ->
        [1] Regenera
        *[other] regenerado
    } {NATURALFIXED($time, 3)} { $time ->
            [1] segundo
            *[other] segundos
    } de apodrecimento

entity-effect-guidebook-area-reaction =
    { $chance ->
        [1] Causas
        *[other] causa
    } uma reação de fumaça ou espuma por {NATURALFIXED($duration, 3)} { $duration ->
            [1] segundo
            *[other] segundos
    }

entity-effect-guidebook-add-to-solution-reaction =
    { $chance ->
        [1] Causas
        *[other] causa
    } {$reagent} a ser adicionado ao seu contêiner de solução interno

entity-effect-guidebook-artifact-unlock =
    { $chance ->
        [1] Ajuda
        *[other] ajuda
        } desbloquear um artefato alienígena.

entity-effect-guidebook-artifact-durability-restore =
    Restaura a durabilidade de {$restored} em nós de artefatos alienígenas ativos.

entity-effect-guidebook-plant-attribute =
    { $chance ->
        [1] Ajusta
        *[other] ajustar
    } {$attribute} by {$positive ->
    [false] [color=red]{$amount}[/color]
    *[true] [color=green]{$amount}[/color]
    }

entity-effect-guidebook-plant-cryoxadone =
    { $chance ->
        [1] Há muito tempo
        *[other] idade de volta
    } a planta, dependendo da idade da planta e do tempo de crescimento

entity-effect-guidebook-plant-phalanximine =
    { $chance ->
        [1] Restaurações
        *[other] restaurar
    } viabilidade para uma planta tornada inviável por uma mutação

entity-effect-guidebook-plant-remove-kudzu =
    { $chance ->
        [1] Remove
        *[other] remover
    } crescimento de erva daninha kudzu de uma planta

entity-effect-guidebook-plant-diethylamine =
    { $chance ->
        [1] Aumenta
        *[other] aumentar
    } a vida útil e/ou saúde básica da planta com 10% de chance para cada

entity-effect-guidebook-plant-robust-harvest =
    { $chance ->
        [1] Aumenta
        *[other] aumentar
    } a potência da planta em {$increase} até um máximo de {$limit}. Faz com que a planta perca suas sementes quando a potência atingir {$seedlesstreshold}. Tentar adicionar potência acima de {$limit} pode causar diminuição no rendimento com 10% de chance

entity-effect-guidebook-plant-seeds-add =
    { $chance ->
        [1] Restaura o
        *[other] restaurar o
    } sementes da planta

entity-effect-guidebook-plant-seeds-remove =
    { $chance ->
        [1] Remove o
        *[other] remova o
    } sementes da planta

entity-effect-guidebook-plant-mutate-chemicals =
    { $chance ->
        [1] Muta
        *[other] sofrer mutação
    } uma planta para produzir {$name}

entity-effect-guidebook-satiate =
    { $chance ->
        [1] Sacia
        *[other] sacia
    } { $relative ->
        [1] {$type} em ritmo médio
        *[other] {$type} a {NATURALFIXED($relative, 3)} vez(es) o ritmo médio
    }

entity-effect-guidebook-plant-mutate-exude-gasses =
    { $chance ->
        [1] Faz a planta sofrer mutação
        *[other] faz a planta sofrer mutação
    } para exalar entre {$minValue} e {$maxValue} mols de gases

entity-effect-guidebook-plant-mutate-consume-gasses =
    { $chance ->
        [1] Faz a planta sofrer mutação
        *[other] faz a planta sofrer mutação
    } para consumir entre {$minValue} e {$maxValue} mols de gases

# <Whiskey> - efeito que levanta modificador de humor
entity-effect-guidebook-adjust-mood =
    { $chance ->
        [1] Afeta
        *[other] afetam
    } o humor de quem tem humor
# </Whiskey>
