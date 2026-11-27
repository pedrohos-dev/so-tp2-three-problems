# Trabalho Prático 2 — Sistemas Operacionais

**Problema 2: Travessia dos babuínos**

CEFET-MG — Engenharia da Computação · Professora: Michelle Hanne

**Integrantes:** Caio César Casadei Miranda; [demais integrantes do grupo]

**Código-fonte:** [colar aqui a URL do repositório com o código]  (arquivo `babuinos_versoes.c`)

---

## 1. Introdução

Um cânion é atravessado por uma única corda. Babuínos de sentidos opostos que se encontram na corda brigam e caem, e a corda suporta no máximo 5 babuínos. O problema pede um esquema de sincronização com semáforos que garanta:

- **P1 (segurança):** nunca há babuínos de sentidos opostos na corda ao mesmo tempo;
- **P2 (capacidade):** nunca há mais de 5 babuínos na corda;
- **P3 (vivacidade):** nenhum babuíno espera indefinidamente, ou seja, não há deadlock nem inanição (um fluxo contínuo num sentido não pode bloquear para sempre o sentido oposto).

O plano de testes do enunciado define quatro objetivos: **O1** verificar a segurança, **O2** verificar a capacidade, **O3** verificar a vivacidade e **O4** caracterizar vazão e tempo de espera conforme a carga cresce.

Para entender o custo de cada decisão de projeto, não implementamos uma única solução, e sim **quatro versões** no mesmo programa, selecionáveis por parâmetro. A versão 1 é uma linha de base que pode causar inanição; as versões 2, 3 e 4 tentam evitá-la por mecanismos diferentes. Todas passam pelos mesmos oráculos e pela mesma carga de teste, o que permite comparar justiça (ausência de inanição) e desempenho.

## 2. Projeto da solução

### 2.1 Modelagem

Cada babuíno é uma thread com um sentido `d ∈ {0, 1}`. O ciclo de vida de cada thread é `entrar_corda(d)` → `atravessa()` → `sair_corda(d)`. A travessia é simulada por um `sleep` de duração Tc.

Os semáforos usados nas versões 1, 2 e 3 são:

| Semáforo | Valor inicial | Função |
|---|---|---|
| `capacidade` | 5 | Cada babuíno na corda ocupa uma vaga (P2). |
| `vazia` | 1 | "A corda está livre para algum sentido". O primeiro babuíno de um sentido a adquire e o último a libera. |
| `mutex_dir[d]` | 1 | Protege o contador `cont_dir[d]` de babuínos admitidos do sentido `d`. |
| `turnstile` | 1 | Catraca de entrada, usada nas versões 2 e 3. |

A combinação de `vazia`, `cont_dir[d]` e `mutex_dir[d]` é o padrão *lightswitch* descrito por Downey [3]: o primeiro a entrar "acende a luz" (toma a corda) e o último a sair "apaga a luz" (libera). A catraca (*turnstile*) é o outro padrão do mesmo livro.

### 2.2 As quatro versões

**Versão 1 — Ingênua (somente lightswitch).** É a solução mínima que satisfaz P1 e P2:

```c
void entrar_v1(int d) {
    P(mutex_dir[d]);
    if (++cont_dir[d] == 1) P(vazia);   /* primeiro do sentido toma a corda */
    V(mutex_dir[d]);
    P(capacidade);
}
void sair_v123(int d) {
    V(capacidade);
    P(mutex_dir[d]);
    if (--cont_dir[d] == 0) V(vazia);   /* último do sentido libera a corda */
    V(mutex_dir[d]);
}
```

Enquanto chegarem babuínos do sentido atual de forma contínua, `cont_dir[d]` nunca volta a zero e o sentido oposto não passa. A versão serve de referência de vazão e como exemplo de inanição.

**Versão 2 — Catraca.** Todo babuíno passa pela catraca antes do lightswitch:

```c
void entrar_v2(int d) {
    P(turnstile);
    P(mutex_dir[d]); if (++cont_dir[d] == 1) P(vazia); V(mutex_dir[d]);
    V(turnstile);
    P(capacidade);
}
```

