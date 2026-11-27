/*
 * Travessia dos babuínos — TP2 de SO (CEFET-MG)
 * Quatro versões da solução, no mesmo programa, para comparação.
 * Portável: Windows (Dev-C++/MinGW/MSVC, Win32 API) e Linux/WSL (pthreads).
 *
 * Compilar:
 *   Windows (Dev-C++): salve como .c e use F11  (ou: gcc -O2 -o babuinos.exe babuinos_versoes.c)
 *   Linux / WSL:       gcc -O2 -Wall -Wextra -pthread babuinos_versoes.c -o babuinos
 *
 * Uso simples:
 *   babuinos                 roda as 4 versões e mostra uma tabela comparativa
 *   babuinos -m 4            relatório detalhado de uma versão (1 a 4)
 *
 * Parâmetros (todos opcionais):
 *   -m  versão: 0 = todas e compara (padrão), ou 1 a 4
 *   -k  tamanho máximo do lote por sentido na versão 4 (padrão 10)
 *   -n  número de babuínos (padrão 100)
 *   -p  % de babuínos no sentido 0 (padrão 50; 100 = fluxo unidirecional)
 *   -t  tempo médio de travessia em ms, variação ±20% (padrão 100)
 *   -g  intervalo médio entre chegadas em ms (padrão 10; 0 = todos de uma vez)
 *   -s  semente (padrão 42)
 *   -c  saída em CSV (uma linha por versão), para planilhas/scripts
 *   -H  com -c: imprime também o cabeçalho do CSV
 *   -V  imprime chegada/entrada/saída de cada babuíno (stderr)
 *
 * Código de saída: 0 = tudo OK, 1 = alguma violação, 2 = suspeita de deadlock.
 *
 * VERSÕES
 *  1  Ingênua ("lightswitch"): o 1º babuíno de um sentido toma a corda e o
 *     último a libera. Seguro, mas PODE CAUSAR INANIÇÃO (fluxo contínuo num
 *     sentido nunca deixa a corda esvaziar). Serve de linha de base.
 *  2  Lightswitch + catraca (turnstile): quando uma thread do sentido oposto
 *     consegue a catraca, ela impede novas admissões até a corda esvaziar.
 *     A ausência de inanição individual ainda depende da política de
 *     escalonamento e da ordem de liberação das primitivas de sincronização.
 *  3  Catraca + admissão serializada: a catraca só é liberada depois de o
 *     babuíno conseguir também uma vaga na corda. Isso serializa a admissão,
 *     mas NÃO garante FIFO estrito, pois semáforos não garantem ordem de fila.
 *  4  Turnos com lote limitado (passagem de bastão): estado protegido por um
 *     semáforo usado como mutex; limita a K as admissões consecutivas de um
 *     sentido quando há espera no sentido oposto. Isso favorece alternância
 *     entre sentidos, mas não estabelece um limite de tempo individual de espera.
 *
 * Obs.: os textos impressos não usam acentos de propósito (o prompt do
 * Windows costuma mostrar acentos errados).
 */
#ifndef _WIN32
#define _GNU_SOURCE
#endif
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#include <windows.h>
#ifndef STACK_SIZE_PARAM_IS_A_RESERVATION
#define STACK_SIZE_PARAM_IS_A_RESERVATION 0x00010000
#endif
#else
#include <errno.h>
#include <pthread.h>
#include <semaphore.h>
#include <time.h>
#include <unistd.h>
#endif

#define CAPACIDADE 5

/* ====================== camada de portabilidade ======================== */
#ifdef _WIN32
typedef HANDLE sem_w;
typedef CRITICAL_SECTION mx_w;
typedef HANDLE thr_w;
typedef LONG at_t;
typedef DWORD (WINAPI *thr_fn_t)(LPVOID);
#define THR_FN(nome) static DWORD WINAPI nome(LPVOID arg)
#define THR_RET return 0

static void sem_ini(sem_w *s, long ini)
{
    *s = CreateSemaphore(NULL, ini, 1 << 20, NULL);
    if (!*s) { fprintf(stderr, "CreateSemaphore falhou\n"); exit(1); }
}
static void P(sem_w *s) { WaitForSingleObject(*s, INFINITE); }
static void V(sem_w *s) { ReleaseSemaphore(*s, 1, NULL); }
static void mx_ini(mx_w *m) { InitializeCriticalSection(m); }
static void mx_lock(mx_w *m) { EnterCriticalSection(m); }
static void mx_unlock(mx_w *m) { LeaveCriticalSection(m); }
/* leitura/soma atômicas compatíveis com cabeçalhos antigos */
static void at_add(at_t *x, int v) { InterlockedExchangeAdd(x, v); }
static at_t at_get(at_t *x) { return InterlockedExchangeAdd(x, 0); }

