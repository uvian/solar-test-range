using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(SolarTestRange.Commands))]

namespace SolarTestRange
{
    public class Commands
    {
        /// <summary>
        /// 主命令：TESTRANGE — 绘制日照测试扇形范围
        /// </summary>
        [CommandMethod("TESTRANGE")]
        public void TestRange()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                // Step 1: 用户选择建筑轮廓多段线
                var selOpts = new PromptEntityOptions("\n选择建筑轮廓多段线: ");
                selOpts.SetRejectMessage("请选择闭合多段线。");
                selOpts.AddAllowedClass(typeof(Polyline), true);

                var selRes = ed.GetEntity(selOpts);
                if (selRes.Status != PromptStatus.OK) return;

                // Step 2: 读取建筑高度
                var heightRes = ed.GetDouble(new PromptDoubleOptions("\n输入建筑高度（米）: ")
                {
                    AllowNegative = false,
                    AllowZero = false,
                    DefaultValue = 30.0
                });
                if (heightRes.Status != PromptStatus.OK) return;
                double height = heightRes.Value;

                // Step 3: 获取包围盒
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    var btr = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                    var pline = tr.GetObject(selRes.ObjectId, OpenMode.ForRead) as Polyline;
                    if (pline == null || !pline.Closed)
                    {
                        ed.WriteMessage("\n错误: 请选择一条闭合的多段线。");
                        return;
                    }

                    var extents = pline.GeometricExtents;
                    double minX = extents.MinPoint.X;
                    double minY = extents.MinPoint.Y;
                    double maxX = extents.MaxPoint.X;
                    double maxY = extents.MaxPoint.Y;

                    // Step 4: 计算几何
                    var calc = new SolarRangeCalculator(minX, minY, maxX, maxY, height);
                    string err = calc.Validate();
                    if (err != null)
                    {
                        ed.WriteMessage($"\n参数错误: {err}");
                        return;
                    }

                    // Step 5: 绘制
                    RangeDrawer.Draw(db, calc);

                    tr.Commit();
                }

                ed.WriteMessage("\n✅ 日照测试范围绘制完成。");
                ed.WriteMessage($"\n   建筑高度: {height:F1}m");
                ed.WriteMessage($"\n   向北延伸: {2 * height:F1}m, 向南延伸: 200m");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n❌ 错误: {ex.Message}");
                ed.WriteMessage($"\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// 设置南向偏移距离（默认200m）
        /// </summary>
        private static double _southOffset = 200.0;
        public static double SouthOffset => _southOffset;
    }
}