Se um babuíno do sentido oposto estiver esperando em `P(vazia)`, ele segura a catraca. Os babuínos que chegam depois, mesmo do sentido atual, ficam na fila da catraca até que a corda esvazie. Com isso a corda acaba esvaziando. A saída é a mesma da versão 1 e não usa a catraca.

**Versão 3 — Catraca com admissão serializada.** Igual à versão 2, mas a catraca só é liberada depois que o babuíno também obtém sua vaga em `capacidade`:

```c
void entrar_v3(int d) {
    P(turnstile);
    P(mutex_dir[d]); if (++cont_dir[d] == 1) P(vazia); V(mutex_dir[d]);
    P(capacidade);          /* vaga em mãos antes de liberar a catraca */
    V(turnstile);
}
```

A admissão fica serializada pela catraca. Isso **não** garante ordem FIFO estrita, porque a ordem em que os semáforos acordam os processos bloqueados não é especificada.

**Versão 4 — Turnos com lote limitado (passagem de bastão).** Em vez de bloquear a entrada em semáforos "de fila única", o estado do sistema fica explícito, protegido por um semáforo-mutex `mtx`, e quem sai da corda decide quem entra:

```c
/* estado (protegido por mtx): ncorda, dir_atual, espera[2], consec */
void entrar_v4(int d) {
    P(mtx);
    if (ncorda == 0 ||
        (dir_atual == d && ncorda < CAPACIDADE && espera[d] == 0 &&
         (espera[1-d] == 0 || consec < K))) {
        admite(d); V(mtx);                 /* entra direto */
    } else {
        espera[d]++; V(mtx);
        P(fila[d]);                        /* quem sair nos admite */
    }
}
void sair_v4(int d) {
    P(mtx);
    ncorda--;
    if (ncorda == 0) {                     /* corda vazia: troca de sentido */
        prox = espera[1-d] > 0 ? 1-d : d;
        if (espera[prox] > 0) {
            dir_atual = prox; consec = 0;
            n = min(espera[prox], min(K, CAPACIDADE));
            repete n vezes: { ncorda++; consec++; espera[prox]--; V(fila[prox]); }
        } else { dir_atual = -1; consec = 0; }
    } else if (espera[d] > 0 && (espera[1-d] == 0 || consec < K)) {
        ncorda++; consec++; espera[d]--; V(fila[d]);   /* vaga liberada, lote continua */
    }
    V(mtx);
}
```

O parâmetro `K` (lote) limita quantos babuínos seguidos do mesmo sentido podem ser admitidos quando há alguém esperando no sentido oposto. Quando a corda esvazia, a vez passa ao sentido oposto, se houver espera. `K` controla a troca entre vazão (lotes grandes mantêm a corda cheia) e alternância entre os sentidos (lotes pequenos).

### 2.3 Argumentos de corretude

**Segurança (P1), versões 1 a 3.** O semáforo `vazia` é adquirido quando `cont_dir[d]` passa de 0 para 1 e liberado quando volta a 0, sempre sob `mutex_dir[d]`. O primeiro babuíno do sentido `d` segura `mutex_dir[d]` enquanto espera em `P(vazia)`. Os demais babuínos de `d` ficam bloqueados nesse mutex e só prosseguem depois que `vazia` foi obtida. Logo, enquanto um sentido tem babuínos admitidos, o sentido oposto não consegue obter `vazia` e não chega a atravessar.

**Segurança (P1), versão 4.** `ncorda` conta os babuínos admitidos e todos eles têm o sentido `dir_atual`. Esse sentido só muda quando `ncorda == 0` (na entrada com corda vazia ou na saída do último babuíno), sempre sob `mtx`.

**Capacidade (P2).** Nas versões 1 a 3 só cruza quem detém uma unidade de `capacidade` (inicial 5). Na versão 4, a admissão exige `ncorda < CAPACIDADE`; ao sair, no máximo uma vaga é reposta (no ramo `ncorda > 0`), e no ramo `ncorda == 0` são admitidos no máximo `min(K, 5)` babuínos.

