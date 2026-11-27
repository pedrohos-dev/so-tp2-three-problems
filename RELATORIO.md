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
| Montanha-russa | Build .NET 10, nove casos instrumentados, demonstracao de carro unico e matriz rapida | Build sem avisos; 9/9 passaram; matriz rapida gerou 27 medicoes |
| Babuinos | Quatro versoes; cargas de 40, 100 e 200 threads; 3 sementes por carga; mais 60 execucoes sob proporcoes 10/90, 50/50 e 90/10 | Todas passaram seguranca, capacidade e conclusao; corda nunca excedeu 5 |
| H2O | `make test`, ordens de chegada, entradas invalidas e 50 sequencias adicionais | Todos os grupos verificados continham 2 H e 1 O; sem bloqueios |

Os resultados de desempenho dependem do ambiente. A bateria dos babuinos acima
e exploratoria, com travessia de 10 ms e tres repeticoes por configuracao; nao
e uma comparacao de desempenho controlada. A matriz rapida da Montanha-Russa
valida o executor, mas nao substitui o experimento completo de 30 repeticoes.

## Como executar

Na raiz do repositorio:

```sh
make -C h2o test
gcc -O2 -Wall -Wextra -pthread travessia-babuinos/babuinos_versoes_revisado.c -o /tmp/babuinos
/tmp/babuinos -m 0 -n 40 -t 1 -g 0 -s 42
cd montanha-russa
dotnet build -c Release
dotnet run -c Release --no-build -- --validate
```

Consulte os READMEs para opcoes adicionais e para executar os experimentos
completos.
