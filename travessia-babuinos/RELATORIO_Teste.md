# Trabalho Prático 2 — Sistemas Operacionais

**Problema 2: Travessia dos babuínos**

---

## 1. Projeto da solução

Cada babuíno é uma thread com sentido `d ∈ {0, 1}` e executa `entrar_corda(d)`, `atravessa()` (um `sleep` de duração Tc) e `sair_corda(d)`.

| Semáforo | Inicial | Função |
|---|---|---|
| `capacidade` | 5 | Uma vaga por babuíno na corda (capacidade). |
| `vazia` | 1 | Corda livre para algum sentido; o primeiro babuíno de um sentido a adquire e o último a libera. |
| `mutex_dir[d]` | 1 | Protege `cont_dir[d]`, o número de babuínos admitidos do sentido `d`. |
| `turnstile` | 1 | Catraca de entrada (versões 2 e 3). |

`vazia`, `cont_dir` e `mutex_dir` formam o padrão *lightswitch* e a catraca é o padrão *turnstile*, ambos descritos por Downey [3].

### 1.1 As quatro versões

```c
void acende(int d) { P(mutex_dir[d]); if (++cont_dir[d] == 1) P(vazia); V(mutex_dir[d]); }
void apaga(int d)  { P(mutex_dir[d]); if (--cont_dir[d] == 0) V(vazia); V(mutex_dir[d]); }

/* entrada */
v1:  acende(d); P(capacidade);
v2:  P(turnstile); acende(d); V(turnstile); P(capacidade);
v3:  P(turnstile); acende(d); P(capacidade); V(turnstile);
/* saída (versões 1 a 3) */
     V(capacidade); apaga(d);
```

- **Versão 1 (ingênua):** só o lightswitch. Satisfaz segurança e capacidade, mas um fluxo contínuo num sentido nunca deixa `cont_dir` voltar a zero, e o sentido oposto não passa.
- **Versão 2 (catraca):** um babuíno do sentido oposto que espera `vazia` segura a catraca, e as novas chegadas, mesmo do sentido atual, ficam na fila da catraca até a corda esvaziar.
- **Versão 3 (catraca com admissão serializada):** a catraca só é liberada depois de o babuíno obter também sua vaga. Isso serializa a admissão, mas **não** garante FIFO estrito, porque a ordem em que os semáforos acordam threads não é especificada.
- **Versão 4 (turnos com lote limitado, passagem de bastão):** o estado fica explícito e protegido por um semáforo-mutex `mtx`, e quem sai da corda decide quem entra. O lote `K` limita quantos babuínos seguidos do mesmo sentido são admitidos quando há espera no sentido oposto.

```c
/* estado (sob mtx): ncorda, dir_atual, espera[2], consec */
void entrar_v4(int d) {
    P(mtx);
    if (ncorda == 0 || (dir_atual == d && ncorda < CAPACIDADE && espera[d] == 0 &&
                        (espera[1-d] == 0 || consec < K))) { admite(d); V(mtx); }
    else { espera[d]++; V(mtx); P(fila[d]); }          /* quem sair nos admite */
}
void sair_v4(int d) {
    P(mtx); ncorda--;
    if (ncorda == 0) {                                  /* corda vazia: troca de sentido */
        prox = espera[1-d] > 0 ? 1-d : d;
        if (espera[prox] > 0) { dir_atual = prox; consec = 0;
            repete min(espera[prox], K, CAPACIDADE) vezes:
                { ncorda++; consec++; espera[prox]--; V(fila[prox]); } }
        else { dir_atual = -1; consec = 0; }
    } else if (espera[d] > 0 && (espera[1-d] == 0 || consec < K)) {
        ncorda++; consec++; espera[d]--; V(fila[d]);    /* vaga liberada, lote continua */
    }
    V(mtx);
}
```

### 1.2 Argumentos de corretude

**Segurança (versões 1 a 3).** `vazia` é adquirida quando `cont_dir[d]` passa de 0 para 1 e liberada quando volta a 0. O primeiro babuíno do sentido `d` segura `mutex_dir[d]` enquanto espera `vazia`, então os demais de `d` ficam bloqueados nesse mutex. Assim, enquanto um sentido tem babuínos admitidos, o oposto não obtém `vazia` e não atravessa. **Versão 4:** todos os babuínos contados em `ncorda` têm o sentido `dir_atual`, que só muda quando `ncorda == 0`, sempre sob `mtx`.

**Capacidade.** Nas versões 1 a 3 só cruza quem detém uma unidade de `capacidade`. Na versão 4, a entrada exige `ncorda < CAPACIDADE`; a saída repõe no máximo uma vaga com a corda ainda ocupada, e com a corda vazia admite no máximo `min(K, 5)`.

