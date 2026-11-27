# Problema da Montanha-Russa

Solução em C# (.NET 10) dos problemas 1 e 1.1 (carro único e vários carros),
com harness de experimentos, verificação de invariantes e geração de gráficos.

O relatório de decisões de projeto, implementação, testes e avaliação está em
`RELATORIO.md`. Os resultados experimentais e gráficos estão em `results/`.

## Como executar

```sh
dotnet build -c Release

# verificação das restrições (invariantes instrumentados)
dotnet run -c Release --no-build -- --validate   # 9 casos -> results/validacao.txt

# demonstração do caso de um carro
dotnet run -c Release --no-build -- --single

# experimento completo (r = 30) -> results/medicoes.csv
dotnet run -c Release --no-build

# matriz reduzida para teste rápido
dotnet run -c Release --no-build -- --quick

# filtros opcionais
dotnet run -c Release --no-build -- --scenario 1 --m 10 --c 4 --reps 5 --output results/teste.csv

# gráficos e tabela agregada
python scripts/plot_results.py --summary-md results/resumo.md

# reconstrói o PDF do relatório a partir de RELATORIO.md
python scripts/build_report.py
```

Requisitos do script de gráficos: Python 3, `pandas` e `matplotlib`
(`pip install -r scripts/requirements.txt`).

## 1. Um carro

Quatro semáforos:

| Semáforo | Valor inicial | Significado |
|---|---:|---|
| `prontoParaEmbarcar` | 0 | carro já chamou `load()` e autoriza embarque |
| `embarcados` | 0 | um passageiro concluiu `board()` |
| `prontoParaDesembarcar` | 0 | carro já chamou `unload()` |
| `desembarcados` | 0 | um passageiro concluiu `unboard()` |

```text
Carro:
  repetir:
    load()
    para k = 1..C: signal(prontoParaEmbarcar)
    para k = 1..C: wait(embarcados)
    run()
    unload()
    para k = 1..C: signal(prontoParaDesembarcar)
    para k = 1..C: wait(desembarcados)

Passageiro:
  wait(prontoParaEmbarcar)
  board()
  signal(embarcados)
  wait(prontoParaDesembarcar)
  unboard()
  signal(desembarcados)
```

O código está em `SingleCarDemo.cs`.

## 1.1. Vários carros

Além dos quatro semáforos **por carro**, há:

- `carregando` (inicial 1): apenas um carro embarca por vez;
- uma fila `filaDeEntrega` protegida por `lock`, que registra a ordem de embarque;
- para cada carro e cada volta, um semáforo `done` novo: o carro espera o
  `done` do carro que embarcou imediatamente antes e, ao terminar o desembarque
  de toda a sua carga, acorda o próximo.

```text
Carro i (cada volta):
  wait(carregando)
  load()
  para k = 1..C: signal(prontoParaEmbarcar[i])
  para k = 1..C: wait(embarcados[i])
  // ainda sob 'carregando':
  pred = cauda da filaDeEntrega; anexa done desta volta à fila
  signal(carregando)
  run()                       // vários carros simultaneamente na pista
  if pred != null: wait(pred) // só espera a vez antes de descarregar
  unload()
  para k = 1..C: signal(prontoParaDesembarcar[i])
  para k = 1..C: wait(desembarcados[i])
  signal(done)
```

### Por que uma ficha compartilhada não resolve a ordem

Um único semáforo-ficha adquirido no início do `unload()` não garante FIFO: um
carro que embarcou depois pode chegar ao `wait()` antes do carro anterior e
consumir a ficha. Também não serviria adquirir a ficha antes do `run()`, pois
isso impediria que mais de um carro estivesse na pista. A corrente de
`done` resolve os dois problemas: a ordem é capturada no embarque (sob
`carregando`) e a espera acontece somente no desembarque. Um `done` novo por
volta evita que um sinal de uma volta antiga seja consumido pelo sucessor
errado.

### Passageiros

Cada passageiro é fixado estaticamente ao carro `id % m` e pede voltas em loop
até o carro terminar suas `R` voltas. Após a última volta, o carro acorda todos
os passageiros ainda bloqueados para que possam encerrar. A latência reportada
é o intervalo entre o pedido de volta e a autorização para embarcar.

## Parâmetros do experimento

| Grandeza | Valores |
|---|---|
| carros `m` | 1, 2, 5, 10, 15, 20, 25, 30, 40, 50 |
| capacidade `C` | 4 (fixo); sensibilidade 2 e 8 |
| passageiros `n` | cenário 1: 200; cenários 2 e 3: `2·m·C` |
| voltas por carro `R` | 20 |
| `tL, tR, tU` | 1 ms, 10 ms, 1 ms (cenário 3: 0) |
| repetições `r` | 30 por configuração |

Cenários: (1) `n` fixo para medir o ganho ao adicionar carros; (2) `n`
proporcional para medir o custo de sincronização conforme o sistema cresce;
(3) sem trabalho simulado para isolar a sobrecarga dos semáforos.

Combinações com `m·C > n` são descartadas — no cenário 1 com `C = 8` isso
acontece para `m > 25`, pois `n = 200`.

Métricas: `T(m)` (tempo total), `λ = m·R·C / T`, latência média no `board()`,
`S = T(1)/T(m)` e `E = S/m` (cenário 1), `T/(m·R)` (cenário 3), uso de CPU e
número de trocas de contexto.

## Resultados

Os dados brutos ficam em `results/medicoes.csv`, a tabela agregada em
`results/resumo.md` e as figuras em `results/graficos/`:

