using System.Drawing;
using CaptureDemo.Core;
using Xunit;

namespace CaptureDemo.Core.Tests;

public class AnnoItemTests
{
    private static AnnoItem Shape(AnnoTool tool, Point from, Point to) => AnnoItem.FromDrag(tool, from, to);

    [Fact]
    public void BoundsAccountsForReversedDrag()
    {
        var item = Shape(AnnoTool.Rect, new Point(120, 90), new Point(20, 10));
        Assert.Equal(new Rectangle(20, 10, 100, 80), item.Bounds);
    }

    [Fact]
    public void MoveShiftsEveryPoint()
    {
        var item = new AnnoItem(AnnoTool.Pen, new[] { 10, 20, 30 }, new[] { 5, 15, 25 });
        item.Move(7, -3);
        Assert.Equal(new[] { 17, 27, 37 }, item.Xs);
        Assert.Equal(new[] { 2, 12, 22 }, item.Ys);
    }

    [Fact]
    public void SetBoundsStretchesFreehandProportionally()
    {
        var item = new AnnoItem(AnnoTool.Pen, new[] { 0, 50, 100 }, new[] { 0, 25, 50 });
        item.SetBounds(new Rectangle(0, 0, 200, 100));
        Assert.Equal(new[] { 0, 100, 200 }, item.Xs);
        Assert.Equal(new[] { 0, 50, 100 }, item.Ys);
    }

    [Fact]
    public void SetBoundsKeepsStrokeWidth()
    {
        var item = Shape(AnnoTool.Arrow, new Point(0, 0), new Point(40, 40));
        item.Width = 5f;
        item.SetBounds(new Rectangle(0, 0, 400, 400));
        Assert.Equal(5f, item.Width);
    }

    [Fact]
    public void CircleDragIsForcedSquare()
    {
        var item = Shape(AnnoTool.Circle, new Point(10, 10), new Point(90, 40));
        Assert.Equal(new Rectangle(10, 10, 80, 80), item.Bounds);
    }

    [Fact]
    public void CircleShapeBoxIsCentredInsideBounds()
    {
        var item = new AnnoItem(AnnoTool.Circle, new[] { 0, 100 }, new[] { 0, 40 });
        var box = item.ShapeBox;
        Assert.Equal(40, box.Width);
        Assert.Equal(30, box.Left);
    }

    [Fact]
    public void EllipseKeepsTheFullBounds()
    {
        var item = Shape(AnnoTool.Ellipse, new Point(0, 0), new Point(100, 40));
        Assert.Equal(new Rectangle(0, 0, 100, 40), item.ShapeBox);
    }

    [Fact]
    public void CircleDragRespectsNegativeDirection()
    {
        var item = Shape(AnnoTool.Circle, new Point(100, 100), new Point(40, 60));
        Assert.Equal(new Rectangle(40, 40, 60, 60), item.Bounds);
    }

    [Fact]
    public void SetEndConstrainsCircleToSquare()
    {
        var item = Shape(AnnoTool.Circle, new Point(10, 10), new Point(10, 10));
        item.SetEnd(60, 20);
        Assert.Equal(50, item.Bounds.Width);
        Assert.Equal(50, item.Bounds.Height);
    }

    [Fact]
    public void HitTestFindsPenStrokeNearTheLine()
    {
        var item = new AnnoItem(AnnoTool.Pen, new[] { 10, 90 }, new[] { 10, 10 });
        item.Width = 4f;
        Assert.True(item.HitTest(new Point(50, 12), 3));
        Assert.False(item.HitTest(new Point(50, 40), 3));
    }

    [Fact]
    public void HitTestRejectsTheInteriorOfAnUnfilledRectangle()
    {
        var item = Shape(AnnoTool.Rect, new Point(0, 0), new Point(100, 100));
        item.Width = 4f;
        Assert.True(item.HitTest(new Point(2, 50), 3));
        // A hollow rectangle should not be grabbed by clicking through its middle.
        Assert.False(item.HitTest(new Point(50, 50), 3));
    }

    [Fact]
    public void HitTestTreatsMosaicAsSolid()
    {
        var item = new AnnoItem(AnnoTool.Mosaic, new[] { 0, 60 }, new[] { 0, 40 });
        Assert.True(item.HitTest(new Point(30, 20), 3));
    }