**Ausência de deadlock.** Na versão 2 a ordem de aquisição é sempre `turnstile` → `mutex_dir` → `vazia`, e depois `capacidade` fora da catraca. O caminho de saída não adquire a catraca, então não há ciclo de espera. Na versão 3 a catraca é mantida enquanto se espera `capacidade`, mas as vagas são devolvidas por babuínos que já estão na corda. A saída deles usa apenas `capacidade` e o `mutex_dir` do sentido que está na corda, e esse mutex só fica retido por quem espera `vazia`, que é sempre um babuíno do sentido oposto. Na versão 4 só existe um mutex e as esperas bloqueantes ocorrem em `fila[d]`, depois de liberar `mtx`. Como `fila[d]` é um semáforo contador, uma admissão feita por quem sai não se perde mesmo que aconteça antes de o babuíno chegar ao `P(fila[d])`.

**Inanição (P3): o que se pode e o que não se pode afirmar.**

- **Versão 1:** não evita a inanição, como os experimentos mostram.
- **Versão 2:** a catraca impede que o fluxo contínuo de um sentido mantenha a corda ocupada para sempre. Porém, a ausência de inanição *individual* depende da ordem em que a implementação dos semáforos acorda as threads, que o padrão não especifica.
- **Versão 3:** tem a mesma ressalva.
- **Versão 4:** impõe alternância entre os sentidos (no máximo `K` admissões seguidas quando há espera oposta). Isso é uma propriedade do algoritmo, e não do escalonador, mas **apenas entre sentidos**. Entre babuínos do mesmo sentido, quem é acordado em `fila[d]` ainda depende da ordem do semáforo. A versão 4 não oferece, portanto, um limite de tempo de espera individual.

Os testes (seção 5) não provam a ausência de inanição: só observam, em execuções finitas, se há assimetria de esperas entre os sentidos.

## 3. Implementação

O programa é um único arquivo C (`babuinos_versoes.c`) com uma camada de portabilidade pequena. No Windows usa a API Win32 (`CreateThread`, `CreateSemaphore`, `CRITICAL_SECTION`, `QueryPerformanceCounter`) e no Linux usa pthreads, semáforos POSIX e `clock_gettime`. A lógica de sincronização (seção 2) é idêntica nas duas plataformas, escrita em termos de `P()` e `V()`.

**Geração de carga.** Cada babuíno recebe um sentido sorteado com probabilidade `p` de ser o sentido 0. A semente é a mesma para todas as versões, então todas enfrentam exatamente a mesma sequência de sentidos. As chegadas são espaçadas por um intervalo uniforme em `[0, 2g]` ms (média `g`). A duração da travessia é `Tc · U(0,8; 1,2)`, ou seja, Tc com variação de ±20%, simulada com `sleep`. O gerador pseudoaleatório é um congruencial linear próprio, com estado local por thread (evita depender de `rand_r`, que não existe no Windows).

**Instrumentação** (conforme o plano de testes):

- **Oráculos** protegidos por um mutex independente da solução testada: `na_corda[0]` e `na_corda[1]` (incrementados antes e decrementados depois de `atravessa()`), `max_total` (máximo de `na_corda[0] + na_corda[1]`), e dois contadores de violação: `viol_sentido` (alguém entra com o sentido oposto na corda) e `viol_cap` (mais de 5 na corda). Contamos também as **trocas de sentido** (quantas vezes o sentido de entrada na corda muda), que ajudam a explicar a vazão.
- **Registro por babuíno** de `t_chegada`, `t_entrada` e `t_saida`. A espera é `t_entrada − t_chegada`.
- **Watchdog:** uma thread verifica a cada 100 ms; se nenhum babuíno concluir a travessia em 10 s e houver babuínos esperando, declara suspeita de deadlock e encerra com código 2.

**Execução.** Sem parâmetros, o programa roda as quatro versões e imprime uma tabela comparativa; `-m 1..4` imprime um relatório detalhado de uma versão e `-c` produz CSV. O Apêndice A lista os comandos usados.

