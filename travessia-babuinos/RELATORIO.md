# Relatorio — Travessia dos Babuinos

## Problema e implementacao

A corda comporta no maximo cinco babuinos e nao pode ser usada ao mesmo tempo
por sentidos opostos. O programa em C compara quatro estrategias de
sincronizacao com mutexes e semaforos: lightswitch, catraca, admissao
serializada e turnos em lotes limitados. As estrategias diferem no controle
de alternancia; as versoes sem politica de turnos nao prometem ausencia de
inanicao sob chegadas continuas.

## Validacao

Compilacao e teste executados em Linux:

```sh
gcc -O2 -Wall -Wextra -pthread babuinos_versoes_revisado.c -o babuinos
./babuinos -m 0 -n 40 -t 1 -g 0 -s 42
```

As quatro versoes concluiram a execucao e passaram as verificacoes de sentidos
opostos, limite de cinco na corda e conclusao dos 40 babuinos. A execucao foi
um teste funcional curto; os numeros abaixo variam conforme o ambiente e nao
devem ser tratados como benchmark.

| Versao | Estrategia | Vazao (babuinos/s) | Espera media (ms) |
|---:|---|---:|---:|
| 1 | Ingenua, sem catraca | 4167,7 | 4 |
| 2 | Catraca | 1856,8 | 10 |
| 3 | Catraca e admissao serializada | 1870,2 | 10 |
| 4 | Turnos com lote limitado (`k=10`) | 3890,9 | 4 |

## Execucao

No Linux, compile com o comando acima e consulte o cabecalho do codigo para
ver opcoes de versao, carga e saida CSV.
