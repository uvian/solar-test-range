namespace SolarTestRange
{
    /// <summary>
    /// 绘制范围模式。决定 HUATU 命令最终生成哪一侧（或两侧）的日照测试范围。
    /// </summary>
    public enum DrawMode
    {
        /// <summary>北向 2H + 南向 200m（两侧全画，默认场景）</summary>
        NorthAndSouth = 0,

        /// <summary>仅绘制北向 2H 范围</summary>
        NorthOnly = 1,

        /// <summary>仅绘制南向 200m 范围</summary>
        SouthOnly = 2,
    }

    public static class DrawModeExtensions
    {
        /// <summary>是否需要北向 2H 扇形</summary>
        public static bool IncludesNorth(this DrawMode mode)
            => mode == DrawMode.NorthAndSouth || mode == DrawMode.NorthOnly;

        /// <summary>是否需要南向 200m 扇形</summary>
        public static bool IncludesSouth(this DrawMode mode)
            => mode == DrawMode.NorthAndSouth || mode == DrawMode.SouthOnly;

        /// <summary>该模式是否需要建筑高度（仅南向时 200m 为固定值，与楼高无关）</summary>
        public static bool RequiresHeight(this DrawMode mode) => mode.IncludesNorth();

        /// <summary>命令行的简短显示名</summary>
        public static string DisplayName(this DrawMode mode)
        {
            switch (mode)
            {
                case DrawMode.NorthOnly: return "仅北向 2H";
                case DrawMode.SouthOnly: return "仅南向 200m";
                default: return "北向 2H + 南向 200m";
            }
        }
    }
}