    [Fact]
    public void HitTestMatchesEllipseCurveNotItsBoundingBox()
    {
        var item = Shape(AnnoTool.Ellipse, new Point(0, 0), new Point(100, 60));
        item.Width = 3f;
        Assert.True(item.HitTest(new Point(50, 1), 4));
        // The box corner lies outside the ellipse, so it must miss.
        Assert.False(item.HitTest(new Point(2, 2), 4));
    }

    [Fact]
    public void HandleAtFindsCornerGrip()
    {
        var item = Shape(AnnoTool.Rect, new Point(10, 10), new Point(110, 110));
        Assert.Equal(AnnoHandle.TopLeft, item.HandleAt(new Point(11, 12), 6));
        Assert.Equal(AnnoHandle.BottomRight, item.HandleAt(new Point(109, 108), 6));
        Assert.Equal(AnnoHandle.None, item.HandleAt(new Point(60, 60), 6));
    }

    [Fact]
    public void DragHandleGrowsRightEdgeOnly()
    {
        var item = Shape(AnnoTool.Rect, new Point(0, 0), new Point(100, 50));
        item.DragHandle(AnnoHandle.Right, new Point(20, 0));
        Assert.Equal(new Rectangle(0, 0, 120, 50), item.Bounds);
    }

    [Fact]
    public void DragHandleNeverInvertsTheBox()
    {
        var item = Shape(AnnoTool.Rect, new Point(0, 0), new Point(100, 50));
        item.DragHandle(AnnoHandle.Right, new Point(-500, 0));
        Assert.True(item.Bounds.Width >= AnnoItem.MinExtent);
        Assert.True(item.Bounds.Right > item.Bounds.Left);
    }

    [Fact]
    public void DragHandleScalesFreehandPoints()
    {
        var item = new AnnoItem(AnnoTool.Pen, new[] { 0, 100 }, new[] { 0, 100 });
        item.DragHandle(AnnoHandle.BottomRight, new Point(100, 100));
        Assert.Equal(new Rectangle(0, 0, 200, 200), item.Bounds);
    }

    [Fact]
    public void SetPointsRestoresASnapshot()
    {
        var item = new AnnoItem(AnnoTool.Pen, new[] { 1, 2 }, new[] { 3, 4 });
        var xs = (int[])item.Xs.Clone();
        var ys = (int[])item.Ys.Clone();
        item.Move(50, 50);
        item.SetPoints(xs, ys);
        Assert.Equal(new[] { 1, 2 }, item.Xs);
        Assert.Equal(new[] { 3, 4 }, item.Ys);
    }

    [Fact]
    public void AppendAddsASample()
    {
        var item = new AnnoItem(AnnoTool.Pen, new[] { 5 }, new[] { 5 });
        item.Append(9, 11);
        Assert.Equal(2, item.Xs.Length);
        Assert.Equal(9, item.Xs[1]);
        Assert.Equal(11, item.Ys[1]);
    }

    [Fact]
    public void ShapeWithNoExtentIsDegenerate()
    {
        var item = Shape(AnnoTool.Rect, new Point(40, 40), new Point(41, 41));
        Assert.True(item.IsDegenerate);
        Assert.False(Shape(AnnoTool.Rect, new Point(0, 0), new Point(40, 40)).IsDegenerate);
    }

    [Fact]
    public void SingleSampleFreehandIsDegenerate()
    {
        var item = new AnnoItem(AnnoTool.Pen, new[] { 7 }, new[] { 7 });
        Assert.True(item.IsDegenerate);
    }

    [Fact]
    public void CloneIsIndependent()
    {
        var item = new AnnoItem(AnnoTool.Pen, new[] { 1, 2 }, new[] { 3, 4 }) { ColorArgb = 123, Width = 6f };
        var copy = item.Clone();
        copy.Move(100, 100);
        Assert.Equal(1, item.Xs[0]);
        Assert.Equal(123, copy.ColorArgb);
        Assert.Equal(6f, copy.Width);
    }

    [Fact]
    public void InkBoundsCoversHighlightWideStroke()
    {
        var highlight = new AnnoItem(AnnoTool.Highlight, new[] { 0, 100 }, new[] { 0, 0 }) { Width = 20f };
        var pen = new AnnoItem(AnnoTool.Pen, new[] { 0, 100 }, new[] { 0, 0 }) { Width = 20f };
        Assert.True(highlight.InkBounds.Height > pen.InkBounds.Height);
        Assert.True(highlight.InkBounds.Height >= 34);
    }

    [Fact]
    public void HandlesReturnsEightGrips()
    {
        var box = new Rectangle(0, 0, 100, 60);
        Assert.Equal(8, AnnoItem.Handles(box).Count());
    }
}