static double agora(void)
{
    static LARGE_INTEGER freq;
    LARGE_INTEGER c;
    if (freq.QuadPart == 0) QueryPerformanceFrequency(&freq);
    QueryPerformanceCounter(&c);
    return (double)c.QuadPart / (double)freq.QuadPart;
}
static void dorme_ms(double ms) { if (ms > 0) Sleep((DWORD)(ms + 0.5)); }

/* winmm carregada em tempo de execução (não precisa de -lwinmm) */
typedef UINT (WINAPI *periodo_fn)(UINT);
static periodo_fn p_end;
static void timer_fino(int ligar)
{
    static HMODULE h;
    periodo_fn p_begin;
    if (ligar) {
        h = LoadLibraryA("winmm.dll");
        if (h) {
            p_begin = (periodo_fn)GetProcAddress(h, "timeBeginPeriod");
            p_end = (periodo_fn)GetProcAddress(h, "timeEndPeriod");
            if (p_begin) p_begin(1);        /* Sleep() com resolução de ~1 ms */
        }
    } else if (h && p_end) {
        p_end(1);
    }
}
static int thr_ini(thr_w *t, thr_fn_t f, void *a)
{
    *t = CreateThread(NULL, 64 * 1024, f, a, STACK_SIZE_PARAM_IS_A_RESERVATION, NULL);
    return *t ? 0 : -1;
}
static void thr_join(thr_w t) { WaitForSingleObject(t, INFINITE); CloseHandle(t); }
#define PARAR(c) ExitProcess(c)

#else /* POSIX */
typedef sem_t sem_w;
typedef pthread_mutex_t mx_w;
typedef pthread_t thr_w;
typedef int at_t;
typedef void *(*thr_fn_t)(void *);
#define THR_FN(nome) static void *nome(void *arg)
#define THR_RET return NULL

static void sem_ini(sem_w *s, long ini) { sem_init(s, 0, (unsigned)ini); }
static void P(sem_w *s) { while (sem_wait(s) != 0 && errno == EINTR) { } }
static void V(sem_w *s) { sem_post(s); }
static void mx_ini(mx_w *m) { pthread_mutex_init(m, NULL); }
static void mx_lock(mx_w *m) { pthread_mutex_lock(m); }
static void mx_unlock(mx_w *m) { pthread_mutex_unlock(m); }
static void at_add(at_t *x, int v) { __atomic_fetch_add(x, v, __ATOMIC_SEQ_CST); }
static at_t at_get(at_t *x) { return __atomic_load_n(x, __ATOMIC_SEQ_CST); }

static double agora(void)
{
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return ts.tv_sec + ts.tv_nsec / 1e9;
}
static void dorme_ms(double ms)
{
    time_t s;
    struct timespec ts;
    if (ms <= 0) return;
    s = (time_t)(ms / 1000.0);
    ts.tv_sec = s;
    ts.tv_nsec = (long)((ms - s * 1000.0) * 1e6);
    nanosleep(&ts, NULL);
}
static void timer_fino(int ligar) { (void)ligar; }
static int thr_ini(thr_w *t, thr_fn_t f, void *a)
{
    pthread_attr_t at;
    int r;
    pthread_attr_init(&at);
    pthread_attr_setstacksize(&at, 256 * 1024);
    r = pthread_create(t, &at, f, a);
    pthread_attr_destroy(&at);
    return r;
}
static void thr_join(thr_w t) { pthread_join(t, NULL); }
#define PARAR(c) _exit(c)
#endif

/* ----------------------------- parâmetros ----------------------------- */
static int VERSAO = 0, K_LOTE = 10;
static int N = 100, P_DIR0 = 50, TC_MS = 100, GAP_MS = 10;
static unsigned int SEED = 42;
static int VERBOSE = 0, CSV = 0;

/* ===================== sincronização: versões 1, 2 e 3 ================= */
static sem_w turnstile, vazia, capacidade, mutex_dir[2];
static int cont_dir[2]; /* babuínos admitidos por sentido (mutex_dir[d]) */

