using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CdsHelper.Game.Engine.Disev;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 발견 대본 흐름도 — <see cref="DisevFlow.Graph"/> 을 왼쪽에서 오른쪽으로 편 그림이다.
/// </summary>
/// <remarks>
/// 분기는 파란 마름모다. 묶음은 <b>명령 하나에 상자 하나</b>를 세로로 쌓고 작은 화살로 잇는다 —
/// 상자는 초록, 끝 명령(결과 코드·게임 오버) 상자는 갈색이고, 누르면 그 명령을 고른다.
/// 묶음으로 드는 화살은 첫 상자에, 나가는 화살은 마지막 상자에 붙는다. 칸(열)은 들머리에서 가장 긴
/// 앞으로 가는 길의 걸음 수로 잡고, 한 열 안에서는 앞 노드 높이에 맞춘다.
/// <b>분기의 아니오(다음 줄)는 마름모 오른쪽 꼭짓점에서 같은 줄로, 예(뛰는 쪽)는 아래 꼭짓점에서
/// 내려 마름모보다 밑 줄로</b> 간다. 예 노드는 가는 길에 걸리는 사이 열 노드 밑으로 민다.
/// 뒤로 뛰는 화살은 두 노드 밑으로 돌려 그린다.
/// </remarks>
internal static class DisevFlowView
{
    private const double BoxW = 280, DiamondW = 190, DiamondH = 90, OutsideW = 190, OutsideH = 44;
    private const double LineH = 18, Pad = 8, GapX = 80, GapY = 28, Edge0 = 16;
    /// <summary>묶음 속 명령 상자 하나의 높이와 상자 사이(작은 화살 자리).</summary>
    private const double OpH = 30, OpGap = 14;
    private const int MaxChars = 34;

    private static readonly Brush LineBrush = Solid(0x44, 0x72, 0xC4);
    private static readonly Brush StrokeBrush = Solid(0x1F, 0x38, 0x64);
    private static readonly Brush DecisionFill = Solid(0x44, 0x72, 0xC4);
    private static readonly Brush BlockFill = Solid(0x54, 0x82, 0x35);
    private static readonly Brush EndFill = Solid(0x9E, 0x48, 0x0E);
    private static readonly Brush OutsideFill = Solid(0x7F, 0x7F, 0x7F);