**Deadlock.** Na versão 2 a ordem de aquisição é sempre `turnstile` → `mutex_dir` → `vazia`, e a saída não usa a catraca, então não há ciclo de espera. Na versão 3 a catraca é mantida enquanto se espera `capacidade`, mas as vagas são devolvidas por babuínos que já estão na corda e cuja saída só usa `capacidade` e o `mutex_dir` do seu sentido. Esse mutex só fica retido por quem espera `vazia`, que é sempre do sentido oposto. Na versão 4 as esperas bloqueantes ficam em `fila[d]`, depois de liberar `mtx`, e como `fila[d]` é um semáforo contador, uma admissão feita antes de o babuíno chegar ao `P` não se perde.

**Inanição.** A versão 1 não a evita. Nas versões 2 e 3, a catraca impede que um sentido ocupe a corda para sempre, mas a ausência de inanição *individual* depende da ordem em que a plataforma acorda os semáforos. A versão 4 impõe alternância entre os sentidos por construção (no máximo `K` admissões seguidas quando há espera oposta), mas, dentro de um mesmo sentido, a ordem em `fila[d]` ainda depende do semáforo; ela não oferece limite de espera individual. Os testes não provam ausência de inanição: só observam esperas em execuções finitas.

## 2. Implementação e metodologia

O programa é um único arquivo C com uma camada de portabilidade: Win32 (`CreateThread`, `CreateSemaphore`, `QueryPerformanceCounter`) no Windows e pthreads, semáforos POSIX e `clock_gettime` no Linux. A lógica de sincronização é a mesma nas duas plataformas.

**Carga.** O sentido de cada babuíno é sorteado (`p` = probabilidade do sentido 0) com a mesma semente em todas as versões. As chegadas são espaçadas por um intervalo uniforme em `[0, 2g]` ms, e a travessia dura `Tc · U(0,8; 1,2)` (±20%), por `sleep`.

**Instrumentação.**
- **Oráculos** sob um mutex independente da solução: `na_corda[0]` e `na_corda[1]` (incrementados antes e decrementados depois de `atravessa()`), `max_total`, e os contadores `viol_sentido` e `viol_cap`.
- **Registro por babuíno** de `t_chegada`, `t_entrada` e `t_saida` (espera = `t_entrada − t_chegada`), e contagem das **trocas de sentido** na entrada da corda.
- **Watchdog:** declara suspeita de deadlock se, em 10 s, nenhum babuíno concluir a travessia havendo babuínos esperando.

**Experimentos.** Parâmetros-base: Tc = 100 ms, `g` = 10 ms (~100 chegadas/s, bem acima dos ~50 babuínos/s que a corda comporta), 50% em cada sentido, `K` = 10.

| Exp. | Objetivo | Variação |
|---|---|---|
| A | Corretude | 40 sementes por versão; n, % do sentido 0 e intervalo de chegada variando por semente; Tc = 5 ms |
| B1 | Carga crescente | n = 25, 100, 400 (3 sementes) |
| B2 | Inanição e vazão | 50%, 80%, 95%, 100% no sentido 0; n = 200 (2 sementes) |
| B3 | Lote da versão 4 | K = 1, 2, 5, 10, 20, 50; n = 200 (2 sementes) |
| B4 | Taxa de chegada | g = 0, 5, 10, 30, 100 ms; n = 100 (1 semente) |

Os experimentos rodaram em um contêiner Linux (kernel 6.18, Xeon 2,1 GHz, 1 vCPU, gcc 13.3 `-O2`). As tabelas mostram a média entre as sementes e, onde há ±, o desvio-padrão. O autor também repetiu uma configuração no Windows (seção 3.5).

## 3. Resultados

### 3.1 Corretude

Nas 160 execuções do experimento A (40 por versão, 19.200 travessias) não houve nenhuma violação de sentido nem de capacidade, o máximo observado na corda foi 5 e o watchdog nunca disparou. Isso é coerente com a seção 1.2, mas testes não provam corretude.

### 3.2 Carga crescente

