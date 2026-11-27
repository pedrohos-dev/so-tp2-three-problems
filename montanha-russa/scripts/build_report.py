#!/usr/bin/env python3
"""Converte RELATORIO.md em RELATORIO.pdf (A4) usando matplotlib.

Suporta: títulos, parágrafos, listas, blocos de código, tabelas Markdown,
imagens e '---' como quebra de página. O número de páginas é impresso no fim.
"""

from __future__ import annotations

import argparse
import re
import textwrap
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
from matplotlib.backends.backend_agg import FigureCanvasAgg  # noqa: E402
from matplotlib.figure import Figure  # noqa: E402
from matplotlib.image import imread  # noqa: E402
from matplotlib.backends.backend_pdf import PdfPages  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent

PAGE_W, PAGE_H = 8.27, 11.69  # A4 em polegadas
LEFT, RIGHT, TOP, BOTTOM = 0.09, 0.91, 0.94, 0.07
WRAP = 104


class Document:
    def __init__(self, source: Path, destination: Path) -> None:
        self.source = source
        self.destination = destination
        self.pdf = PdfPages(destination)
        self.fig: Figure | None = None
        self.y = TOP
        self.page = 0
        self.new_page()

    def new_page(self) -> None:
        self.finish_page()
        self.fig = Figure(figsize=(PAGE_W, PAGE_H), dpi=100)
        FigureCanvasAgg(self.fig)
        self.y = TOP
        self.page += 1

    def finish_page(self) -> None:
        if self.fig is None:
            return
        self.fig.text(0.5, 0.035, f"{self.page}", ha="center", fontsize=8, color="0.55")
        self.pdf.savefig(self.fig)
        self.fig = None

    def ensure(self, height: float) -> None:
        if self.y - height < BOTTOM:
            self.new_page()

    @staticmethod
    def line_height(fontsize: float) -> float:
        return fontsize * 1.38 / 72 / PAGE_H

    def text_block(self, text: str, fontsize: float, *, bold: bool = False,
                   mono: bool = False, space_before: float = 0.0) -> None:
        lines = text.splitlines() or [""]
        line_h = self.line_height(fontsize)
        needed = space_before + line_h * len(lines) + 0.006
        self.ensure(needed)
        self.y -= space_before
        weight = "bold" if bold else "normal"
        family = "DejaVu Sans Mono" if mono else "DejaVu Sans"
        assert self.fig is not None
        self.fig.text(
            LEFT, self.y, text, fontsize=fontsize, va="top", ha="left",
            fontweight=weight, family=family, linespacing=1.32,
        )
        self.y -= line_h * len(lines) + 0.006

    def paragraph(self, text: str) -> None:
        clean = text.replace("**", "").replace("`", "")
        wrapped = textwrap.fill(clean, width=WRAP, break_long_words=False)
        self.text_block(wrapped, 9.0)

    def image(self, path: Path, alt: str) -> None:
        if not path.exists():
            self.text_block(f"[imagem não encontrada: {path}]", 8.0)
            return
        height = 0.265
        self.ensure(height + 0.045)
        assert self.fig is not None
        ax = self.fig.add_axes([LEFT, self.y - height, RIGHT - LEFT, height])
        ax.imshow(imread(path))
        ax.axis("off")
        self.y -= height + 0.012
        self.text_block(textwrap.fill(alt, width=WRAP), 7.5)

    def table(self, rows: list[list[str]]) -> None:
        if not rows:
            return
        widths = [max(len(row[i]) for row in rows) for i in range(len(rows[0]))]
        lines = []
        for row in rows:
            lines.append("  ".join(cell.ljust(widths[i]) for i, cell in enumerate(row)).rstrip())

        longest = max(len(line) for line in lines)
        fontsize = min(8.0, 8.0 * WRAP / max(longest, 1))
        fontsize = max(fontsize, 5.2)
        self.text_block("\n".join(lines), fontsize, mono=True)

    def parse_table_row(self, line: str) -> list[str]:
        return [cell.strip().replace("**", "").replace("`", "")
                for cell in line.strip().strip("|").split("|")]

    @staticmethod
    def is_separator(row: list[str]) -> bool:
        return all(re.fullmatch(r":?-{2,}:?", cell.replace(" ", "")) for cell in row)

    def build(self) -> int:
        lines = self.source.read_text(encoding="utf-8").splitlines()
        i = 0
        while i < len(lines):
            line = lines[i].rstrip()
            stripped = line.strip()

            if not stripped:
                i += 1
                continue

            if stripped == "---":
                if self.y < TOP - 0.02:
                    self.new_page()
                i += 1
                continue

            if stripped.startswith("```"):
                i += 1
                code: list[str] = []
                while i < len(lines) and not lines[i].strip().startswith("```"):
                    code.append(lines[i].rstrip())
                    i += 1
                i += 1
                wrapped = "\n".join(
                    textwrap.fill(c, width=WRAP, break_long_words=False) for c in code
                )
                self.text_block(wrapped, 7.6, mono=True)
                continue

            image_match = re.match(r"!\[(.*?)\]\((.*?)\)", stripped)
            if image_match:
                alt, relative = image_match.groups()
                self.image((self.source.parent / relative).resolve(), alt)
                i += 1
                continue

            if stripped.startswith("|"):
                rows: list[list[str]] = []
                while i < len(lines) and lines[i].strip().startswith("|"):
                    row = self.parse_table_row(lines[i])
                    if not self.is_separator(row):
                        rows.append(row)
                    i += 1
                self.table(rows)
                continue

            if stripped.startswith("### "):
                self.text_block(stripped[4:], 10.5, bold=True, space_before=0.018)
                i += 1
                continue

            if stripped.startswith("## "):
                self.text_block(stripped[3:], 13.0, bold=True, space_before=0.03)
                i += 1
                continue

            if stripped.startswith("# "):
                self.text_block(stripped[2:], 17.0, bold=True, space_before=0.01)
                i += 1
                continue

            if stripped.startswith("- "):
                items: list[str] = []
                while i < len(lines) and lines[i].strip().startswith("- "):
                    items.append("• " + lines[i].strip()[2:].replace("**", "").replace("`", ""))
                    i += 1
                wrapped = "\n".join(
                    textwrap.fill(item, width=WRAP, subsequent_indent="  ")
                    for item in items
                )
                self.text_block(wrapped, 9.0)
                continue

            paragraph: list[str] = []
            while i < len(lines):
                candidate = lines[i].strip()
                if (not candidate or candidate == "---" or candidate.startswith(("#", "|", "```", "- ", "!["))):
                    break
                paragraph.append(candidate)
                i += 1
            if paragraph:
                self.paragraph(" ".join(paragraph))

        self.finish_page()
        self.pdf.close()
        return self.page


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=ROOT / "RELATORIO.md")
    parser.add_argument("--output", type=Path, default=ROOT / "RELATORIO.pdf")
    args = parser.parse_args()

    pages = Document(args.source, args.output).build()
    print(f"{args.output} gerado com {pages} página(s)")
    if pages > 15:
        raise SystemExit("ERRO: o relatório excede o limite de 15 páginas")


if __name__ == "__main__":
    main()
