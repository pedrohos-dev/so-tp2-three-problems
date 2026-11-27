#!/bin/sh
set -eu

run_case() {
    sequence=$1
    molecules=$2
    output=$(timeout 10s ./h2o "$sequence")
    printf '%s\n' "$output" | grep -Fq "OK: $molecules molecula(s)"
}

run_case HOH 1
run_case OOHHHH 2
run_case HHHOOH 2

stress_sequence=
iteration=0
while [ "$iteration" -lt 10 ]; do
    stress_sequence="${stress_sequence}HHOHOHOHH"
    iteration=$((iteration + 1))
done

run_case HHO 1
run_case HOH 1
run_case OHH 1

iteration=0
while [ "$iteration" -lt 20 ]; do
    run_case "$stress_sequence" 30
    iteration=$((iteration + 1))
done

if timeout 10s ./h2o HHOH >/dev/null 2>&1; then
    echo "Falha: uma entrada com numero invalido de atomos foi aceita." >&2
    exit 1
fi

if timeout 10s ./h2o HHX >/dev/null 2>&1; then
    echo "Falha: uma entrada com simbolo invalido foi aceita." >&2
    exit 1
fi

echo "Testes H2O passaram."