| n | Versão | Vazão (bab/s) | Espera média (ms) | Trocas de sentido |
|---|---|---|---|---|
| 25 | 1 Ingênua | 36.9 ± 2.0 | 156 | 2 |
| 25 | 2 Catraca | 20.1 ± 1.6 | 488 | 12 |
| 25 | 3 Adm. serializada | 20.1 ± 1.5 | 487 | 12 |
| 25 | 4 Turnos (K=10) | 33.8 ± 1.9 | 169 | 4 |
| 100 | 1 Ingênua | 45.7 ± 1.0 | 541 | 2 |
| 100 | 2 Catraca | 18.8 ± 0.3 | 2040 | 49 |
| 100 | 3 Adm. serializada | 18.8 ± 0.3 | 2037 | 49 |
| 100 | 4 Turnos (K=10) | 42.4 ± 0.8 | 608 | 11 |
| 400 | 1 Ingênua | 47.5 ± 1.0 | 2184 | 2 |
| 400 | 2 Catraca | 18.7 ± 0.8 | 8538 | 197 |
| 400 | 3 Adm. serializada | 18.7 ± 0.8 | 8532 | 197 |
| 400 | 4 Turnos (K=10) | 44.9 ± 0.3 | 2376 | 39 |

- **Vazão.** A partir de n = 100, quando há fila, ela estabiliza: as versões 2 e 3 em ~19 bab/s, a versão 4 em ~42 a 45 e a versão 1 em ~46 a 48 (teto teórico: 5 / Tc = 50). Com n = 25 a fila é curta e todas ficam abaixo disso.
- **Espera.** Cresce linearmente com n, pois a chegada (~100/s) supera o atendimento. De n = 100 para n = 400, a espera média da versão 2 vai de 2040 ms para 8538 ms e a da versão 4, de 608 ms para 2376 ms.
- **Versões 2 e 3 são praticamente idênticas.** O número de trocas é ≈ n/2: como a catraca preserva a ordem de chegada e os sentidos são sorteados com 50%, os grupos do mesmo sentido têm 2 babuínos em média, e cada grupo ocupa a corda por ~Tc (2 / 0,1 s ≈ 20 bab/s, próximo do medido). Na versão 4 os grupos têm ~10 babuínos, igual a `K`.

### 3.3 Composição do tráfego e inanição

| % sentido 0 | Versão | Vazão (bab/s) | Espera máx. sent. 0 (ms) | Espera máx. sent. 1 (ms) | Desigualdade |
|---|---|---|---|---|---|
| 50% | 1 Ingênua | 46.4 ± 0.2 | 1174 | 1874 | 17.6x |
| 50% | 2 Catraca | 18.6 ± 0.5 | 8447 | 8453 | 1.0x |
| 50% | 3 Adm. serializada | 18.6 ± 0.5 | 8447 | 8452 | 1.0x |
| 50% | 4 Turnos (K=10) | 44.1 ± 0.6 | 1973 | 2299 | 1.2x |
| 80% | 1 Ingênua | 48.5 ± 0.1 | 1058 | 3180 | 3.0x |
| 80% | 2 Catraca | 24.7 ± 0.3 | 5811 | 5805 | 1.0x |
| 80% | 3 Adm. serializada | 24.7 ± 0.3 | 5805 | 5800 | 1.0x |
| 80% | 4 Turnos (K=10) | 43.2 ± 0.7 | 2371 | 337 | 7.0x |
| 95% | 1 Ingênua | 48.7 ± 0.2 | 1559 | 3689 | 2.4x |
| 95% | 2 Catraca | 36.5 ± 0.6 | 3239 | 3026 | 1.1x |
| 95% | 3 Adm. serializada | 36.4 ± 0.7 | 3228 | 3029 | 1.1x |
| 95% | 4 Turnos (K=10) | 43.2 ± 0.3 | 2356 | 184 | 12.9x |
| 100% | 1 Ingênua | 49.0 ± 0.1 | 1798 | 0 | — |
| 100% | 2 Catraca | 49.0 ± 0.1 | 2327 | 0 | — |
| 100% | 3 Adm. serializada | 49.0 ± 0.1 | 1800 | 0 | — |
| 100% | 4 Turnos (K=10) | 49.0 ± 0.1 | 1801 | 0 | — |

*Desigualdade = maior espera máxima entre os sentidos dividida pela menor, calculada em cada execução.*

- **Versão 1.** Com 50% em cada lado houve só 2 trocas de sentido: um sentido passou quase todo antes do outro (desigualdade de 17,6x). Qual sentido sofre varia com a semente, por isso as médias por sentido parecem próximas. Com 80% e 95% no sentido 0, o sentido minoritário esperou 2 a 3 vezes mais que o majoritário (3180 ms contra 1058 ms e 3689 ms contra 1559 ms).
- **Versões 2 e 3.** Esperas simétricas em todos os cenários, pois a catraca respeita a ordem de chegada. O custo é a vazão (18,6 bab/s com 50%), que só se aproxima das demais quando o tráfego é muito desbalanceado (36,5 bab/s com 95%).
- **Versão 4.** Com tráfego balanceado os sentidos esperam parecido (1,2x). Com tráfego desbalanceado o sentido **majoritário** espera mais e o minoritário pouco (337 ms e 184 ms com 80% e 95%), o que não é inanição do minoritário: os turnos alternam e a fila do majoritário é simplesmente maior. Por isso a desigualdade deve ser lida junto com as esperas por sentido: na versão 1 sofre o minoritário e na versão 4 espera mais o majoritário.
- **100%.** Todas as versões são equivalentes (49,0 bab/s, ~98% do teto), então a diferença entre elas está no tratamento do sentido oposto e não no custo de sincronização.

