using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;

namespace SolarTestRange
{
    public static class RangeDrawer
    {
        private const string LayerBox = "00包围矩形";
        private const string LayerNorth = "00二倍楼高范围";
        private const string LayerSouth = "00二百米范围";

        public static void Draw(Database db, SolarRangeCalculator calc)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // 确保图层存在
                EnsureLayer(tr, db, LayerBox);
                EnsureLayer(tr, db, LayerNorth);
                EnsureLayer(tr, db, LayerSouth);

                // 各图层 ObjectId 收集，用于编组
                var idsBox = new ObjectIdCollection();
                var idsNorth = new ObjectIdCollection();
                var idsSouth = new ObjectIdCollection();

                // === 图层：00包围矩形 ===
                idsBox.Add(DrawRect(tr, btr, calc.MinX, calc.MinY, calc.MaxX, calc.MaxY, LayerBox));

                // === 图层：00二倍楼高范围（北向）===
                // 向上射线：① ③ ⑤ ⑦
                idsNorth.Add(DrawRay(tr, btr, calc.NW, calc.NW_Upper, LayerNorth));
                idsNorth.Add(DrawRay(tr, btr, calc.NE, calc.NE_Upper, LayerNorth));
                idsNorth.Add(DrawRay(tr, btr, calc.SW, calc.SW_Upper, LayerNorth));
                idsNorth.Add(DrawRay(tr, btr, calc.SE, calc.SE_Upper, LayerNorth));
                // 向上闭合线：①→⑤  ③→⑦
                idsNorth.Add(DrawLine(tr, btr, calc.NW_Upper, calc.SW_Upper, LayerNorth));
                idsNorth.Add(DrawLine(tr, btr, calc.NE_Upper, calc.SE_Upper, LayerNorth));
                // 上偏移线
                idsNorth.Add(DrawLine(tr, btr, calc.OffsetTopLeft, calc.OffsetTopRight, LayerNorth));
                // 北向圆弧
                idsNorth.Add(DrawArc(tr, btr, calc.GetArcNW(), LayerNorth));
                idsNorth.Add(DrawArc(tr, btr, calc.GetArcNE(), LayerNorth));

                // === 图层：00二百米范围（南向）===
                // 向下射线：② ④ ⑥ ⑧
                idsSouth.Add(DrawRay(tr, btr, calc.NW, calc.NW_Lower, LayerSouth));
                idsSouth.Add(DrawRay(tr, btr, calc.NE, calc.NE_Lower, LayerSouth));
                idsSouth.Add(DrawRay(tr, btr, calc.SW, calc.SW_Lower, LayerSouth));
                idsSouth.Add(DrawRay(tr, btr, calc.SE, calc.SE_Lower, LayerSouth));
                // 向下闭合线：②→⑥  ④→⑧
                idsSouth.Add(DrawLine(tr, btr, calc.NW_Lower, calc.SW_Lower, LayerSouth));
                idsSouth.Add(DrawLine(tr, btr, calc.NE_Lower, calc.SE_Lower, LayerSouth));
                // 下偏移线
                idsSouth.Add(DrawLine(tr, btr, calc.OffsetBottomLeft, calc.OffsetBottomRight, LayerSouth));
                // 南向圆弧
                idsSouth.Add(DrawArc(tr, btr, calc.GetArcSW(), LayerSouth));
                idsSouth.Add(DrawArc(tr, btr, calc.GetArcSE(), LayerSouth));

                // 编组
                CreateGroup(tr, db, "包围矩形", idsBox);
                CreateGroup(tr, db, "二倍楼高范围", idsNorth);
                CreateGroup(tr, db, "二百米范围", idsSouth);

                tr.Commit();
            }
        }

        private static void EnsureLayer(Transaction tr, Database db, string name)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
            if (!lt.Has(name))
            {
                var layer = new LayerTableRecord { Name = name };
                lt.Add(layer);
                tr.AddNewlyCreatedDBObject(layer, true);
            }
        }

        private static ObjectId DrawRay(Transaction tr, BlockTableRecord btr, Point2D from, Point2D to, string layer)
        {
            var line = new Line(To3d(from), To3d(to)) { Layer = layer };
            btr.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
            return line.ObjectId;
        }

        private static ObjectId DrawLine(Transaction tr, BlockTableRecord btr, Point2D p1, Point2D p2, string layer)
        {
            var line = new Line(To3d(p1), To3d(p2)) { Layer = layer };
            btr.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
            return line.ObjectId;
        }

        private static ObjectId DrawArc(Transaction tr, BlockTableRecord btr, ArcParams arc, string layer)
        {
            var center = new Point3d(arc.Center.X, arc.Center.Y, 0);
            var arcEnt = new Arc(center, arc.Radius,
                arc.StartAngleDeg * System.Math.PI / 180.0,
                arc.EndAngleDeg * System.Math.PI / 180.0)
            { Layer = layer };
            btr.AppendEntity(arcEnt);
            tr.AddNewlyCreatedDBObject(arcEnt, true);
            return arcEnt.ObjectId;
        }

        private static ObjectId DrawRect(Transaction tr, BlockTableRecord btr, double minX, double minY, double maxX, double maxY, string layer)
        {
            var pline = new Polyline();
            pline.AddVertexAt(0, new Point2d(minX, minY), 0, 0, 0);
            pline.AddVertexAt(1, new Point2d(maxX, minY), 0, 0, 0);
            pline.AddVertexAt(2, new Point2d(maxX, maxY), 0, 0, 0);
            pline.AddVertexAt(3, new Point2d(minX, maxY), 0, 0, 0);
            pline.Closed = true;
            pline.Layer = layer;
            btr.AppendEntity(pline);
            tr.AddNewlyCreatedDBObject(pline, true);
            return pline.ObjectId;
        }

        private static void CreateGroup(Transaction tr, Database db, string name, ObjectIdCollection ids)
        {
            if (ids.Count == 0) return;

            var group = new Group(name, true);
            foreach (ObjectId id in ids)
                group.Append(id);

            var groupDict = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
            groupDict.SetAt(name, group);
            tr.AddNewlyCreatedDBObject(group, true);
        }

        private static Point3d To3d(Point2D p) => new Point3d(p.X, p.Y, 0);
        private static Point2d To2d(Point2D p) => new Point2d(p.X, p.Y);
    }
}