/* lightswitch: o primeiro do sentido toma a corda, o último a libera */
static void luz_acende(int d)
{
    P(&mutex_dir[d]);
    if (++cont_dir[d] == 1)
        P(&vazia);
    V(&mutex_dir[d]);
}
static void luz_apaga(int d)
{
    P(&mutex_dir[d]);
    if (--cont_dir[d] == 0)
        V(&vazia);
    V(&mutex_dir[d]);
}

static void entrar_v1(int d)        /* ingênua: sem catraca */
{
    luz_acende(d);
    P(&capacidade);
}
static void entrar_v2(int d)        /* catraca */
{
    P(&turnstile);
    luz_acende(d);
    V(&turnstile);
    P(&capacidade);
}
static void entrar_v3(int d)        /* catraca com FIFO estrito */
{
    P(&turnstile);
    luz_acende(d);
    P(&capacidade);                 /* só libera a catraca com a vaga em mãos */
    V(&turnstile);
}
static void sair_v123(int d)
{
    V(&capacidade);
    luz_apaga(d);
}

/* ===================== sincronização: versão 4 (turnos) ================ */
static sem_w mtx, fila[2];
static int ncorda, dir_atual = -1, espera[2], consec;

static void admite(int d)
{
    if (dir_atual != d) { dir_atual = d; consec = 0; }
    ncorda++;
    consec++;
}

static void entrar_v4(int d)
{
    P(&mtx);
    if (ncorda == 0 ||
        (dir_atual == d && ncorda < CAPACIDADE && espera[d] == 0 &&
         (espera[1 - d] == 0 || consec < K_LOTE))) {
        admite(d);
        V(&mtx);
    } else {
        espera[d]++;
        V(&mtx);
        P(&fila[d]);    /* quem sair nos admite (já contado em ncorda) */
    }
}

static void sair_v4(int d)
{
    int prox, n, lim, i;

    P(&mtx);
    ncorda--;
    if (ncorda == 0) {
        /* corda vazia: dá a vez ao sentido oposto, se houver espera */
        prox = espera[1 - d] > 0 ? 1 - d : d;
        if (espera[prox] > 0) {
            dir_atual = prox;
            consec = 0;
            lim = K_LOTE < CAPACIDADE ? K_LOTE : CAPACIDADE;
            n = espera[prox] < lim ? espera[prox] : lim;
            for (i = 0; i < n; i++) {
                ncorda++;
                consec++;
                espera[prox]--;
                V(&fila[prox]);
            }
        } else {
            dir_atual = -1;
            consec = 0;
        }
    } else if (espera[d] > 0 && (espera[1 - d] == 0 || consec < K_LOTE)) {
        /* abriu uma vaga e o lote ainda não acabou */
        ncorda++;
        consec++;
        espera[d]--;
        V(&fila[d]);
    }
    V(&mtx);
}

/* ------------------------- despacho por versão ------------------------- */
static void entrar_corda(int d)
{
    switch (VERSAO) {
    case 1: entrar_v1(d); break;
    case 2: entrar_v2(d); break;
    case 3: entrar_v3(d); break;
    default: entrar_v4(d); break;
    }
}
static void sair_corda(int d)
{
    if (VERSAO == 4) sair_v4(d);
    else sair_v123(d);
}

/* ------------------------ oráculos e instrumentação ------------------- */
static mx_w oraculo;    /* independe da solução testada */
static int na_corda[2], max_total, viol_sentido, viol_cap;
static int ultimo_dir = -1, trocas;

static at_t esperando, concluidos, fim;

typedef struct {
    int id, dir;
    unsigned int seed;
    double t_chegada, t_entrada, t_saida;
    thr_w th;
} babuino_t;

/* gerador pseudoaleatório próprio (LCG), estado local => thread-safe */
static double rnd(unsigned int *s)
{
    *s = *s * 1664525u + 1013904223u;
    return (double)(*s >> 8) / 16777216.0;      /* [0,1) */
}

static void atravessa(babuino_t *b)
{
    int d = b->dir, total;

    mx_lock(&oraculo);
    na_corda[d]++;
    total = na_corda[0] + na_corda[1];
    if (total > max_total) max_total = total;
    if (na_corda[1 - d] > 0) viol_sentido++;    /* sentidos opostos se encontraram */
    if (total > CAPACIDADE) viol_cap++;         /* corda rompeu */
    if (ultimo_dir != d) { trocas++; ultimo_dir = d; }
    mx_unlock(&oraculo);

    dorme_ms(TC_MS * (0.8 + 0.4 * rnd(&b->seed)));  /* ±20% */

    mx_lock(&oraculo);
    na_corda[d]--;
    mx_unlock(&oraculo);
}

