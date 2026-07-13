using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Color = Autodesk.AutoCAD.Colors.Color;

namespace SolarTestRange
{
    /// <summary>
    /// AutoCAD 绘制层 — 负责将计算出的几何数据渲染到当前图纸
    /// </summary>
    public static class RangeDrawer
    {
        /// <summary>
        /// 绘制完整的日照测试范围
        /// </summary>
        public static void Draw(Database db, SolarRangeCalculator calc)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                var btr = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                // 1. 画八角射线（8条线）
                DrawRay(tr, btr, calc.NW, calc.NW_Upper);  // ①
                DrawRay(tr, btr, calc.NW, calc.NW_Lower);  // ②
                DrawRay(tr, btr, calc.NE, calc.NE_Upper);  // ③
                DrawRay(tr, btr, calc.NE, calc.NE_Lower);  // ④
                DrawRay(tr, btr, calc.SW, calc.SW_Upper);  // ⑤
                DrawRay(tr, btr, calc.SW, calc.SW_Lower);  // ⑥
                DrawRay(tr, btr, calc.SE, calc.SE_Upper);  // ⑦
                DrawRay(tr, btr, calc.SE, calc.SE_Lower);  // ⑧

                // 2. 闭合线：①-⑤ ③-⑦ ②-⑥ ④-⑧
                DrawLine(tr, btr, calc.NW_Upper, calc.SW_Upper);
                DrawLine(tr, btr, calc.NE_Upper, calc.SE_Upper);
                DrawLine(tr, btr, calc.NW_Lower, calc.SW_Lower);
                DrawLine(tr, btr, calc.NE_Lower, calc.SE_Lower);

                // 3. 偏移线（上下各一条）
                DrawLine(tr, btr, calc.OffsetTopLeft, calc.OffsetTopRight);
                DrawLine(tr, btr, calc.OffsetBottomLeft, calc.OffsetBottomRight);

                // 4. 四段圆弧（裁剪后的）
                DrawArc(tr, btr, calc.GetArcNW());
                DrawArc(tr, btr, calc.GetArcNE());
                DrawArc(tr, btr, calc.GetArcSW());
                DrawArc(tr, btr, calc.GetArcSE());

                tr.Commit();
            }

            // 闭合区域填色（生成一个封闭的多段线轮廓）
            DrawFilledBoundary(db, calc);
        }

        private static void DrawRay(Transaction tr, BlockTableRecord btr, Point2D from, Point2D to)
        {
            var line = new Line(ToPoint3d(from), ToPoint3d(to));
            line.Color = Color.FromColor(System.Drawing.Color.Cyan);
            btr.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
        }

        private static void DrawLine(Transaction tr, BlockTableRecord btr, Point2D p1, Point2D p2)
        {
            var line = new Line(ToPoint3d(p1), ToPoint3d(p2));
            line.Color = Color.FromColor(System.Drawing.Color.Yellow);
            btr.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
        }

        private static void DrawArc(Transaction tr, BlockTableRecord btr, ArcParams arc)
        {
            var center = new Point3d(arc.Center.X, arc.Center.Y, 0);
            var arcEnt = new Arc(center, arc.Radius, 
                arc.StartAngleDeg * Math.PI / 180.0, 
                arc.EndAngleDeg * Math.PI / 180.0);
            arcEnt.Color = Color.FromColor(System.Drawing.Color.Magenta);
            btr.AppendEntity(arcEnt);
            tr.AddNewlyCreatedDBObject(arcEnt, true);
        }

        /// <summary>
        /// 绘制闭合的日照测试范围填充区域
        /// </summary>
        private static void DrawFilledBoundary(Database db, SolarRangeCalculator calc)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                var btr = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                var pline = new Polyline();

                // 按顺时针构建闭合多段线边界
                int idx = 0;
                pline.AddVertexAt(idx++, ToPoint2d(calc.NW_Upper), 0, 0, 0);
                pline.AddVertexAt(idx++, ToPoint2d(calc.OffsetTopLeft), 0, 0, 0);
                pline.AddVertexAt(idx++, ToPoint2d(calc.OffsetTopRight), 0, 0, 0);
                pline.AddVertexAt(idx++, ToPoint2d(calc.NE_Upper), 0, 0, 0);
                pline.AddVertexAt(idx++, ToPoint2d(calc.SE_Upper), 0, 0, 0);
                pline.AddVertexAt(idx++, ToPoint2d(calc.OffsetBottomRight), 0, 0, 0);
                pline.AddVertexAt(idx++, ToPoint2d(calc.OffsetBottomLeft), 0, 0, 0);
                pline.AddVertexAt(idx++, ToPoint2d(calc.SW_Lower), 0, 0, 0);
                pline.AddVertexAt(idx++, ToPoint2d(calc.NW_Lower), 0, 0, 0);
                pline.Closed = true;
                pline.Color = Color.FromColor(System.Drawing.Color.OrangeRed);

                btr.AppendEntity(pline);
                tr.AddNewlyCreatedDBObject(pline, true);
                tr.Commit();
            }
        }

        private static Point3d ToPoint3d(Point2D p) => new(p.X, p.Y, 0);
        private static Point2d ToPoint2d(Point2D p) => new(p.X, p.Y);
    }
}
