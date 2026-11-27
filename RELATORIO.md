# Trabalho Pratico 2 — Sistemas Operacionais

**Integrantes:** Pedro Henrique Oliveira Santos, Caio Casadei e Vitório  
**Repositorio:** [so-tp2-three-problems](https://github.com/pedrohos-dev/so-tp2-three-problems)

## Resumo

O trabalho implementa solucoes concorrentes para tres problemas classicos de
sincronizacao. As implementacoes e os relatorios especificos estao nos
diretorios abaixo:

| Problema | Implementacao e relatorio |
|---|---|
| Montanha-russa | [Codigo e relatorio](montanha-russa/README.md) · [Relatorio detalhado](montanha-russa/RELATORIO.md) |
| Travessia dos babuinos | [Codigo](travessia-babuinos/babuinos_versoes_revisado.c) · [Relatorio](travessia-babuinos/RELATORIO.md) |
| Construcao de H2O | [Codigo e instrucoes](h2o/README.md) · [Relatorio](h2o/RELATORIO.md) |

## Validacao

| Problema | Verificacao | Resultado |
|---|---|---|
| Montanha-russa | Nove casos com invariantes instrumentados, registrados em `montanha-russa/results/validacao.txt` | 9/9 passaram; a execucao nao foi repetida nesta maquina, que nao tem o SDK .NET |
| Babuinos | Quatro versoes, 40 threads, travessia de 1 ms e chegadas simultaneas | Todas passaram seguranca, capacidade e conclusao |
| H2O | Exemplos, entradas invalidas e 20 execucoes com 30 moleculas cada | `make test` passou |

Os resultados de desempenho dependem do ambiente. A bateria dos babuinos acima
e um teste de funcionamento, nao uma comparacao de desempenho controlada.

## Como executar

Consulte o README de cada problema para requisitos e comandos. Para validar
H2O em Linux, entre em `h2o/` e execute `make test`.