THR_FN(babuino)
{
    babuino_t *b = (babuino_t *)arg;

    b->t_chegada = agora();
    at_add(&esperando, 1);

    entrar_corda(b->dir);

    b->t_entrada = agora();
    at_add(&esperando, -1);

    atravessa(b);
    b->t_saida = agora();

    sair_corda(b->dir);
    at_add(&concluidos, 1);
    THR_RET;
}

/* Watchdog: 10 s sem travessia concluída com babuínos esperando */
THR_FN(watchdog)
{
    at_t ultimo = 0, c;
    double t_prog = agora();
    (void)arg;

    while (!at_get(&fim)) {
        dorme_ms(100);
        c = at_get(&concluidos);
        if (c != ultimo) {
            ultimo = c;
            t_prog = agora();
        } else if (at_get(&esperando) > 0 && agora() - t_prog > 10.0) {
            fprintf(stderr, "\nSUSPEITA DE DEADLOCK (versao %d): %ld concluidos, %ld esperando\n",
                    VERSAO, (long)c, (long)at_get(&esperando));
            fflush(stderr);
            PARAR(2);
        }
    }
    THR_RET;
}

/* ============================ execução de uma versão ==================== */
typedef struct {
    int versao, concluidos, trocas, max_total, viol_sentido, viol_cap, ok;
    double T, vazao, espera_media, espera_max, emax_d[2];
} resultado_t;

static void reinicia(void)
{
    cont_dir[0] = cont_dir[1] = 0;
    ncorda = 0; dir_atual = -1; espera[0] = espera[1] = 0; consec = 0;
    na_corda[0] = na_corda[1] = 0;
    max_total = 0; viol_sentido = 0; viol_cap = 0; ultimo_dir = -1; trocas = 0;
    esperando = 0; concluidos = 0; fim = 0;

    sem_ini(&turnstile, 1);
    sem_ini(&vazia, 1);
    sem_ini(&capacidade, CAPACIDADE);
    sem_ini(&mutex_dir[0], 1);
    sem_ini(&mutex_dir[1], 1);
    sem_ini(&mtx, 1);
    sem_ini(&fila[0], 0);
    sem_ini(&fila[1], 0);
}

/* roda uma versão do início ao fim e preenche o resultado; -1 em caso de erro */
static int executa(int versao, resultado_t *r)
{
    int i;
    babuino_t *bs;
    unsigned int rs;
    thr_w wd;
    double t0, soma = 0, e;

    VERSAO = versao;
    reinicia();

    bs = (babuino_t *)calloc((size_t)N, sizeof *bs);
    if (!bs) { fprintf(stderr, "sem memoria\n"); return -1; }

    /* mesma semente => mesma sequência de babuínos em todas as versões */
    rs = SEED ? SEED : 1;
    for (i = 0; i < N; i++) {
        bs[i].id = i;
        bs[i].dir = (int)(rnd(&rs) * 100.0) < P_DIR0 ? 0 : 1;
        bs[i].seed = SEED * 2654435761u + (unsigned)i * 40503u + 1u;
    }

    thr_ini(&wd, watchdog, NULL);

    t0 = agora();
    for (i = 0; i < N; i++) {
        if (i > 0 && GAP_MS > 0)
            dorme_ms(2.0 * GAP_MS * rnd(&rs));      /* média = GAP_MS */
        if (thr_ini(&bs[i].th, babuino, &bs[i]) != 0) {
            fprintf(stderr, "falha ao criar thread (i=%d)\n", i);
            free(bs);
            return -1;
        }
    }
    for (i = 0; i < N; i++)
        thr_join(bs[i].th);
    r->T = agora() - t0;

    at_add(&fim, 1);
    thr_join(wd);

    r->espera_max = 0;
    r->emax_d[0] = r->emax_d[1] = 0;
    for (i = 0; i < N; i++) {
        e = (bs[i].t_entrada - bs[i].t_chegada) * 1000.0;
        soma += e;
        if (e > r->espera_max) r->espera_max = e;
        if (e > r->emax_d[bs[i].dir]) r->emax_d[bs[i].dir] = e;
        if (VERBOSE)
            fprintf(stderr, "bab %4d dir=%d chegada=%8.1f entrada=%8.1f saida=%8.1f ms\n",
                    bs[i].id, bs[i].dir, (bs[i].t_chegada - t0) * 1000,
                    (bs[i].t_entrada - t0) * 1000, (bs[i].t_saida - t0) * 1000);
    }

    r->versao = versao;
    r->concluidos = (int)at_get(&concluidos);
    r->trocas = trocas;
    r->max_total = max_total;
    r->viol_sentido = viol_sentido;
    r->viol_cap = viol_cap;
    r->vazao = N / r->T;
    r->espera_media = soma / N;
    r->ok = (viol_sentido == 0 && viol_cap == 0 && max_total <= CAPACIDADE &&
             r->concluidos == N);

    free(bs);
    return 0;
}

