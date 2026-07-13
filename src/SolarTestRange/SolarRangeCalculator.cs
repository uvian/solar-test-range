using System;

namespace SolarTestRange
{
    /// <summary>
    /// 纯几何计算，不依赖 AutoCAD API。
    /// 输入：建筑轮廓包围盒 + 建筑高度 H
    /// 输出：所有关键点坐标和弧参数
    /// </summary>
    public class SolarRangeCalculator
    {
        private const double AngleDeg = 35.0;
        private const double SouthOffset = 200.0;
        private readonly double _cosA;
        private readonly double _sinA;

        public double BuildingHeight { get; }
        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }

        public Point2D NW => new(MinX, MaxY);
        public Point2D NE => new(MaxX, MaxY);
        public Point2D SW => new(MinX, MinY);
        public Point2D SE => new(MaxX, MinY);

        public SolarRangeCalculator(double minX, double minY, double maxX, double maxY, double buildingHeight)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
            BuildingHeight = buildingHeight;

            double rad = AngleDeg * Math.PI / 180.0;
            _cosA = Math.Cos(rad);
            _sinA = Math.Sin(rad);
        }

        public double NorthLength => 2.0 * BuildingHeight;
        public double SouthLength => SouthOffset;

        #region 八角射线端点

        /// <summary>① 左上→向上35°</summary>
        public Point2D NW_Upper => NW.Offset(-NorthLength * _cosA, NorthLength * _sinA);
        /// <summary>② 左上→向下35°</summary>
        public Point2D NW_Lower => NW.Offset(-SouthLength * _cosA, -SouthLength * _sinA);
        /// <summary>③ 右上→向上35°</summary>
        public Point2D NE_Upper => NE.Offset(NorthLength * _cosA, NorthLength * _sinA);
        /// <summary>④ 右上→向下35°</summary>
        public Point2D NE_Lower => NE.Offset(SouthLength * _cosA, -SouthLength * _sinA);
        /// <summary>⑤ 左下→向上35°</summary>
        public Point2D SW_Upper => SW.Offset(-NorthLength * _cosA, NorthLength * _sinA);
        /// <summary>⑥ 左下→向下35°</summary>
        public Point2D SW_Lower => SW.Offset(-SouthLength * _cosA, -SouthLength * _sinA);
        /// <summary>⑦ 右下→向上35°</summary>
        public Point2D SE_Upper => SE.Offset(NorthLength * _cosA, NorthLength * _sinA);
        /// <summary>⑧ 右下→向下35°</summary>
        public Point2D SE_Lower => SE.Offset(SouthLength * _cosA, -SouthLength * _sinA);

        #endregion

        #region 偏移线端点

        public Point2D OffsetTopLeft => new(MinX, MaxY + NorthLength);
        public Point2D OffsetTopRight => new(MaxX, MaxY + NorthLength);
        public Point2D OffsetBottomLeft => new(MinX, MinY - SouthLength);
        public Point2D OffsetBottomRight => new(MaxX, MinY - SouthLength);

        #endregion

        #region 圆弧参数（角度制）

        public ArcParams GetArcNW() => new(NW, NorthLength, 90, 180 - AngleDeg);
        public ArcParams GetArcNE() => new(NE, NorthLength, 35, 90);
        public ArcParams GetArcSW() => new(SW, SouthLength, 180 + AngleDeg, 270);
        public ArcParams GetArcSE() => new(SE, SouthLength, 270, 360 - AngleDeg);

        #endregion

        public string Validate()
        {
            if (BuildingHeight <= 0) return "建筑高度必须大于 0";
            if (MaxX <= MinX || MaxY <= MinY) return "包围盒无效";
            return null;
        }
    }

    public struct Point2D
    {
        public double X { get; }
        public double Y { get; }
        public Point2D(double x, double y) { X = x; Y = y; }
        public Point2D Offset(double dx, double dy) => new(X + dx, Y + dy);
        public override string ToString() => $"({X:F2}, {Y:F2})";
    }

    public struct ArcParams
    {
        public Point2D Center { get; }
        public double Radius { get; }
        public double StartAngleDeg { get; }
        public double EndAngleDeg { get; }
        public ArcParams(Point2D center, double radius, double startDeg, double endDeg)
        {
            Center = center;
            Radius = radius;
            StartAngleDeg = startDeg;
            EndAngleDeg = endDeg;
        }
    }
}
