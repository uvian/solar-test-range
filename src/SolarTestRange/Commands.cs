using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace SolarTestRange
{
    public class Commands
    {
        [CommandMethod("HUATU")]
        public void TestRange()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            ed.WriteMessage("\n--- SolarTestRange 插件已启动 ---");

            try
            {
                // 选择建筑轮廓多段线
                var selOpts = new PromptEntityOptions("\n选择建筑轮廓 (闭合多段线): ");
                selOpts.SetRejectMessage("请选择闭合多段线。");
                selOpts.AddAllowedClass(typeof(Polyline), true);

                var selRes = ed.GetEntity(selOpts);
                if (selRes.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n已取消。");
                    return;
                }

                // 输入建筑高度
                var heightRes = ed.GetDouble(new PromptDoubleOptions("\n输入建筑高度 (米): ")
                {
                    AllowNegative = false,
                    AllowZero = false,
                    DefaultValue = 30.0
                });
                if (heightRes.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n已取消。");
                    return;
                }
                double height = heightRes.Value;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var pline = tr.GetObject(selRes.ObjectId, OpenMode.ForRead) as Polyline;
                    if (pline == null || !pline.Closed)
                    {
                        ed.WriteMessage("\n错误: 选中的不是闭合多段线。");
                        tr.Abort();
                        return;
                    }

                    var extents = pline.GeometricExtents;
                    double minX = extents.MinPoint.X;
                    double minY = extents.MinPoint.Y;
                    double maxX = extents.MaxPoint.X;
                    double maxY = extents.MaxPoint.Y;
                    ed.WriteMessage($"\n  包围盒: ({minX:F2},{minY:F2}) -> ({maxX:F2},{maxY:F2})");

                    var calc = new SolarRangeCalculator(minX, minY, maxX, maxY, height);
                    string valErr = calc.Validate();
                    if (valErr != null)
                    {
                        ed.WriteMessage($"\n参数错误: {valErr}");
                        tr.Abort();
                        return;
                    }

                    RangeDrawer.Draw(db, calc);
                    tr.Commit();
                }

                ed.WriteMessage($"\n✅ 日照测试范围已生成。H={height:F1}m, 北向={2*height:F1}m, 南向=200m");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n❌ 错误: {ex.Message}");
                ed.WriteMessage($"\n{ex}");
            }
        }
    }
}