/* ================================ saídas ================================ */
static const char *nome_versao(int v, char *buf)
{
    switch (v) {
    case 1: return "Ingenua (sem catraca)";
    case 2: return "Catraca";
    case 3: return "Catraca + admissao serializada";
    default: sprintf(buf, "Turnos com lote (k=%d)", K_LOTE); return buf;
    }
}

/* maior espera / menor espera entre os dois sentidos (0 = não se aplica) */
static double desigualdade(const resultado_t *r)
{
    double a = r->emax_d[0], b = r->emax_d[1];
    double mx = a > b ? a : b, mn = a > b ? b : a;
    return mn > 0 ? mx / mn : 0;
}

static void imprime_parametros(void)
{
    printf("Parametros: %d babuinos | %d%% no sentido 0 | travessia %d ms | "
           "chegada media %d ms | semente %u\n", N, P_DIR0, TC_MS, GAP_MS, SEED);
}

static void imprime_csv(const resultado_t *r, int cabecalho)
{
    if (cabecalho)
        printf("versao,n,p_dir0,tc_ms,gap_ms,seed,k_lote,T_s,vazao_bab_s,espera_media_ms,"
               "espera_max_ms,espera_max_dir0_ms,espera_max_dir1_ms,trocas_sentido,"
               "max_total,viol_sentido,viol_cap,resultado\n");
    printf("%d,%d,%d,%d,%d,%u,%d,%.3f,%.2f,%.2f,%.2f,%.2f,%.2f,%d,%d,%d,%d,%s\n",
           r->versao, N, P_DIR0, TC_MS, GAP_MS, SEED, r->versao == 4 ? K_LOTE : 0,
           r->T, r->vazao, r->espera_media, r->espera_max, r->emax_d[0], r->emax_d[1],
           r->trocas, r->max_total, r->viol_sentido, r->viol_cap, r->ok ? "OK" : "FALHA");
}

static void imprime_detalhe(const resultado_t *r)
{
    char buf[64];
    double des = desigualdade(r);

    printf("\n=== Travessia dos babuinos - versao %d: %s ===\n", r->versao,
           nome_versao(r->versao, buf));
    imprime_parametros();

    printf("\nVERIFICACAO\n");
    printf("  %-40s: %-5s (%d violacoes)\n", "Seguranca (sentidos opostos na corda)",
           r->viol_sentido == 0 ? "OK" : "FALHA", r->viol_sentido);
    printf("  %-40s: %-5s (maximo %d de %d)\n", "Capacidade (maximo de babuinos na corda)",
           (r->viol_cap == 0 && r->max_total <= CAPACIDADE) ? "OK" : "FALHA",
           r->max_total, CAPACIDADE);
    printf("  %-40s: %-5s (%d de %d)\n", "Vivacidade (todos atravessaram)",
           r->concluidos == N ? "OK" : "FALHA", r->concluidos, N);

    printf("\nDESEMPENHO\n");
    printf("  Tempo total          : %.2f s\n", r->T);
    printf("  Vazao                : %.1f babuinos/s\n", r->vazao);
    printf("  Espera media         : %.0f ms\n", r->espera_media);
    printf("  Espera maxima        : %.0f ms (sentido 0: %.0f ms | sentido 1: %.0f ms)\n",
           r->espera_max, r->emax_d[0], r->emax_d[1]);
    if (des > 0)
        printf("  Desigualdade         : %.1fx (maior espera / menor espera entre os sentidos)\n", des);
    printf("  Trocas de sentido    : %d (grupo medio de %.1f babuinos)\n",
           r->trocas, r->trocas > 0 ? (double)N / r->trocas : 0.0);

    printf("\nRESULTADO: %s\n", r->ok ? "OK" : "FALHA");
}