## 4. Plano de testes e metodologia

**Parâmetros.** Salvo indicação contrária: Tc = 100 ms (±20%), capacidade 5, intervalo médio de chegada `g` = 10 ms (~100 chegadas/s, bem acima do máximo de ~50 babuínos/s que a corda comporta), 50% dos babuínos em cada sentido e lote `K = 10` na versão 4.

**Métricas.** Tempo total; vazão (babuínos/s = n / tempo total); espera média e máxima (também por sentido); número de trocas de sentido; máximo observado na corda; violações dos oráculos.

**Experimentos.**

| Exp. | Objetivo | Variação |
|---|---|---|
| A | O1, O2, O3 (corretude sob estresse) | 40 sementes por versão, n = 60 a 180, % do sentido 0 e intervalo de chegada variando por semente, Tc = 5 ms |
| B1 | O4: carga crescente | n = 25, 50, 100, 200, 400 (3 sementes cada) |
| B2 | O3, O4: composição do tráfego | 50%, 80%, 95% e 100% no sentido 0, n = 200 (2 sementes cada) |
| B3 | Sensibilidade ao lote (versão 4) | K = 1, 2, 5, 10, 20, 50, n = 200 (2 sementes cada) |
| B4 | O4: taxa de chegada | g = 0 (rajada), 5, 10, 30, 100 ms, n = 100 (1 semente) |

**Ambiente.** Os experimentos foram executados em um contêiner Linux (kernel 6.18, Intel Xeon 2,1 GHz, **1 vCPU**), compilador gcc 13.3 com `-O2`. As travessias são `sleep`, então a CPU não é o gargalo, mas as medidas dependem do escalonador. As tabelas mostram a média entre as sementes; onde há ±, é o desvio-padrão populacional entre elas. Além disso, o autor repetiu uma configuração no Windows (seção 5.6) [máquina: modelo do processador / RAM / versão do Windows].

## 5. Resultados

### 5.1 Corretude sob estresse (O1, O2, O3)

| Versão | Execuções | Travessias simuladas | Violações de sentido | Violações de capacidade | Máx. na corda | Deadlocks | Resultado |
|---|---|---|---|---|---|---|---|
| 1 Ingênua | 40 | 4800 | 0 | 0 | 5 | 0 | OK |
| 2 Catraca | 40 | 4800 | 0 | 0 | 5 | 0 | OK |
| 3 Catraca + adm. serializada | 40 | 4800 | 0 | 0 | 5 | 0 | OK |
| 4 Turnos com lote | 40 | 4800 | 0 | 0 | 5 | 0 | OK |

Em todas as 160 execuções (19.200 travessias) os oráculos não registraram nenhuma violação de sentido nem de capacidade; o máximo observado na corda foi exatamente 5 e o watchdog nunca disparou. Esse resultado é coerente com os argumentos da seção 2.3, mas testes não provam corretude: eles apenas aumentam a confiança.

### 5.2 Carga crescente (O4)

Cenário: 50% em cada sentido, `g` = 10 ms, Tc = 100 ms.