### 3.4 Lote K da versão 4

| k (lote) | Vazão (bab/s) | Espera média (ms) | Espera máx. (ms) | Trocas de sentido |
|---|---|---|---|---|
| 1 | 10.0 ± 0.0 | 8795 | 17662 | 186 |
| 2 | 18.4 ± 0.2 | 4311 | 8584 | 94 |
| 5 | 42.5 ± 0.5 | 1240 | 2462 | 38 |
| 10 | 44.1 ± 0.6 | 1165 | 2301 | 20 |
| 20 | 45.7 ± 0.1 | 1073 | 2145 | 10 |
| 50 | 46.2 ± 0.4 | 1070 | 2111 | 4 |

O lote ajusta a troca entre vazão e alternância. Com K = 1 a vazão cai para 10 bab/s (um babuíno por Tc) e com K = 2 ela iguala a das versões 2 e 3 (grupos de ~2). A vazão sobe rápido até K = 5 (a capacidade da corda) e continua crescendo devagar até K = 50, com cada vez menos trocas de sentido. K = 10 é um compromisso razoável.

### 3.5 Taxa de chegada e validação no Windows

Com carga leve (g = 100 ms, ~10 chegadas/s) as quatro versões são equivalentes: a vazão é limitada pela chegada (10,2 bab/s) e a espera é de dezenas de ms. Com g = 30 ms (~33 chegadas/s), as versões 2 e 3 já saturam (18,8 bab/s, espera média de 1278 ms), enquanto as versões 1 e 4 acompanham a chegada (31,5 e 31,4 bab/s, esperas de 147 e 126 ms). A versão 4 sustenta, portanto, uma carga oferecida mais de duas vezes maior sem formar fila significativa.

No Windows (Dev-C++), com n = 200, 50% no sentido 0, Tc = 100 ms, g = 10 ms e semente 42, as versões 1 a 4 deram 44,9; 20,9; 20,8 e 44,1 bab/s, com 2, 87, 87 e 20 trocas de sentido e nenhuma violação. O comportamento é o mesmo do Linux, com pequenas diferenças de valores atribuíveis ao escalonador e à resolução do `sleep`.

## 4. Conclusão e limitações

As quatro versões satisfizeram segurança e capacidade em todas as execuções, e a diferença está na justiça e no desempenho. A versão 1 é rápida, mas favorece um sentido; as versões 2 e 3 são simétricas, mas têm ~40% da vazão da versão 1 (por atender na ordem de chegada e formar grupos curtos); a versão 4 chega a ~88% do teto de vazão com alternância entre os sentidos, e o parâmetro K regula a troca. **Recomendamos a versão 4 com K = 10.**

Limitações: dados de um contêiner de 1 vCPU e de poucas sementes, sem intervalos de confiança; a espera máxima é uma estatística de cauda e varia entre execuções, então valem mais as comparações qualitativas que diferenças pequenas; nenhuma propriedade foi verificada formalmente; e o `sleep` simula a travessia sem carga real de CPU.

## Referências

1. G. R. Andrews. *Concurrent Programming: Principles and Practice*. Addison-Wesley, 1991.
2. A. S. Tanenbaum. *Modern Operating Systems*. Prentice Hall, 2ª ed., 2001.
3. A. B. Downey. *The Little Book of Semaphores*. Green Tea Press (padrões *turnstile* e *lightswitch*).

## Apêndice — Como reproduzir

```
gcc -O2 -pthread babuinos_versoes.c -o babuinos      # Linux/WSL (Windows: Dev-C++, F11)
babuinos -n 200 -p 50 -t 100 -g 10                   # compara as 4 versões
babuinos -m 4 -k 10 -n 200 -p 50 -t 100 -g 10        # relatório detalhado da versão 4
babuinos -c -H -n 200 -p 50 -t 100 -g 10 -s 1        # CSV com cabeçalho
```

Parâmetros: `-m` versão (0 = todas), `-k` lote, `-n` babuínos, `-p` % no sentido 0, `-t` Tc em ms, `-g` intervalo médio de chegada em ms, `-s` semente, `-c` CSV, `-H` cabeçalho.
