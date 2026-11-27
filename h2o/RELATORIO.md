# Relatorio — Construindo H2O

## Problema e solucao

Cada molecula de agua requer duas threads de hidrogenio e uma de oxigenio.
Threads excedentes devem aguardar; alem disso, todas as chamadas `bond` de uma
molecula precisam ocorrer antes de qualquer chamada da molecula seguinte.

A implementacao em C usa threads POSIX, um mutex para proteger o estado e
semaforos separados para as filas de hidrogenio e oxigenio. Quando ha dois H e
um O aguardando, o coordenador libera exatamente essas tres vagas. A proxima
molecula so e liberada quando as tres threads liberadas ja executaram `bond`.
Assim, cada trecho consecutivo de tres ligacoes contem dois H e um O.

## Testes

`make test` compila com avisos tratados como erros e verifica os exemplos
`HOH`, `OOHHHH` e `HHHOOH`, alem de 20 execucoes concorrentes com 30 moleculas
cada. Tambem confirma a rejeicao de entradas com composicao ou simbolos
invalidos. O programa valida os grupos produzidos e usa um limite de tempo nos
testes para detectar bloqueios.

## Execucao

No diretorio `h2o`, execute `make test`. Para testar uma sequencia propria,
use `./h2o SEQUENCIA`, por exemplo `./h2o OOHHHH`.