| n | Versão | Vazão (bab/s) | Espera média (ms) | Espera máx. (ms) | Trocas de sentido |
|---|---|---|---|---|---|
| 25 | 1 Ingênua | 36.9 ± 2.0 | 156 | 398 | 2 |
| 25 | 2 Catraca | 20.1 ± 1.6 | 488 | 905 | 12 |
| 25 | 3 Catraca + adm. serializada | 20.1 ± 1.5 | 487 | 905 | 12 |
| 25 | 4 Turnos com lote | 33.8 ± 1.9 | 169 | 434 | 4 |
| 50 | 1 Ingênua | 44.0 ± 1.3 | 280 | 643 | 2 |
| 50 | 2 Catraca | 20.3 ± 1.7 | 932 | 1865 | 22 |
| 50 | 3 Catraca + adm. serializada | 20.3 ± 1.7 | 933 | 1866 | 22 |
| 50 | 4 Turnos com lote | 40.7 ± 2.1 | 311 | 684 | 6 |
| 100 | 1 Ingênua | 45.7 ± 1.0 | 541 | 1183 | 2 |
| 100 | 2 Catraca | 18.8 ± 0.3 | 2040 | 4185 | 49 |
| 100 | 3 Catraca + adm. serializada | 18.8 ± 0.3 | 2037 | 4182 | 49 |
| 100 | 4 Turnos com lote | 42.4 ± 0.8 | 608 | 1232 | 11 |
| 200 | 1 Ingênua | 47.0 ± 0.7 | 1067 | 2229 | 2 |
| 200 | 2 Catraca | 18.7 ± 0.4 | 4153 | 8521 | 99 |
| 200 | 3 Catraca + adm. serializada | 18.7 ± 0.4 | 4145 | 8512 | 99 |
| 200 | 4 Turnos com lote | 44.1 ± 0.5 | 1197 | 2371 | 20 |
| 400 | 1 Ingênua | 47.5 ± 1.0 | 2184 | 4397 | 2 |
| 400 | 2 Catraca | 18.7 ± 0.8 | 8538 | 17294 | 197 |
| 400 | 3 Catraca + adm. serializada | 18.7 ± 0.8 | 8532 | 17288 | 197 |
| 400 | 4 Turnos com lote | 44.9 ± 0.3 | 2376 | 5056 | 39 |

- **Vazão.** Estabiliza assim que há fila: as versões 2 e 3 em torno de 18,7 babuínos/s, a versão 4 em torno de 44 a 45 e a versão 1 em torno de 47. O teto teórico da corda é 5 / Tc = 50 babuínos/s.
- **Espera.** Cresce linearmente com n, porque a taxa de chegada (~100/s) supera a taxa de atendimento. Na versão 2, a espera média passa de 4153 ms (n = 200) para 8538 ms (n = 400); na versão 4, de 1197 ms para 2376 ms.
- **As versões 2 e 3 são praticamente idênticas.** A 3 espera por uma vaga dentro da catraca, mas com grupos de ~2 babuínos a capacidade quase nunca é o fator limitante.
- **Trocas de sentido.** Nas versões 2 e 3, o número de trocas é ≈ n/2 (grupos médios de 2 babuínos). Como a catraca preserva a ordem de chegada e os sentidos são sorteados com 50%, as sequências do mesmo sentido têm comprimento esperado 2, e cada grupo ocupa a corda por ~Tc: 2 / 0,1 s ≈ 20 babuínos/s, em linha com o valor medido. Na versão 4 os grupos têm ~10 babuínos (20 trocas para n = 200), igual a K.

### 5.3 Composição do tráfego e inanição (O3, O4)

Cenário: n = 200, `g` = 10 ms, Tc = 100 ms.

| % sentido 0 | Versão | Vazão (bab/s) | Espera média (ms) | Espera máx. sent. 0 (ms) | Espera máx. sent. 1 (ms) | Desigualdade | Trocas |
|---|---|---|---|---|---|---|---|
| 50% | 1 Ingênua | 46.4 ± 0.2 | 1078 | 1174 | 1874 | 17.6x | 2 |
| 50% | 2 Catraca | 18.6 ± 0.5 | 4169 | 8447 | 8453 | 1.0x | 100 |
| 50% | 3 Catraca + adm. serializada | 18.6 ± 0.5 | 4169 | 8447 | 8452 | 1.0x | 100 |
| 50% | 4 Turnos com lote | 44.1 ± 0.6 | 1166 | 1973 | 2299 | 1.2x | 20 |
| 80% | 1 Ingênua | 48.5 ± 0.1 | 931 | 1058 | 3180 | 3.0x | 2 |
| 80% | 2 Catraca | 24.7 ± 0.3 | 2928 | 5811 | 5805 | 1.0x | 62 |
| 80% | 3 Catraca + adm. serializada | 24.7 ± 0.3 | 2932 | 5805 | 5800 | 1.0x | 62 |
| 80% | 4 Turnos com lote | 43.2 ± 0.7 | 1352 | 2371 | 337 | 7.0x | 10 |
| 95% | 1 Ingênua | 48.7 ± 0.2 | 927 | 1559 | 3689 | 2.4x | 2 |
| 95% | 2 Catraca | 36.5 ± 0.6 | 1648 | 3239 | 3026 | 1.1x | 24 |
| 95% | 3 Catraca + adm. serializada | 36.4 ± 0.7 | 1656 | 3228 | 3029 | 1.1x | 24 |
| 95% | 4 Turnos com lote | 43.2 ± 0.3 | 1366 | 2356 | 184 | 12.9x | 11 |
| 100% | 1 Ingênua | 49.0 ± 0.1 | 926 | 1798 | 0 | — | 1 |
| 100% | 2 Catraca | 49.0 ± 0.1 | 927 | 2327 | 0 | — | 1 |
| 100% | 3 Catraca + adm. serializada | 49.0 ± 0.1 | 927 | 1800 | 0 | — | 1 |
| 100% | 4 Turnos com lote | 49.0 ± 0.1 | 927 | 1801 | 0 | — | 1 |

