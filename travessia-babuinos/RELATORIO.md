# Relatorio — Travessia dos Babuinos

## Problema e implementacao

A corda comporta no maximo cinco babuinos e nao pode ser usada ao mesmo tempo
por sentidos opostos. O programa em C compara quatro estrategias de
sincronizacao com mutexes e semaforos: lightswitch, catraca, admissao
serializada e turnos em lotes limitados. As estrategias diferem no controle
de alternancia; as versoes sem politica de turnos nao prometem ausencia de
inanicao sob chegadas continuas.

## Validacao

Compilacao e testes executados em Linux:

```sh
gcc -O2 -Wall -Wextra -pthread babuinos_versoes_revisado.c -o babuinos
./babuinos -m 0 -n 40 -t 1 -g 0 -s 42
```

Na bateria de carga, todas as quatro versoes passaram as verificacoes de
sentidos opostos, limite de cinco na corda e conclusao. Foram avaliadas
chegadas simultaneas (`gap=0`), travessia de 10 ms, proporcao 50/50 e tres
sementes para cada tamanho. A tabela mostra vazao media (babuinos/s) e espera
media (ms), agregadas por versao:

| Versao | Estrategia | n=40 | n=100 | n=200 |
|---:|---|---:|---:|---:|
| 1 | Ingenua, sem catraca | 443 (37 ms) | 470 (96 ms) | 478 (194 ms) |
| 2 | Catraca | 207 (93 ms) | 194 (250 ms) | 193 (500 ms) |
| 3 | Catraca e admissao serializada | 195 (102 ms) | 199 (238 ms) | 192 (502 ms) |
| 4 | Turnos com lote limitado (`k=10`) | 409 (39 ms) | 435 (104 ms) | 439 (215 ms) |

O pico observado na corda foi cinco em todas as configuracoes. A vazao das
versoes 2 e 3 fica proxima de 200 babuinos/s neste ambiente, enquanto a espera
cresce com a carga; isso evidencia o custo da alternancia. A versao 4 melhora
a vazao, mas os testes finitos nao provam ausencia de inanicao sob todo
escalonamento. Os numeros sao exploratorios, nao um benchmark controlado.

Como estresse adicional, foram executadas 60 combinacoes de semente e
proporcao direcional (10/90, 50/50 e 90/10), cada uma comparando as quatro
versoes com 100 threads. Todas passaram os invariantes.

## Execucao

No Linux, compile com o comando acima e consulte o cabecalho do codigo para
ver opcoes de versao, carga e saida CSV.