static void imprime_tabela(const resultado_t *rs, int n)
{
    int i;
    char buf[64], dbuf[16];
    double des;

    printf("\n=== Travessia dos babuinos - comparacao das versoes ===\n");
    imprime_parametros();
    printf("\n");
    printf(" V  Solucao                   Vazao   Espera   Espera max (ms)    Desigual.  Trocas  Resultado\n");
    printf("                            (bab/s)  med (ms)  sent.0   sent.1\n");
    printf("--------------------------------------------------------------------------------------------\n");
    for (i = 0; i < n; i++) {
        des = desigualdade(&rs[i]);
        if (des > 0) sprintf(dbuf, "%.1fx", des);
        else sprintf(dbuf, "-");
        printf(" %d  %-24s %7.1f %8.0f %8.0f %8.0f %11s %7d  %s\n",
               rs[i].versao, nome_versao(rs[i].versao, buf), rs[i].vazao,
               rs[i].espera_media, rs[i].emax_d[0], rs[i].emax_d[1], dbuf,
               rs[i].trocas, rs[i].ok ? "OK" : "FALHA");
    }
    printf("--------------------------------------------------------------------------------------------\n");
    printf("Como ler: vazao maior = melhor. Desigualdade proxima de 1x = justo; valor alto = um\n");
    printf("sentido espera muito mais que o outro (inanicao). Poucas trocas = grupos grandes.\n");
}

/* -------------------------------- main -------------------------------- */
int main(int argc, char **argv)
{
    int header = 0, i, v, falhou = 0;
    resultado_t rs[4], um;

    for (i = 1; i < argc; i++) {
        if (!strcmp(argv[i], "-H")) header = 1;
        else if (!strcmp(argv[i], "-c")) CSV = 1;
        else if (!strcmp(argv[i], "-V")) VERBOSE = 1;
        else if (i + 1 < argc && !strcmp(argv[i], "-m")) VERSAO = atoi(argv[++i]);
        else if (i + 1 < argc && !strcmp(argv[i], "-k")) K_LOTE = atoi(argv[++i]);
        else if (i + 1 < argc && !strcmp(argv[i], "-n")) N = atoi(argv[++i]);
        else if (i + 1 < argc && !strcmp(argv[i], "-p")) P_DIR0 = atoi(argv[++i]);
        else if (i + 1 < argc && !strcmp(argv[i], "-t")) TC_MS = atoi(argv[++i]);
        else if (i + 1 < argc && !strcmp(argv[i], "-g")) GAP_MS = atoi(argv[++i]);
        else if (i + 1 < argc && !strcmp(argv[i], "-s")) SEED = (unsigned)atoi(argv[++i]);
        else {
            fprintf(stderr, "uso: %s [-m 0..4] [-k lote] [-n N] [-p %%dir0] [-t Tc_ms] "
                            "[-g gap_ms] [-s seed] [-c] [-H] [-V]\n", argv[0]);
            return 64;
        }
    }
    if (N <= 0 || VERSAO < 0 || VERSAO > 4 || K_LOTE < 1 ||
        P_DIR0 < 0 || P_DIR0 > 100 || TC_MS < 0 || GAP_MS < 0) {
        fprintf(stderr, "parametros invalidos (N>0, versao 0..4, lote>=1)\n");
        return 64;
    }

    timer_fino(1);
    mx_ini(&oraculo);

    if (VERSAO == 0) {
        for (v = 1; v <= 4; v++) {
            if (!CSV) {
                printf("Executando versao %d de 4...\r", v);
                fflush(stdout);
            }
            if (executa(v, &rs[v - 1]) != 0) return 1;
            if (!rs[v - 1].ok) falhou = 1;
            if (CSV) imprime_csv(&rs[v - 1], header && v == 1);
        }
        if (!CSV) imprime_tabela(rs, 4);
    } else {
        if (executa(VERSAO, &um) != 0) return 1;
        falhou = !um.ok;
        if (CSV) imprime_csv(&um, header);
        else imprime_detalhe(&um);
    }

    timer_fino(0);
    return falhou ? 1 : 0;
}