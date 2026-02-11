// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using Stride.Core.Mathematics;

namespace Stride.Graphics.Font
{
    /// <summary>
    /// An RGBA bitmap representing a glyph.
    /// Intended for runtime MSDF font (stored in RGB, alpha optional).
    /// </summary>
    internal sealed class CharacterBitmapRgba
    {
        private readonly int width;
        private readonly int rows;
        private readonly Color[] buffer;

        /// <summary>
        /// Initializes a null bitmap.
        /// </summary>
        public CharacterBitmapRgba()
        {
        }

        /// <summary>
        /// Allocates an RGBA bitmap (uninitialized).
        /// </summary>
        public CharacterBitmapRgba(int width, int rows)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(width);
            ArgumentOutOfRangeException.ThrowIfNegative(rows);

            this.width = width;
            this.rows = rows;

            if (width != 0 && rows != 0)
            {
                buffer = new Color[width * rows];
            }
        }

        /// <summary>
        /// Allocates an RGBA bitmap and copies data from a source buffer with the given pitch.
        /// </summary>
        public unsafe CharacterBitmapRgba(IntPtr srcRgba, int width, int rows, int srcPitchBytes)
            : this(width, rows)
        {
            if (srcRgba == IntPtr.Zero && (width != 0 || rows != 0))
                throw new ArgumentNullException(nameof(srcRgba));
            ArgumentOutOfRangeException.ThrowIfNegative(srcPitchBytes);

            if (buffer == null)
                return;

            var src = (byte*)srcRgba;

            // Copy row-by-row to handle pitch differences.
            for (int y = 0; y < rows; y++)
            {
                var srcRow = src + y * srcPitchBytes;

                for (int x = 0; x < width; x++)
                {
                    int bufferIndex = y * width + x;
                    int srcOffset = x * 4;

                    buffer[bufferIndex] = new Color(
                        srcRow[srcOffset],     // R
                        srcRow[srcOffset + 1], // G
                        srcRow[srcOffset + 2], // B
                        srcRow[srcOffset + 3]  // A
                    );
                }
            }
        }

        public int Width => width;

        public int Rows => rows;

        public Color[] Buffer => buffer;
    }
}
