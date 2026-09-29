//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Draws a report card (CardDrawing) the way its SVG shows it: the same shapes with the style sheet's values (CardStyle), text in Inter,
//* scaled to the control's width. With CanZoom the mouse wheel zooms the time axis around the pointer, a drag pans it and a double-click
//* goes back to the whole run, and a horizontal wheel or Shift+wheel scrolls (the view asks its owner). A card built as a sliding window
//* (ScrollShape layers reaching beyond the plots) scrolls by ScrollOffset alone: the layers move, clipped to the plots, and the plots' time
//* ranges move with them, so nothing is built again until the owner sends a new window. Hovering a plot draws a cursor line and what
//* HoverText says about that point.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using MB.FramePacing.Charts;

namespace MB.FramePacing.Gui.Views
{
  public sealed class CardView : Control
  {
    public static readonly StyledProperty<CardDrawing?> DrawingProperty = AvaloniaProperty.Register<CardView, CardDrawing?>(nameof(Drawing));

    public static readonly StyledProperty<bool> CanZoomProperty = AvaloniaProperty.Register<CardView, bool>(nameof(CanZoom));

    public static readonly StyledProperty<double> ScrollOffsetProperty = AvaloniaProperty.Register<CardView, double>(nameof(ScrollOffset));

    // The wheel zooms by this much per notch; sideways it scrolls by this share of the range in view
    private const double ZoomStep = 0.8;
    private const double ScrollStep = 0.1;
    private static readonly FontFamily g_font = new FontFamily("fonts:Inter#Inter");
    private static readonly IBrush g_hoverBackground = new SolidColorBrush(Color.Parse("#0d1117"), 0.92);
    private static readonly IPen g_hoverBorder = new Pen(new SolidColorBrush(Color.Parse("#8b949e"), 0.5), 1);
    private static readonly IPen g_cursor = new Pen(new SolidColorBrush(Colors.White, 0.5), 1);

    private List<Action<DrawingContext>> m_operations = new List<Action<DrawingContext>>();
    private (CardPlot Plot, Point Card, string? Text)? m_hover;
    private (CardPlot Plot, double StartX)? m_drag;

    static CardView()
    {
      AffectsRender<CardView>(DrawingProperty, ScrollOffsetProperty);
      AffectsMeasure<CardView>(DrawingProperty);
    }

    public CardView()
    {
      ClipToBounds = true;
    }

    /// <summary>The card to draw.</summary>
    public CardDrawing? Drawing
    {
      get => GetValue(DrawingProperty);
      set => SetValue(DrawingProperty, value);
    }

    /// <summary>The wheel, a drag and a double-click change the time range (<see cref="RangeRequested"/>, <see cref="ResetRequested"/>).</summary>
    public bool CanZoom
    {
      get => GetValue(CanZoomProperty);
      set => SetValue(CanZoomProperty, value);
    }

    /// <summary>How far the card's scrolling layers are moved to the right, in card units: the sliding window's position.</summary>
    public double ScrollOffset
    {
      get => GetValue(ScrollOffsetProperty);
      set => SetValue(ScrollOffsetProperty, value);
    }

    /// <summary>What to show at a point of a plot (its values), or null for nothing.</summary>
    public Func<CardPlot, double, double, string?>? HoverText { get; set; }

    /// <summary>The time range the user panned to, in the plots' x values (seconds).</summary>
    public event Action<double, double>? RangeRequested;

    /// <summary>
    /// The wheel: zoom by the factor around the time under the pointer. The owner applies it to the range it last asked for, so notches add
    /// up while the card on screen is still the older one.
    /// </summary>
    public event Action<double, double>? ZoomRequested;

    /// <summary>A horizontal wheel or swipe, or Shift+wheel: scroll by this fraction of the range in view (negative: to the left).</summary>
    public event Action<double>? ScrollRequested;

    /// <summary>A double-click: back to the whole run.</summary>
    public event Action? ResetRequested;

    /// <summary>Card units per device independent pixel.</summary>
    private double Scale => Drawing is { } drawing && Bounds.Width > 0 ? Bounds.Width / drawing.Width : 1;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
      base.OnPropertyChanged(change);
      if (change.Property == DrawingProperty)
        m_operations = Drawing is { } drawing ? Prepare(drawing) : new List<Action<DrawingContext>>();
      // The pointer stays where it is: describe the same place on the new card, or at the new offset
      if ((change.Property == DrawingProperty || change.Property == ScrollOffsetProperty) && m_hover is { } hover && Drawing is { } card)
        m_hover = Hover(card, hover.Card);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
      if (Drawing is not { } drawing)
        return default;
      double width = double.IsInfinity(availableSize.Width) ? drawing.Width : availableSize.Width;
      return new Size(width, drawing.Height * width / drawing.Width);
    }