- **Versão 1 (inanição parcial).** Com 50% em cada lado, houve só 2 trocas de sentido: um sentido passou praticamente todo antes do outro, e a espera máxima do sentido que ficou para depois foi, em média, 17,6 vezes a do outro. Qual sentido sofre varia com a semente, por isso as médias por sentido na tabela parecem próximas; a coluna de desigualdade (maior espera máxima dividida pela menor, calculada em cada execução) mostra a assimetria. Com 80% e 95% no sentido 0, o sentido minoritário esperou de 2 a 3 vezes mais que o majoritário (3180 ms contra 1058 ms e 3689 ms contra 1559 ms).
- **Versão 2 e 3.** As esperas máximas dos dois sentidos são praticamente iguais em todos os cenários: a fila da catraca respeita a ordem de chegada. O custo é a vazão: 18,6 bab/s com 50%, e só se aproxima das demais versões quando o tráfego fica muito desbalanceado (36,5 bab/s com 95%), porque aí os grupos do mesmo sentido ficam longos naturalmente.
- **Versão 4.** Com tráfego balanceado, os dois sentidos esperam parecido (desigualdade ~1,2x). Com tráfego desbalanceado, o sentido **majoritário** espera mais (2371 ms e 2356 ms) e o minoritário pouco (337 ms e 184 ms). Isso não é inanição do minoritário: os turnos alternam, e a fila do sentido majoritário é simplesmente maior. Por isso a "desigualdade" entre sentidos (maior espera dividida pela menor) deve ser lida junto com a coluna de espera por sentido: na versão 1 quem sofre é o minoritário, na versão 4 quem espera mais é o majoritário.
- **100% no sentido 0.** As quatro versões são equivalentes (49,0 bab/s, ~98% do teto de 50), o que confirma que a diferença entre elas está no tratamento do sentido oposto, e não no custo de sincronização.

### 5.4 Sensibilidade ao lote K (versão 4)

Cenário: n = 200, 50% em cada sentido, `g` = 10 ms.

| k (lote) | Vazão (bab/s) | Espera média (ms) | Espera máx. (ms) | Trocas de sentido |
|---|---|---|---|---|
| 1 | 10.0 ± 0.0 | 8795 | 17662 | 186 |
| 2 | 18.4 ± 0.2 | 4311 | 8584 | 94 |
| 5 | 42.5 ± 0.5 | 1240 | 2462 | 38 |
| 10 | 44.1 ± 0.6 | 1165 | 2301 | 20 |
| 20 | 45.7 ± 0.1 | 1073 | 2145 | 10 |
| 50 | 46.2 ± 0.4 | 1070 | 2111 | 4 |

O lote controla a troca entre vazão e alternância. Com K = 1 o sentido muda a cada babuíno e a vazão cai para 10 babuínos/s (um por Tc). Com K = 2 a vazão (18,4) é igual à das versões 2 e 3, cujos grupos têm ~2 babuínos. A vazão sobe rápido até K = 5, que é a capacidade da corda (42,5), e continua crescendo mais devagar até K = 50 (46,2), mas com menos trocas de sentido e, portanto, menos alternância entre os lados. K = 10 é um compromisso razoável.

