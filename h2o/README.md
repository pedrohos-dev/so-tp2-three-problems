# Construindo H2O

Implementacao concorrente do problema H2O em C com threads POSIX e semaforos.
Cada molecula e formada por duas threads de hidrogenio e uma de oxigenio.

## Compilar e executar

Requer um compilador C e pthreads (por exemplo, `gcc` no Linux):

```sh
make
./h2o HOH
./h2o OOHHHH
make test
```

O programa recebe a sequencia de chegada como argumento e valida a composicao
de cada grupo de tres atomos na ordem em que `bond` foi executada. A entrada
precisa conter somente `H` e `O`, com dois hidrogenios para cada oxigenio.

Consulte [RELATORIO.md](RELATORIO.md) para as decisoes de sincronizacao e os
resultados dos testes.