    public override void Render(DrawingContext context)
    {
      if (Drawing is not { } drawing)
        return;
      double scale = Scale;
      using (context.PushTransform(Matrix.CreateScale(scale, scale)))
      {
        foreach (var operation in m_operations)
          operation(context);
        if (m_hover is { } hover)
          DrawHover(context, drawing, hover.Plot, hover.Card, hover.Text);
      }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------
    // Interaction

    protected override void OnPointerMoved(PointerEventArgs e)
    {
      base.OnPointerMoved(e);
      if (Drawing is not { } drawing)
        return;
      var card = ToCard(e.GetPosition(this));
      if (m_drag is { } drag)
      {
        // Pan by the distance dragged, in the range the drag started on
        double delta = drag.Plot.ValueX(drag.StartX) - drag.Plot.ValueX(card.X);
        RangeRequested?.Invoke(drag.Plot.XFrom + delta, drag.Plot.XTo + delta);
        return;
      }
      m_hover = Hover(drawing, card);
      InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
      base.OnPointerExited(e);
      m_hover = null;
      InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
      base.OnPointerPressed(e);
      if (!CanZoom || Drawing is not { } drawing || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        return;
      if (e.ClickCount == 2)
      {
        // The first click started a drag
        m_drag = null;
        e.Pointer.Capture(null);
        ResetRequested?.Invoke();
        e.Handled = true;
        return;
      }
      var card = ToCard(e.GetPosition(this));
      if (PlotAt(drawing, card) is { } plot)
      {
        m_drag = (plot, card.X);
        m_hover = null;
        e.Pointer.Capture(this);
        e.Handled = true;
      }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
      base.OnPointerReleased(e);
      m_drag = null;
      if (ReferenceEquals(e.Pointer.Captured, this))
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
      base.OnPointerWheelChanged(e);
      if (!CanZoom || Drawing is not { } drawing)
        return;
      // Sideways (a horizontal wheel or swipe, or Shift with the wheel) scrolls; a tenth of the range in view per notch
      double sideways =
        e.Delta.X != 0 ? -e.Delta.X
        : (e.KeyModifiers & KeyModifiers.Shift) != 0 ? -e.Delta.Y
        : 0;
      if (sideways != 0)
      {
        ScrollRequested?.Invoke(sideways * ScrollStep);
        e.Handled = true;
        return;
      }
      if (e.Delta.Y == 0)
        return;
      var card = ToCard(e.GetPosition(this));
      if (PlotAt(drawing, card) is not { } plot)
        return;
      // Zoom around the time under the pointer: it stays where it is
      ZoomRequested?.Invoke(plot.ValueX(card.X), Math.Pow(ZoomStep, e.Delta.Y));
      e.Handled = true;
    }

    private Point ToCard(Point control) => new Point(control.X / Scale, control.Y / Scale);

    /// <summary>The plot at the point, its time range moved with the scrolling layers.</summary>
    private CardPlot? PlotAt(CardDrawing drawing, Point card) =>
      drawing.Plots.FirstOrDefault(p => p.Contains(card.X, card.Y)) is { } plot ? Scrolled(plot) : null;

    private CardPlot Scrolled(CardPlot plot)
    {
      double offset = ScrollOffset;
      if (offset == 0)
        return plot;
      double seconds = offset * (plot.XTo - plot.XFrom) / (plot.Right - plot.Left);
      return plot with { XFrom = plot.XFrom - seconds, XTo = plot.XTo - seconds };
    }

    private (CardPlot, Point, string?)? Hover(CardDrawing drawing, Point card) =>
      PlotAt(drawing, card) is { } plot ? (plot, card, HoverText?.Invoke(plot, plot.ValueX(card.X), plot.ValueY(card.Y))) : null;

    /// <summary>The cursor line through the plot, and the text in a box beside the pointer, kept on the card.</summary>
    private static void DrawHover(DrawingContext context, CardDrawing drawing, CardPlot plot, Point card, string? text)
    {
      context.DrawLine(g_cursor, new Point(card.X, plot.Top), new Point(card.X, plot.Bottom));
      if (string.IsNullOrEmpty(text))
        return;
      var layout = new TextLayout(text, new Typeface(g_font), 12, Brushes.White);
      const double Padding = 8;
      double width = layout.Width + (2 * Padding);
      double height = layout.Height + (2 * Padding);
      double x = card.X + 14 + width <= drawing.Width - 4 ? card.X + 14 : card.X - 14 - width;
      double y = Math.Clamp(card.Y - (height / 2), 4, Math.Max(4, drawing.Height - height - 4));
      context.DrawRectangle(g_hoverBackground, g_hoverBorder, new Rect(x, y, width, height), 6, 6);
      layout.Draw(context, new Point(x + Padding, y + Padding));
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------
    // Drawing: every shape turned once into what draws it

    private List<Action<DrawingContext>> Prepare(CardDrawing drawing)
    {
      var operations = new List<Action<DrawingContext>>();
      var card = CardStyle.Resolve("card", text: false);
      var cardRect = new Rect(0.5, 0.5, drawing.Width - 1, drawing.Height - 1);
      var cardFill = Brush(card.Fill, card.FillOpacity, defaultBlack: true);
      var cardPen = Pen(card);
      operations.Add(context => context.DrawRectangle(cardFill, cardPen, cardRect, 14, 14));
      foreach (var shape in drawing.Shapes)
        Add(operations, shape);
      return operations;
    }

    private void Add(List<Action<DrawingContext>> operations, CardShape shape)
    {
      switch (shape)
      {
        case RectShape r:
        {
          var style = CardStyle.Resolve(r.Class, text: false);
          var fill = Brush(style.Fill, style.FillOpacity, defaultBlack: true);
          var pen = Pen(style);
          var rect = new Rect(r.X.Value, r.Y.Value, Math.Max(0, r.Width.Value), Math.Max(0, r.Height.Value));
          double radius = r.Rx.Length > 0 ? double.Parse(r.Rx, System.Globalization.CultureInfo.InvariantCulture) : 0;
          operations.Add(context => context.DrawRectangle(fill, pen, rect, radius, radius));
          break;
        }
        case LineShape l:
        {
          if (Pen(CardStyle.Resolve(l.Class, text: false)) is { } pen)
          {
            var from = new Point(l.X1.Value, l.Y1.Value);
            var to = new Point(l.X2.Value, l.Y2.Value);
            operations.Add(context => context.DrawLine(pen, from, to));
          }
          break;
        }
        case PathShape p:
        {
          var style = CardStyle.Resolve(p.Class, text: false);
          var geometry = StreamGeometry.Parse(p.Data);
          var fill = Brush(style.Fill, style.FillOpacity, defaultBlack: true);
          var pen = Pen(style);
          operations.Add(context => context.DrawGeometry(fill, pen, geometry));
          break;
        }
        case TextShape t:
        {
          var layout = Text(t);
          double width = layout.WidthIncludingTrailingWhitespace;
          double x = t.Anchor switch
          {
            "middle" => t.X - (width / 2),
            "end" => t.X - width,
            _ => t.X,
          };
          var origin = new Point(x, t.Y - layout.TextLines[0].Baseline);
          operations.Add(context => layout.Draw(context, origin));
          break;
        }
        case GroupShape g:
        {
          var children = new List<Action<DrawingContext>>();
          foreach (var child in g.Children)
            Add(children, child);
          var translate = Matrix.CreateTranslation(0, g.TranslateY);
          operations.Add(context =>
          {
            using (context.PushTransform(translate))
            {
              foreach (var child in children)
                child(context);
            }
          });
          break;
        }
        case ScrollShape layer:
        {
          var children = new List<Action<DrawingContext>>();
          foreach (var child in layer.Children)
            Add(children, child);
          var clip = new Rect(layer.Left, layer.Top, layer.Right - layer.Left, layer.Bottom - layer.Top);
          operations.Add(context =>
          {
            using (context.PushClip(clip))
            using (context.PushTransform(Matrix.CreateTranslation(ScrollOffset, 0)))
            {
              foreach (var child in children)
                child(context);
            }
          });
          break;
        }
        default:
          throw new ArgumentException($"Unknown card shape {shape.GetType().Name}", nameof(shape));
      }
    }

    private static TextLayout Text(TextShape text)
    {
      var style = CardStyle.Resolve(text.Class, text: true);
      double size = style.FontSize ?? 13;
      var weight = style.FontWeight switch
      {
        >= 700 => FontWeight.Bold,
        >= 600 => FontWeight.SemiBold,
        _ => FontWeight.Normal,
      };
      var features = style.TabularNumbers == true ? new FontFeatureCollection { FontFeature.Parse("tnum") } : null;
      return new TextLayout(
        text.Content,
        new Typeface(g_font, FontStyle.Normal, weight),
        size,
        Brush(style.Fill, style.FillOpacity, defaultBlack: true),
        letterSpacing: (style.LetterSpacingEm ?? 0) * size,
        fontFeatures: features
      );
    }

    /// <summary>SVG's paint: a colour with its opacity, "none" for nothing, and black where the sheet sets no fill.</summary>
    private static IBrush? Brush(string? colour, double? opacity, bool defaultBlack)
    {
      if (colour == "none" || (colour == null && !defaultBlack))
        return null;
      return new SolidColorBrush(colour != null ? Color.Parse(colour) : Colors.Black, opacity ?? 1).ToImmutable();
    }

    /// <summary>The stroke, when the sheet sets one: its width, dashes (Avalonia counts them in widths) and joins.</summary>
    private static IPen? Pen(CardStyleRule style)
    {
      if (Brush(style.Stroke, style.StrokeOpacity, defaultBlack: false) is not { } brush)
        return null;
      double width = style.StrokeWidth ?? 1;
      var dashes = style.StrokeDashArray is { Count: > 0 } array ? new DashStyle(array.Select(d => d / width), 0) : null;
      return new ImmutablePen(
        (IImmutableBrush)brush,
        width,
        dashes?.ToImmutable(),
        PenLineCap.Flat,
        style.RoundJoins == true ? PenLineJoin.Round : PenLineJoin.Miter
      );
    }
  }
}