### 5.5 Taxa de chegada (O4)

Cenário: n = 100, 50% em cada sentido, uma semente.

| Intervalo médio entre chegadas (ms) | Chegadas/s | Versão | Vazão (bab/s) | Espera média (ms) | Espera máx. (ms) |
|---|---|---|---|---|---|
| 0 | rajada | 1 Ingênua | 48.1 | 966 | 1963 |
| 0 | rajada | 2 Catraca | 19.0 | 2679 | 5156 |
| 0 | rajada | 3 Catraca + adm. serializada | 19.0 | 2680 | 5157 |
| 0 | rajada | 4 Turnos com lote | 43.6 | 1032 | 2187 |
| 5 | 200 | 1 Ingênua | 47.8 | 733 | 1514 |
| 5 | 200 | 2 Catraca | 19.0 | 2442 | 4674 |
| 5 | 200 | 3 Catraca + adm. serializada | 19.0 | 2441 | 4673 |
| 5 | 200 | 4 Turnos com lote | 43.2 | 804 | 1712 |
| 10 | 100 | 1 Ingênua | 47.4 | 529 | 1153 |
| 10 | 100 | 2 Catraca | 18.9 | 2209 | 4201 |
| 10 | 100 | 3 Catraca + adm. serializada | 18.9 | 2208 | 4198 |
| 10 | 100 | 4 Turnos com lote | 42.1 | 621 | 1295 |
| 30 | 33 | 1 Ingênua | 31.5 | 147 | 587 |
| 30 | 33 | 2 Catraca | 18.8 | 1278 | 2312 |
| 30 | 33 | 3 Catraca + adm. serializada | 18.8 | 1278 | 2312 |
| 30 | 33 | 4 Turnos com lote | 31.4 | 126 | 429 |
| 100 | 10 | 1 Ingênua | 10.2 | 24 | 238 |
| 100 | 10 | 2 Catraca | 10.2 | 33 | 248 |
| 100 | 10 | 3 Catraca + adm. serializada | 10.2 | 33 | 248 |
| 100 | 10 | 4 Turnos com lote | 10.2 | 24 | 238 |

Com carga leve (intervalo de 100 ms, ~10 chegadas/s) as quatro versões são equivalentes: a vazão é limitada pela chegada (10,2 bab/s) e a espera é de dezenas de ms. Conforme a carga oferecida cresce, as versões 2 e 3 saturam primeiro (~19 bab/s): com intervalo de 30 ms (~33 chegadas/s) elas já têm espera média de 1278 ms, enquanto as versões 1 e 4 acompanham a chegada (31,5 e 31,4 bab/s) com esperas de 147 e 126 ms. Ou seja, a versão 4 sustenta uma carga oferecida mais de duas vezes maior que a das versões com catraca sem formar fila significativa.

### 5.6 Validação no Windows

O autor rodou a configuração `n = 200, 50% sentido 0, Tc = 100 ms, g = 10 ms, semente 42` no Windows (código compilado no Dev-C++ / Embarcadero):

| Versão | Vazão (bab/s) | Espera média (ms) | Espera máx. sent. 0 / sent. 1 (ms) | Trocas | Resultado |
|---|---|---|---|---|---|
| 1 Ingênua | 44,9 | 1140 | 93 / 2266 | 2 | OK |
| 2 Catraca | 20,9 | 3602 | 7318 / 7235 | 87 | OK |
| 3 Catraca + adm. serializada | 20,8 | 3605 | 7324 / 7241 | 87 | OK |
| 4 Turnos com lote (K = 10) | 44,1 | 1145 | 1978 / 2420 | 20 | OK |

O comportamento é o mesmo do Linux: nenhuma violação, versões 2 e 3 em torno de 21 bab/s, versões 1 e 4 em torno de 44 a 45, esperas simétricas nas versões 2 e 3 e assimetria forte na versão 1. As diferenças de valores (por exemplo, 87 trocas no Windows contra ~99 no Linux nas versões 2 e 3) vêm do escalonador e da resolução do `sleep`, e não da lógica.

