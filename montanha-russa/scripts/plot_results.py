#!/usr/bin/env python3
"""Gera gráficos e tabelas a partir de results/medicoes.csv.

Uso:
    python scripts/plot_results.py
    python scripts/plot_results.py --csv results/medicoes_quick.csv --summary-md results/resumo.md
"""

from __future__ import annotations

import argparse
from pathlib import Path

import pandas as pd

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402  (após escolher o backend)

ROOT = Path(__file__).resolve().parent.parent

SCENARIOS = {
    "fixa": "Cenário 1 — n fixo (200)",
    "proporcional": "Cenário 2 — n = 2·m·C",
    "sem-trabalho": "Cenário 3 — sem trabalho simulado",
}
COLORS = {2: "#1f77b4", 4: "#ff7f0e", 8: "#2ca02c"}
NUMERIC = ["T_s", "throughput", "latency_ms", "cpu_s", "speedup", "efficiency", "overhead_ms"]
REQUIRED = {"scenario", "m", "C", "n", "rep", *NUMERIC}


def read_data(path: Path) -> pd.DataFrame:
    df = pd.read_csv(path)
    missing = REQUIRED - set(df.columns)
    if missing:
        raise ValueError(f"Colunas ausentes em {path}: {sorted(missing)}")

    for column in NUMERIC:
        df[column] = pd.to_numeric(df[column], errors="coerce")

    # Uso de CPU como porcentagem de um núcleo: cpu_s / T_s.
    df["cpu_pct"] = df["cpu_s"] / df["T_s"] * 100.0
    return df


def aggregate(df: pd.DataFrame) -> pd.DataFrame:
    return (
        df.groupby(["scenario", "C", "m"], as_index=False)
        .agg(
            n=("n", "first"),
            T=("T_s", "mean"),
            T_std=("T_s", "std"),
            throughput=("throughput", "mean"),
            throughput_std=("throughput", "std"),
            latency=("latency_ms", "mean"),
            latency_std=("latency_ms", "std"),
            speedup=("speedup", "mean"),
            efficiency=("efficiency", "mean"),
            overhead=("overhead_ms", "mean"),
            cpu_pct=("cpu_pct", "mean"),
            cpu_std=("cpu_pct", "std"),
        )
        .sort_values(["scenario", "C", "m"])
    )


def filtered(df: pd.DataFrame, scenario_arg: str | None) -> pd.DataFrame:
    if scenario_arg is None:
        return df
    wanted = {"1": "fixa", "2": "proporcional", "3": "sem-trabalho"}[scenario_arg]
    return df[df["scenario"] == wanted]


def style_axis(ax, title: str, ylabel: str) -> None:
    ax.set_title(title, fontsize=10)
    ax.set_xlabel("carros (m)")
    ax.set_ylabel(ylabel)
    ax.grid(True, alpha=0.3)
    ax.set_axisbelow(True)


def plot_panels(
    data: pd.DataFrame,
    value: str,
    err: str | None,
    ylabel: str,
    filename: str,
    outdir: Path,
    title: str,
) -> None:
    """Um painel por cenário; linhas por capacidade C, com banda de desvio."""
    fig, axes = plt.subplots(1, 3, figsize=(16, 4.6), sharex=True)
    for ax, (key, label) in zip(axes, SCENARIOS.items()):
        subset = data[data["scenario"] == key]
        for capacity, group in subset.groupby("C"):
            group = group.sort_values("m")
            color = COLORS.get(int(capacity))
            ax.plot(
                group["m"], group[value], marker="o", linewidth=1.6,
                color=color, label=f"C={capacity}",
            )
            if err and group[err].notna().any():
                ax.fill_between(
                    group["m"], group[value] - group[err], group[value] + group[err],
                    alpha=0.15, color=color,
                )
        style_axis(ax, label, ylabel)
        if subset.empty:
            ax.text(0.5, 0.5, "sem dados", ha="center", va="center", transform=ax.transAxes)
        else:
            ax.legend(fontsize=8)

    fig.suptitle(title, fontsize=13)
    fig.tight_layout()
    fig.savefig(outdir / filename, dpi=150)
    plt.close(fig)


