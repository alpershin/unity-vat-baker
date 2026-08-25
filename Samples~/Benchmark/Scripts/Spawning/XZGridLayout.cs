using UnityEngine;

namespace Alpershin.Vat.Samples.Spawning
{
    /// <summary>
    /// Cell centers of a rectangular grid lying in the XZ plane, in local space.
    /// </summary>
    public sealed class XZGridLayout
    {
        private readonly int _columns;
        private readonly int _rows;
        private readonly Vector2 _spacing;
        private readonly Vector3 _origin;

        public XZGridLayout(int columns, int rows, Vector2 spacing, bool centered)
            : this(columns, rows, spacing, CenteredOrigin(columns, rows, spacing, centered))
        {
        }

        private XZGridLayout(int columns, int rows, Vector2 spacing, Vector3 origin)
        {
            _columns = Mathf.Max(1, columns);
            _rows = Mathf.Max(1, rows);
            _spacing = spacing;
            _origin = origin;
        }

        public int Columns => _columns;
        public int Rows => _rows;
        public int CellCount => _columns * _rows;

        /// <summary>
        /// Densest grid of the given spacing that fits inside <paramref name="area"/>, centered in it.
        /// The rect lives in the XZ plane: x/width map to X, y/height map to Z.
        /// </summary>
        public static XZGridLayout FromArea(Rect area, Vector2 spacing)
        {
            var columns = CellsAlong(area.width, spacing.x);
            var rows = CellsAlong(area.height, spacing.y);
            var origin = new Vector3(
                area.center.x - (columns - 1) * spacing.x * 0.5f,
                0f,
                area.center.y - (rows - 1) * spacing.y * 0.5f);

            return new XZGridLayout(columns, rows, spacing, origin);
        }

        public Vector3 GetCell(int column, int row)
        {
            return _origin + new Vector3(column * _spacing.x, 0f, row * _spacing.y);
        }

        public Vector3 GetCell(int index)
        {
            return GetCell(index % _columns, index / _columns);
        }

        private static int CellsAlong(float size, float spacing)
        {
            return spacing > Mathf.Epsilon ? Mathf.Max(1, Mathf.FloorToInt(size / spacing) + 1) : 1;
        }

        private static Vector3 CenteredOrigin(int columns, int rows, Vector2 spacing, bool centered)
        {
            if (!centered)
            {
                return Vector3.zero;
            }

            return new Vector3(
                -(Mathf.Max(1, columns) - 1) * spacing.x * 0.5f,
                0f,
                -(Mathf.Max(1, rows) - 1) * spacing.y * 0.5f);
        }
    }
}