## 6. Discussão

1. **Ausência de inanição custa vazão se for obtida só pela ordem de chegada.** As versões 2 e 3 eliminam a assimetria entre os sentidos, mas, como atendem na ordem de chegada, quebram o tráfego em grupos curtos e deixam a corda subutilizada (~19 bab/s contra um teto de 50).
2. **Alternar por lotes recupera quase toda a vazão.** A versão 4 chega a ~88% do teto sem deixar um sentido monopolizar a corda, ao custo de um estado explícito e de uma implementação mais longa.
3. **A versão 1 mostra que um resultado "OK" nos oráculos não basta.** Ela passa em todos os testes de segurança e capacidade, mas tem a pior distribuição de esperas. A vivacidade precisa de métricas próprias (esperas por sentido, trocas de sentido).
4. **A vazão máxima depende só do tempo de travessia.** Com 100% no mesmo sentido todas as versões atingem ~49 bab/s; o custo de sincronização em si é desprezível frente a Tc = 100 ms.

## 7. Limitações

- Os dados vêm de um contêiner de 1 vCPU e de poucas sementes (1 a 3). Os desvios entre sementes são pequenos, mas não calculamos intervalos de confiança. O experimento B4 usa uma única semente por ponto.
- A espera máxima é uma estatística de cauda e varia entre execuções; as comparações qualitativas (ordens de grandeza, simetria entre sentidos) são mais confiáveis que diferenças pequenas.
- A garantia contra inanição nas versões 2 e 3 depende da ordem de liberação dos semáforos da plataforma; a versão 4 garante alternância entre sentidos, mas não ordem dentro de um mesmo sentido. Não verificamos formalmente nenhuma das propriedades.
- A resolução do `sleep` no Windows (~1 ms com `timeBeginPeriod`, ~15 ms sem) pode afetar os valores absolutos. O Tc simulado por `sleep` também ignora a carga de CPU real de uma travessia.

## 8. Conclusão

As quatro versões satisfazem a segurança e a capacidade em todas as execuções testadas (19.200 travessias sem violação), e os argumentos da seção 2.3 explicam por quê. A diferença está na justiça e no desempenho. A versão 1 é rápida, mas favorece um sentido; as versões 2 e 3 são justas entre os sentidos, mas têm cerca de 40% da vazão da versão 1; a versão 4 combina vazão próxima da máxima com alternância entre os sentidos, e o parâmetro K permite ajustar a troca entre as duas. Recomendamos a versão 4 como solução final do problema, com K = 10.

## Referências

1. G. R. Andrews. *Concurrent Programming: Principles and Practice*. Addison-Wesley, 1991.
2. A. S. Tanenbaum. *Modern Operating Systems*. Prentice Hall, 2ª ed., 2001.
3. A. B. Downey. *The Little Book of Semaphores*. Green Tea Press (padrões *turnstile* e *lightswitch*).

## Apêndice A — Como reproduzir

Compilação:

```
Windows (Dev-C++): salvar como .c e compilar com F11
Linux / WSL:       gcc -O2 -Wall -Wextra -pthread babuinos_versoes.c -o babuinos
```

Comandos usados:

```
babuinos                                         # compara as 4 versões (n=100, 50%, Tc=100, g=10)
babuinos -n 200 -p 50 -t 100 -g 10               # configuração base dos experimentos B
babuinos -m 4 -k 10 -n 200 -p 50 -t 100 -g 10    # relatório detalhado da versão 4
babuinos -c -H -n 200 -p 50 -t 100 -g 10 -s 1    # saída em CSV com cabeçalho (semente 1)
```

Parâmetros: `-m` versão (0 = todas, 1 a 4), `-k` lote da versão 4, `-n` babuínos, `-p` % no sentido 0, `-t` Tc em ms, `-g` intervalo médio de chegada em ms (0 = rajada), `-s` semente, `-c` CSV, `-H` cabeçalho do CSV, `-V` registro por babuíno.