    private static SolidColorBrush Solid(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>흐름도를 그린다.</summary>
    /// <param name="pick">노드를 누르면 그 첫 명령 자리를 넘긴다.</param>
    /// <param name="describe">명령 상자에 적을 글. 없으면 <see cref="DisevScript.Op.Text"/> 다.</param>
    /// <param name="action">명령 상자 오른쪽에 붙일 단추 — 이름과 누르면 할 일. 붙일 것이 없으면 null 을 낸다.</param>
    public static FrameworkElement Build(DisevFlow.Graph graph, Action<int> pick,
        Func<DisevScript.Op, string>? describe = null,
        Func<DisevScript.Op, (string Label, Action Run)?>? action = null)
    {
        describe ??= op => op.Text;
        var canvas = new Canvas { Background = Brushes.White };
        var nodes = graph.Nodes;
        int n = nodes.Count;
        if (n == 0) return canvas;

        var size = nodes.Select(SizeOf).ToArray();

        // 열 — 앞으로 가는 화살(To > From)만 따라 가장 긴 걸음 수.
        var col = new int[n];
        var outs = graph.Edges.ToLookup(e => e.From);
        for (int i = 0; i < n; i++)
            foreach (var e in outs[i])
                if (e.To > i) col[e.To] = Math.Max(col[e.To], col[i] + 1);

        int columns = col.Max() + 1;
        var colX = new double[columns];
        double x0 = Edge0;
        for (int c = 0; c < columns; c++)
        {
            colX[c] = x0;
            double width = Enumerable.Range(0, n).Where(i => col[i] == c).Select(i => size[i].Width).DefaultIfEmpty(0).Max();
            x0 += width + GapX;
        }

        // 줄 — 열마다 앞 노드 가운데 높이에 맞추고, 겹치면 밑으로 민다.
        var y = new double[n];

        bool IsDown(DisevFlow.Edge e) => e.Jump && nodes[e.From].Kind == DisevFlow.NodeKind.Decision;

        // 화살이 붙는 높이(노드 윗변에서) — 쌓인 묶음은 드는 쪽이 첫 상자, 나가는 쪽이 마지막 상자 가운데다.
        double InOff(int i) => IsStack(nodes[i]) ? OpH / 2 : size[i].Height / 2;
        double OutOff(int i) => IsStack(nodes[i]) ? size[i].Height - OpH / 2 : size[i].Height / 2;

        // 예 노드의 윗변 — 마름모 밑으로 내리고, 가로 화살이 사이 열 노드를 꿰지 않게 더 민다.
        // 열은 왼쪽부터 놓으므로 사이 열은 이미 자리가 잡혀 있다.
        double BelowDecision(int decision, int target)
        {
            double want = y[decision] + size[decision].Height + GapY;
            for (bool moved = true; moved;)
            {
                moved = false;
                double line = want + InOff(target);
                for (int k = 0; k < n; k++)
                {
                    if (col[k] <= col[decision] || col[k] >= col[target]) continue;
                    if (line < y[k] - GapY / 2 || line > y[k] + size[k].Height + GapY / 2) continue;
                    want = y[k] + size[k].Height + GapY - InOff(target);
                    moved = true;
                }
            }
            return want;
        }
        var incoming = graph.Edges.Where(e => e.From < e.To).ToLookup(e => e.To);
        for (int c = 0; c < columns; c++)
        {
            var order = Enumerable.Range(0, n).Where(i => col[i] == c)
                .Select(i =>
                {
                    var from = incoming[i].OrderBy(e => e.From).FirstOrDefault();
                    bool has = incoming[i].Any();
                    bool down = has && IsDown(from);
                    double want = !has ? Edge0
                        : down ? BelowDecision(from.From, i)
                        : y[from.From] + OutOff(from.From) - InOff(i);
                    // 아니오가 먼저(같은 줄), 예는 맨 뒤(밑 줄)다.
                    int rank = !has ? 1 : down ? 2 : from.Label.Length > 0 ? 0 : 1;
                    return (Node: i, Want: want, Label: rank);
                })
                .OrderBy(t => t.Want).ThenBy(t => t.Label).ThenBy(t => t.Node);

            double free = Edge0;
            foreach (var (node, want, _) in order)
            {
                y[node] = Math.Max(Math.Max(want, Edge0), free);
                free = y[node] + size[node].Height + GapY;
            }
        }

        double right = Enumerable.Range(0, n).Max(i => colX[col[i]] + size[i].Width) + Edge0;
        double bottom = Enumerable.Range(0, n).Max(i => y[i] + size[i].Height) + Edge0;

        // 화살 먼저 — 노드가 그 위를 덮는다.
        int back = 0;
        foreach (var e in graph.Edges)
        {
            // 예는 마름모 아래 꼭짓점, 그 밖은 오른쪽 가운데에서 나간다.
            bool down = IsDown(e);
            double sx = down ? colX[col[e.From]] + size[e.From].Width / 2 : colX[col[e.From]] + size[e.From].Width;
            double sy = down ? y[e.From] + size[e.From].Height : y[e.From] + OutOff(e.From);
            double tx = colX[col[e.To]], ty = y[e.To] + InOff(e.To);
            var line = new Polyline { Stroke = LineBrush, StrokeThickness = 1.2 };
            double labelX, labelY = ty - 19;

            if (down)
            {
                labelX = sx + 6;
                labelY = sy + 2;
                if (col[e.To] > col[e.From] && ty >= sy + 14)
                    line.Points = [new(sx, sy), new(sx, ty), new(tx, ty)];
                else
                {
                    // 과녁이 꼭짓점보다 높거나 뒤에 있으면 한 번 내려 섰다가 돌아 들어간다.
                    double by = col[e.To] > col[e.From]
                        ? sy + 14
                        : Math.Max(sy, y[e.To] + size[e.To].Height) + 14 + back++ * 8;
                    double lx = tx - (col[e.To] > col[e.From] ? GapX / 2 : 14);
                    line.Points = [new(sx, sy), new(sx, by), new(lx, by), new(lx, ty), new(tx, ty)];
                    bottom = Math.Max(bottom, by + Edge0);
                }
            }
            else if (col[e.To] > col[e.From])
            {
                double mx = tx - GapX / 2;
                line.Points = [new(sx, sy), new(mx, sy), new(mx, ty), new(tx, ty)];
                labelX = mx + 4;
            }
            else
            {
                double by = Math.Max(y[e.From] + size[e.From].Height, y[e.To] + size[e.To].Height) + 14 + back++ * 8;
                double rx = sx + 14, lx = tx - 14;
                line.Points = [new(sx, sy), new(rx, sy), new(rx, by), new(lx, by), new(lx, ty), new(tx, ty)];
                labelX = lx - 34;
                bottom = Math.Max(bottom, by + Edge0);
            }

            canvas.Children.Add(line);
            canvas.Children.Add(new Polygon
            {
                Fill = LineBrush,
                Points = [new(tx, ty), new(tx - 9, ty - 4.5), new(tx - 9, ty + 4.5)],
            });

            if (e.Label.Length > 0)
            {
                var label = new TextBlock { Text = e.Label, Foreground = StrokeBrush, FontWeight = FontWeights.Bold };
                Canvas.SetLeft(label, labelX);
                Canvas.SetTop(label, labelY);
                canvas.Children.Add(label);
            }
        }

        for (int i = 0; i < n; i++)
        {
            var node = nodes[i];
            var element = Draw(node, size[i], pick, describe, action);
            element.Cursor = Cursors.Hand;
            // 쌓인 묶음은 상자마다 제 풀이를 달았으니 겉에는 안 단다.
            if (!IsStack(node))
                element.ToolTip = node.Ops.Count > 0 ? string.Join(Environment.NewLine, node.Ops.Select(o => o.Text)) : node.Title;
            int offset = node.Offset;
            element.MouseLeftButtonUp += (_, args) => { args.Handled = true; pick(offset); };

            Canvas.SetLeft(element, colX[col[i]]);
            Canvas.SetTop(element, y[i]);
            canvas.Children.Add(element);
        }

        canvas.Width = right;
        canvas.Height = bottom;
        return canvas;
    }

    private static Size SizeOf(DisevFlow.Node node) => node.Kind switch
    {
        DisevFlow.NodeKind.Decision => new Size(DiamondW, DiamondH),
        DisevFlow.NodeKind.Outside => new Size(OutsideW, OutsideH),
        _ => new Size(BoxW, node.Ops.Count * OpH + Math.Max(0, node.Ops.Count - 1) * OpGap),
    };

    /// <summary>명령 상자를 쌓아 그리는 노드인가 — 묶음과 멎는 묶음.</summary>
    private static bool IsStack(DisevFlow.Node node) =>
        node.Kind is DisevFlow.NodeKind.Block or DisevFlow.NodeKind.End;

    private static FrameworkElement Draw(DisevFlow.Node node, Size size, Action<int> pick,
        Func<DisevScript.Op, string> describe, Func<DisevScript.Op, (string Label, Action Run)?>? action)
    {
        if (IsStack(node))
        {
            var stack = new StackPanel { Width = size.Width };
            for (int k = 0; k < node.Ops.Count; k++)
            {
                var op = node.Ops[k];
                if (k > 0) stack.Children.Add(DownArrow(size.Width));

                string text = describe(op);
                var box = new Border
                {
                    Height = OpH,
                    Background = DisevFlow.Ends.Contains(op.Kind) ? EndFill : BlockFill,
                    BorderBrush = StrokeBrush,
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = text,
                    Child = BoxContent(text, action?.Invoke(op)),
                };
                // 상자를 누르면 노드 첫 명령이 아니라 그 명령을 고른다 — 겉 노드 손은 안 돌게 막는다.
                int offset = op.Offset;
                box.MouseLeftButtonUp += (_, args) => { args.Handled = true; pick(offset); };
                stack.Children.Add(box);
            }
            return stack;
        }

        if (node.Kind == DisevFlow.NodeKind.Decision)
        {
            // 물음에도 상자와 같은 이름을 붙인다 — 「발견물 55 찾았나」 만으로는 무엇인지 모른다.
            // 이름은 describe 가 풀이 뒤에 붙인 꼬리만 떼어 온다.
            string title = node.Title;
            if (node.Ops.Count > 0)
            {
                var op = node.Ops[0];
                string described = describe(op);
                if (described.Length > op.Text.Length && described.StartsWith(op.Text, StringComparison.Ordinal))
                    title += described[op.Text.Length..];
            }

            var grid = new Grid { Width = size.Width, Height = size.Height, Background = Brushes.Transparent };
            grid.Children.Add(new Polygon
            {
                Fill = DecisionFill,
                Stroke = StrokeBrush,
                StrokeThickness = 1,
                Points = [new(0, size.Height / 2), new(size.Width / 2, 0), new(size.Width, size.Height / 2), new(size.Width / 2, size.Height)],
            });
            grid.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                MaxWidth = size.Width * 0.62,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            return grid;
        }

        // 남은 것은 덩이 밖 자리뿐이다.
        var lines = new StackPanel { Margin = new Thickness(Pad, Pad - 1, Pad, 0) };
        lines.Children.Add(Text(node.Title));

        return new Border
        {
            Width = size.Width,
            Height = size.Height,
            Background = OutsideFill,
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(1),
            Child = lines,
        };
    }

    /// <summary>
    /// 명령 상자 속 — 풀이 글과, 있으면 오른쪽 단추(그림 보기 · 소리 듣기).
    /// </summary>
    /// <remarks>단추는 제 누름을 삼키므로 상자의 「명령 고르기」 손은 안 돈다.</remarks>
    private static FrameworkElement BoxContent(string text, (string Label, Action Run)? extra)
    {
        var label = new TextBlock
        {
            Text = Short(text),
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Pad, 0, Pad, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (extra is not { } button) return label;

        var panel = new DockPanel();
        var run = button.Run;
        var click = new Button
        {
            Content = button.Label,
            FontSize = 11,
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(0, 3, 4, 3),
            Cursor = Cursors.Arrow,
        };
        click.Click += (_, args) => { args.Handled = true; run(); };
        DockPanel.SetDock(click, Dock.Right);
        panel.Children.Add(click);
        panel.Children.Add(label);
        return panel;
    }

    /// <summary>쌓인 상자 사이의 작은 아래 화살.</summary>
    private static Canvas DownArrow(double width)
    {
        double cx = width / 2;
        var canvas = new Canvas { Width = width, Height = OpGap };
        canvas.Children.Add(new Line
        {
            X1 = cx, Y1 = 0, X2 = cx, Y2 = OpGap - 5,
            Stroke = LineBrush,
            StrokeThickness = 1.2,
        });
        canvas.Children.Add(new Polygon
        {
            Fill = LineBrush,
            Points = [new(cx, OpGap), new(cx - 4, OpGap - 6), new(cx + 4, OpGap - 6)],
        });
        return canvas;
    }

    private static TextBlock Text(string text) => new()
    {
        Text = text,
        Foreground = Brushes.White,
        Height = LineH,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static string Short(string text) => text.Length > MaxChars ? text[..(MaxChars - 1)] + "…" : text;
}