- `tempo_total.png` — `T(m)` por cenário e capacidade;
- `vazao.png` — `λ(m)`;
- `latencia.png` — espera média até o embarque (com desvio);
- `speedup_eficiencia.png` — `S(m)` e `E(m)` com as retas ideais;
- `sobrecarga.png` — `T(m)/(m·R)` no cenário 3;
- `cpu.png` — CPU em % de um núcleo.

A bateria de testes fica em `results/validacao.txt` (9/9 casos passaram).

### Principais resultados (C = 4)

Médias de 30 repetições; a tabela completa para `C = 2, 4, 8` está em
`results/resumo.md` e os dados brutos em `results/medicoes.csv`.

| m | C1: T (s) | C1: λ | C2: T (s) | C2: λ | C2: latência (ms) | C3: T/volta (ms) | S | E |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 0,326 | 246 | 0,294 | 272 | 15,6 | 0,419 | 1,001 | 1,001 |
| 2 | 0,337 | 478 | 0,304 | 527 | 15,8 | 0,269 | 0,972 | 0,486 |
| 5 | 0,344 | 1167 | 0,327 | 1227 | 16,3 | 0,197 | 0,950 | 0,190 |
| 10 | 0,475 | 1690 | 0,488 | 1640 | 25,1 | 0,217 | 0,687 | 0,069 |
| 15 | 0,715 | 1682 | 0,722 | 1665 | 41,2 | 0,246 | 0,456 | 0,030 |
| 20 | 0,947 | 1691 | 0,953 | 1680 | 58,0 | 0,277 | 0,344 | 0,017 |
| 25 | 1,114 | 1801 | 1,174 | 1704 | 73,5 | 0,331 | 0,293 | 0,012 |
| 30 | 1,304 | 1842 | 1,405 | 1709 | 88,5 | 0,349 | 0,250 | 0,008 |
| 40 | 1,788 | 1792 | 1,837 | 1743 | 122,1 | 0,388 | 0,182 | 0,005 |
| 50 | 2,279 | 1756 | 2,278 | 1756 | 153,8 | 0,481 | 0,143 | 0,003 |

Leitura dos resultados:

- **Vazão:** sobe rapidamente até `m ≈ 10` e depois satura (~1700 pass/s para
  `C = 4`). O gargalo é a serialização inerente: um carro embarca por vez e os
  desembarques ocorrem em fila. Com `C = 2` e `C = 8`, os tetos são de cerca de
  900 e 3400 pass/s, respectivamente — aproximadamente proporcionais a `C`.
- **Latência:** no cenário 2 ela cresce de ~16 ms (`m ≤ 5`) para ~154 ms
  (`m = 50`) porque `n = 2mC` passageiros disputam as mesmas vagas de embarque.
  No cenário 1 com `C = 8`, a latência cai de ~103 ms (`m = 1`) para ~32 ms
  (`m = 10`) e depois estabiliza: mais carros reduzem a fila até o ponto em que
  o custo de sincronização passa a dominar.
- **Speedup/eficiência:** os valores decrescentes não indicam piora de um
  trabalho fixo. Como `R = 20` é por carro, adicionar carros **aumenta** o
  número total de voltas; `T(m)` cresce quase linearmente para `m ≥ 10` e,
  pela definição `S = T(1)/T(m)`, `S` cai. A métrica que realmente mostra o
  ganho é `λ(m)`, que satura conforme descrito acima.
- **Sobrecarga (cenário 3):** o tempo por volta tem formato em U — com
  `C = 4`, cai de 0,419 ms (`m = 1`) para 0,197 ms (`m = 5`) e sobe para
  0,481 ms (`m = 50`). O primeiro trecho é o paralelismo dos carros; o segundo
  é o custo de escalonamento dos até 850 threads e da cadeia de semáforos. Com
  `C = 8` e `m = 50`, a sobrecarga chega a 0,944 ms por volta.
- **CPU e trocas de contexto:** `cpu_s` está no CSV. Trocas de contexto não
  foram coletadas nesta execução Windows; veja a observação abaixo.

## Observações sobre a medição

- No Windows, `Thread.Sleep(1)` dura cerca de 15 ms com a resolução padrão do
  timer. `Timing.EnableFineGrainedSleep()` chama `timeBeginPeriod(1)` no início
  e `timeEndPeriod(1)` no fim, de modo que `tL = tU = 1 ms` seja representativo.
- O uso de CPU é obtido de `Process.TotalProcessorTime` dividido pelo tempo
  decorrido. O .NET/Windows não expõe contador de trocas de contexto; essa
  métrica pode ser coletada executando o binário sob WSL/Linux com
  `perf stat -e context-switches,cpu-migrations dotnet montanha-russa.dll`.
- `--validate` instrumenta os invariantes: no máximo um carro em `load()`,
  exatamente `C` embarques e desembarques por volta, nenhum `board()` antes do
  `load()` do seu carro, nenhum `unboard()` antes do `unload()` do seu carro,
  fila de entrega vazia ao fim e sequência de cargas no desembarque não
  decrescente. A instrumentação é desligada nas medições de desempenho.

## Estrutura

```text
montanha-russa.csproj
Program.cs            CLI
Config.cs             parâmetros
Timing.cs             trabalho simulado e resolução do timer
Semaphore.cs          wrapper P/V sobre SemaphoreSlim
RollerCoaster.cs      estado compartilhado (multi-carro)
Car.cs                thread do carro
Passenger.cs          thread do passageiro
SingleCarDemo.cs      solução do problema 1
Experiment.cs         matriz, execução e CSV
TestSuite.cs          bateria de 9 testes de correção
Metrics.cs            linha de resultado
RELATORIO.md/.pdf     relatório final (10 páginas)
scripts/plot_results.py
scripts/build_report.py
scripts/requirements.txt
```
