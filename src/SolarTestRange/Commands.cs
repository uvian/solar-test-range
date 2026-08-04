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
                // 选择建筑轮廓多段线（支持一个或多个，框选/点选均可）
                var selOpts = new PromptSelectionOptions
                {
                    MessageForAdding = "\n选择建筑轮廓 (闭合多段线, 可多选): ",
                    AllowSubSelections = false
                };
                // 只允许选轻量多段线（与原有 AddAllowedClass(typeof(Polyline), true) 等价）
                var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE") });

                var selRes = ed.GetSelection(selOpts, filter);
                if (selRes.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n已取消。");
                    return;
                }
                if (selRes.Value == null || selRes.Value.Count == 0)
                {
                    ed.WriteMessage("\n未选择任何对象。");
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
                    // 遍历所有选中多段线，取闭合者的包围盒并集
                    double minX = double.MaxValue, minY = double.MaxValue;
                    double maxX = double.MinValue, maxY = double.MinValue;
                    int validCount = 0;
                    int skippedCount = 0;

                    foreach (ObjectId id in selRes.Value.GetObjectIds())
                    {
                        var pline = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                        if (pline == null || !pline.Closed)
                        {
                            skippedCount++;
                            continue;
                        }

                        var ext = pline.GeometricExtents;
                        minX = Math.Min(minX, ext.MinPoint.X);
                        minY = Math.Min(minY, ext.MinPoint.Y);
                        maxX = Math.Max(maxX, ext.MaxPoint.X);
                        maxY = Math.Max(maxY, ext.MaxPoint.Y);
                        validCount++;
                    }

                    if (validCount == 0)
                    {
                        ed.WriteMessage("\n错误: 所选对象中没有闭合多段线。");
                        tr.Abort();
                        return;
                    }
                    if (skippedCount > 0)
                        ed.WriteMessage($"\n  已忽略 {skippedCount} 条非闭合多段线。");

                    ed.WriteMessage($"\n  已选取 {validCount} 条闭合多段线。");
                    ed.WriteMessage($"\n  并集包围盒: ({minX:F2},{minY:F2}) -> ({maxX:F2},{maxY:F2})");

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