def plot_speedup(data: pd.DataFrame, outdir: Path) -> None:
    subset = data[data["scenario"] == "fixa"]
    if subset.empty:
        return

    fig, axes = plt.subplots(1, 2, figsize=(11, 4.4))
    for ax, (column, ylabel, ideal) in zip(
        axes,
        [
            ("speedup", "speedup S(m)", True),
            ("efficiency", "eficiência E(m)", True),
        ],
    ):
        for capacity, group in subset.groupby("C"):
            group = group.sort_values("m")
            color = COLORS.get(int(capacity))
            ax.plot(group["m"], group[column], marker="o", color=color, label=f"C={capacity}")
        if ideal:
            m = sorted(subset["m"].unique())
            if column == "speedup":
                ax.plot(m, m, "k--", linewidth=1, label="ideal S=m")
            else:
                ax.axhline(1.0, color="k", linestyle="--", linewidth=1, label="ideal E=1")
        style_axis(ax, SCENARIOS["fixa"], ylabel)
        ax.legend(fontsize=8)

    fig.suptitle("Cenário 1: escalabilidade", fontsize=13)
    fig.tight_layout()
    fig.savefig(outdir / "speedup_eficiencia.png", dpi=150)
    plt.close(fig)


def summary_markdown(data: pd.DataFrame) -> str:
    lines = [
        "# Resumo agregado das medições",
        "",
        "Cada célula é a média das repetições; T em segundos, λ em passageiros/s,",
        "latência e T/volta em ms. `—` indica dado não medido.",
        "",
    ]
    for key, label in SCENARIOS.items():
        subset = data[data["scenario"] == key]
        if subset.empty:
            continue
        lines += [f"## {label}", ""]
        lines.append("| C | m | n | T (s) | λ (pass/s) | latência (ms) | S | E | T/volta (ms) |")
        lines.append("|---:|---:|---:|---:|---:|---:|---:|---:|---:|")
        for row in subset.itertuples(index=False):
            def fmt(value, digits=3):
                return "—" if pd.isna(value) else f"{value:.{digits}f}"
            lines.append(
                f"| {row.C} | {row.m} | {row.n} | {fmt(row.T)} | {fmt(row.throughput, 1)} "
                f"| {fmt(row.latency, 2)} | {fmt(row.speedup)} | {fmt(row.efficiency)} "
                f"| {fmt(row.overhead, 3)} |"
            )
        lines.append("")
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--csv", type=Path, default=ROOT / "results" / "medicoes.csv")
    parser.add_argument("--out", type=Path, default=ROOT / "results" / "graficos")
    parser.add_argument("--summary-md", type=Path, help="salva também uma tabela em Markdown")
    parser.add_argument("--scenario", choices=["1", "2", "3"], help="apenas um cenário")
    args = parser.parse_args()

    raw = filtered(read_data(args.csv), args.scenario)
    if raw.empty:
        raise SystemExit(f"Nenhum dado em {args.csv}")

    data = aggregate(raw)
    args.out.mkdir(parents=True, exist_ok=True)

    plot_panels(data, "T", "T_std", "T (s)", "tempo_total.png", args.out,
                "Tempo total até todas as voltas terminarem")
    plot_panels(data, "throughput", "throughput_std", "passageiros/s", "vazao.png", args.out,
                "Vazão")
    plot_panels(data, "latency", "latency_std", "ms", "latencia.png", args.out,
                "Latência média de espera até o embarque")
    plot_panels(data, "overhead", None, "ms por volta", "sobrecarga.png", args.out,
                "Tempo por volta T/(m·R) — cenário 3 isola a sobrecarga de sincronização")
    plot_panels(data, "cpu_pct", "cpu_std", "% de um núcleo", "cpu.png", args.out,
                "Uso de CPU por configuração")
    plot_speedup(data, args.out)

    summary = summary_markdown(data)
    if args.summary_md:
        args.summary_md.parent.mkdir(parents=True, exist_ok=True)
        args.summary_md.write_text(summary, encoding="utf-8")
        print(f"Tabela: {args.summary_md}")
    else:
        print(summary)

    print(f"Gráficos salvos em {args.out}")


if __name__ == "__main__":
    main()
