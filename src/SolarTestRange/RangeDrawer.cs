using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;

namespace SolarTestRange
{
    /// <summary>
    /// AutoCAD 绘制层 — 将计算出的几何数据渲染到当前图纸
    /// </summary>
    public static class RangeDrawer
    {
        // ACI 颜色索引: 1=红, 2=黄, 4=青, 6=品红
        private static readonly Color ColorCyan = Color.FromColorIndex(ColorMethod.ByAci, 4);
        private static readonly Color ColorYellow = Color.FromColorIndex(ColorMethod.ByAci, 2);
        private static readonly Color ColorMagenta = Color.FromColorIndex(ColorMethod.ByAci, 6);
        private static readonly Color ColorRed = Color.FromColorIndex(ColorMethod.ByAci, 1);

        public static void Draw(Database db, SolarRangeCalculator calc)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // 1. 八角射线（青色）
                DrawRay(tr, btr, calc.NW, calc.NW_Upper);
                DrawRay(tr, btr, calc.NW, calc.NW_Lower);
                DrawRay(tr, btr, calc.NE, calc.NE_Upper);
                DrawRay(tr, btr, calc.NE, calc.NE_Lower);
                DrawRay(tr, btr, calc.SW, calc.SW_Upper);
                DrawRay(tr, btr, calc.SW, calc.SW_Lower);
                DrawRay(tr, btr, calc.SE, calc.SE_Upper);
                DrawRay(tr, btr, calc.SE, calc.SE_Lower);

                // 2. 闭合线（黄色）
                DrawLine(tr, btr, calc.NW_Upper, calc.SW_Upper);
                DrawLine(tr, btr, calc.NE_Upper, calc.SE_Upper);
                DrawLine(tr, btr, calc.NW_Lower, calc.SW_Lower);
                DrawLine(tr, btr, calc.NE_Lower, calc.SE_Lower);

                // 3. 偏移线（黄色）
                DrawLine(tr, btr, calc.OffsetTopLeft, calc.OffsetTopRight);
                DrawLine(tr, btr, calc.OffsetBottomLeft, calc.OffsetBottomRight);

                // 4. 圆弧（品红）
                DrawArc(tr, btr, calc.GetArcNW());
                DrawArc(tr, btr, calc.GetArcNE());
                DrawArc(tr, btr, calc.GetArcSW());
                DrawArc(tr, btr, calc.GetArcSE());

                // 5. 填充边界（红色闭合多段线）
                DrawBoundary(tr, btr, calc);

                tr.Commit();
            }
        }

        private static void DrawRay(Transaction tr, BlockTableRecord btr, Point2D from, Point2D to)
        {
            var line = new Line(To3d(from), To3d(to)) { Color = ColorCyan };
            btr.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
        }

        private static void DrawLine(Transaction tr, BlockTableRecord btr, Point2D p1, Point2D p2)
        {
            var line = new Line(To3d(p1), To3d(p2)) { Color = ColorYellow };
            btr.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
        }

        private static void DrawArc(Transaction tr, BlockTableRecord btr, ArcParams arc)
        {
            var center = new Point3d(arc.Center.X, arc.Center.Y, 0);
            var arcEnt = new Arc(center, arc.Radius,
                arc.StartAngleDeg * System.Math.PI / 180.0,
                arc.EndAngleDeg * System.Math.PI / 180.0)
            { Color = ColorMagenta };
            btr.AppendEntity(arcEnt);
            tr.AddNewlyCreatedDBObject(arcEnt, true);
        }

        private static void DrawBoundary(Transaction tr, BlockTableRecord btr, SolarRangeCalculator calc)
        {
            var pline = new Polyline();
            int idx = 0;
            pline.AddVertexAt(idx++, To2d(calc.NW_Upper), 0, 0, 0);
            pline.AddVertexAt(idx++, To2d(calc.OffsetTopLeft), 0, 0, 0);
            pline.AddVertexAt(idx++, To2d(calc.OffsetTopRight), 0, 0, 0);
            pline.AddVertexAt(idx++, To2d(calc.NE_Upper), 0, 0, 0);
            pline.AddVertexAt(idx++, To2d(calc.SE_Upper), 0, 0, 0);
            pline.AddVertexAt(idx++, To2d(calc.OffsetBottomRight), 0, 0, 0);
            pline.AddVertexAt(idx++, To2d(calc.OffsetBottomLeft), 0, 0, 0);
            pline.AddVertexAt(idx++, To2d(calc.SW_Lower), 0, 0, 0);
            pline.AddVertexAt(idx++, To2d(calc.NW_Lower), 0, 0, 0);
            pline.Closed = true;
            pline.Color = ColorRed;
            btr.AppendEntity(pline);
            tr.AddNewlyCreatedDBObject(pline, true);
        }

        private static Point3d To3d(Point2D p) => new Point3d(p.X, p.Y, 0);
        private static Point2d To2d(Point2D p) => new Point2d(p.X, p.Y);
    }
}
