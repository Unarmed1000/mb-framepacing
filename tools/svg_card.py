#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""The repository's dark chart card as SVG, for the scripts that draw one (pacer_capture_report.py, frame_stages_chart.py): the
look of measure/libs/MB.FramePacing.Charts/source/SvgMarkup.cs, its text and its colours. A script adds the classes only it
uses (Svg's extra_style), so one script's chart does not change when another gets a new colour.
"""

TICKS_PER_MS = 10_000


def ms(ticks: int, digits: int = 3) -> str:
    return f"{ticks / TICKS_PER_MS:.{digits}f}"


# The look of the repository's charts (measure/libs/MB.FramePacing.Charts/source/SvgMarkup.cs): the dark card, its text and its colours
STYLE = """
  text { font-family: "Segoe UI Variable Text", "Segoe UI", Inter, system-ui, -apple-system, "Helvetica Neue", Arial, sans-serif;
         font-size: 13px; fill: #e6edf3; font-variant-numeric: tabular-nums; }
  .card { fill: #1f242b; stroke: #8b949e; stroke-opacity: 0.25; stroke-width: 1; }
  .title { font-size: 20px; font-weight: 600; letter-spacing: -0.01em; }
  .sub { fill: #8b949e; }
  .label { font-size: 11px; font-weight: 600; letter-spacing: 0.08em; fill: #8b949e; }
  .axis { font-size: 12px; fill: #c9d1d9; }
  .value { font-size: 11px; fill: #c9d1d9; }
  .grid { stroke: #ffffff; stroke-opacity: 0.1; stroke-width: 1; }
  .zero-line { stroke: #ffffff; stroke-opacity: 0.45; stroke-width: 1; }
  .band { fill: #ffffff; fill-opacity: 0.06; }
  .green { fill: #2ea043; }
  .green-faint { fill: #2ea043; fill-opacity: 0.45; }
  .blue { fill: #58a6ff; }
  .blue-faint { fill: #58a6ff; fill-opacity: 0.45; }
  .red { fill: #e5534b; }
  .amber { fill: #d29922; }
  .line { fill: none; stroke-width: 1.6; stroke-linejoin: round; }
  .line-blue { stroke: #58a6ff; }
  .line-green { stroke: #2ea043; }
  .line-amber { stroke: #d29922; }
  .line-pink { stroke: #db61a2; }
  .threshold { stroke: #e5534b; stroke-width: 1.5; stroke-dasharray: 6 4; }
  .threshold-text { fill: #ff7b72; font-size: 12px; }
  .mark { stroke: #ffffff; stroke-opacity: 0.45; stroke-width: 1; stroke-dasharray: 2 4; }
"""


def coordinate(value: float) -> str:
    text = f"{value:.1f}"
    return text.removesuffix(".0")


def escape(text: str) -> str:
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


class Svg:
    """A chart on the repository's dark card: a title, a line under it, and what is drawn into it."""

    def __init__(self, width: int, height: int, title: str, subtitle: str, extra_style: str = "") -> None:
        self.width: int = width
        self.height: int = height
        self.title: str = title
        self.extra_style: str = extra_style
        # Patterns and other definitions a chart's classes refer to
        self.defs: list[str] = []
        self.parts: list[str] = []
        self.rect(0.5, 0.5, width - 1, height - 1, "card", radius=10)
        self.text(28, 40, title, "title")
        self.text(28, 62, subtitle, "sub")

    def shape(self, element: str, tip: str = "") -> None:
        """Adds a shape (its tag and attributes). A tip is the shape's <title>: the text a viewer shows while the pointer is on it."""
        tag = element.split(" ", 1)[0]
        self.parts.append(f"<{element}><title>{escape(tip)}</title></{tag}>" if tip else f"<{element}/>")

    def rect(self, x: float, y: float, width: float, height: float, style: str, radius: float = 0, tip: str = "") -> None:
        corner = f' rx="{coordinate(radius)}"' if radius else ""
        self.shape(f'rect class="{style}" x="{coordinate(x)}" y="{coordinate(y)}" width="{coordinate(width)}" height="{coordinate(height)}"{corner}', tip)

    def line(self, x1: float, y1: float, x2: float, y2: float, style: str, tip: str = "") -> None:
        self.shape(f'line class="{style}" x1="{coordinate(x1)}" y1="{coordinate(y1)}" x2="{coordinate(x2)}" y2="{coordinate(y2)}"', tip)

    def text(self, x: float, y: float, text: str, style: str = "", anchor: str = "start") -> None:
        classes = f' class="{style}"' if style else ""
        align = f' text-anchor="{anchor}"' if anchor != "start" else ""
        self.parts.append(f'<text{classes} x="{coordinate(x)}" y="{coordinate(y)}"{align}>{escape(text)}</text>')

    def polyline(self, points: list[tuple[float, float]], style: str) -> None:
        path = " ".join(f"{coordinate(x)},{coordinate(y)}" for x, y in points)
        self.parts.append(f'<polyline class="{style}" points="{path}"/>')

    def polygon(self, points: list[tuple[float, float]], style: str, tip: str = "") -> None:
        path = " ".join(f"{coordinate(x)},{coordinate(y)}" for x, y in points)
        self.shape(f'polygon class="{style}" points="{path}"', tip)

    def key(self, x: float, y: float, entries: list[tuple[str, str]]) -> None:
        """Colour swatches with their names, left to right from x."""
        for style, name in entries:
            self.rect(x, y - 10, 12, 12, style, radius=2)
            self.text(x + 18, y, name, "axis")
            x += 18 + 7.2 * len(name) + 22

    def line_key(self, x: float, y: float, entries: list[tuple[str, str]]) -> None:
        for style, name in entries:
            self.line(x, y - 4, x + 18, y - 4, f"line {style}")
            self.text(x + 24, y, name, "axis")
            x += 24 + 7.2 * len(name) + 22

    def render(self) -> str:
        head = (
            f'<svg xmlns="http://www.w3.org/2000/svg" width="{self.width}" height="{self.height}" '
            f'viewBox="0 0 {self.width} {self.height}" role="img" aria-label="{escape(self.title)}">\n'
            f"<title>{escape(self.title)}</title>\n<style>{STYLE}{self.extra_style}</style>\n"
        )
        if self.defs:
            head += "<defs>\n" + "\n".join(self.defs) + "\n</defs>\n"
        return head + "\n".join(self.parts) + "\n</svg>\n"
