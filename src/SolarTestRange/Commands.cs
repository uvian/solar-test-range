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

                // 选择绘制模式（框选建筑轮廓之后）
                // 用 PromptKeywordOptions + Keywords.Add 注册三个关键字。
                // AllowNone = false 时用户必须选一个；ESC/回车取消 → PromptStatus.Cancel/Error。
                var kwOpts = new PromptKeywordOptions(
                    "\n选择绘制范围 [北向2H+南向200(N)/仅北向2H(B)/仅南向200(S)]: ")
                {
                    AllowNone = false
                };
                kwOpts.Keywords.Add("N");
                kwOpts.Keywords.Add("B");
                kwOpts.Keywords.Add("S");

                var kwRes = ed.GetKeywords(kwOpts);
                if (kwRes.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n已取消。");
                    return;
                }

                DrawMode mode;
                switch (kwRes.StringResult)
                {
                    case "N":
                        mode = DrawMode.NorthAndSouth;
                        break;
                    case "B":
                        mode = DrawMode.NorthOnly;
                        break;
                    case "S":
                        mode = DrawMode.SouthOnly;
                        break;
                    default:
                        ed.WriteMessage("\n未识别的模式，已取消。");
                        return;
                }
                ed.WriteMessage($"\n  绘制模式 = {mode.DisplayName()}");

                // 输入建筑高度（循环直到拿到有效值）
                // 仅南向模式不需要建筑高度：南向 200m 是固定值，与楼高无关。
                // ⚠️ 实测修复 (v1.0.5)：不能用带 DefaultValue 的 GetDouble——GetSelection 结束时的
                // 回车/空格会被紧接着的 GetDouble 吞掉，直接命中默认值 30 → 北向恒为 60m。
                // 改为 GetString + TryParse + 无默认值循环：空输入/无效输入一律重试，绝不静默用默认值。
                double height = 0.0;
                if (mode.RequiresHeight())
                {
                    while (true)
                    {
                        var heightRes = ed.GetString(new PromptStringOptions("\n输入建筑高度 (米): ")
                        {
                            AllowSpaces = false
                        });
                        if (heightRes.Status == PromptStatus.Cancel)
                        {
                            ed.WriteMessage("\n已取消。");
                            return;
                        }
                        if (heightRes.Status == PromptStatus.OK &&
                            double.TryParse(heightRes.StringResult, out height) &&
                            height > 0)
                        {
                            break;
                        }
                        ed.WriteMessage("\n输入无效，请重新输入建筑高度 (米): ");
                    }
                    ed.WriteMessage($"\n  建筑高度 = {height:F1} m → 北向范围 = {2 * height:F1} m");
                }
                else
                {
                    ed.WriteMessage("\n  仅南向模式：南向范围固定 200m，无需建筑高度。");
                }

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

                    var calc = new SolarRangeCalculator(minX, minY, maxX, maxY, height, mode);
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

                // 按实际模式汇报结果，避免在仅南向时仍显示无意义的北向数值
                var parts = new System.Collections.Generic.List<string>();
                if (mode.IncludesNorth()) parts.Add($"北向={2 * height:F1}m");
                if (mode.IncludesSouth()) parts.Add($"南向={SolarRangeCalculator.SouthLengthDefault:F0}m");
                string heightText = mode.RequiresHeight() ? $"H={height:F1}m, " : "";
                ed.WriteMessage($"\n✅ 日照测试范围已生成（{mode.DisplayName()}）。{heightText}{string.Join(", ", parts)}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n❌ 错误: {ex.Message}");
                ed.WriteMessage($"\n{ex}");
            }
        }
    }
}
