// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Buffers;
using Stride.Core.Mathematics;

namespace Stride.Graphics.Font.RuntimeMsdf
{
    /// <summary>
    /// Bitmap-based SDF rasterizer using Euclidean Distance Transform.
    /// Used as a fallback when outline data is not available (e.g., bitmap fonts or coverage-based rendering).
    /// Generates single-channel SDF packed into RGB channels (median(R,G,B) = SDF value).
    /// </summary>
    public sealed class BitmapSdfRasterizer
    {
        /// <summary>
        /// Generate SDF from a coverage bitmap (byte array where values >= 128 are considered "inside").
        /// </summary>
        /// <param name="coverageBuffer">Source coverage bitmap data</param>
        /// <param name="width">Width of the source bitmap</param>
        /// <param name="height">Height of the source bitmap</param>
        /// <param name="pitch">Row stride in bytes of the source bitmap</param>
        /// <param name="padding">Padding to add around the glyph</param>
        /// <param name="pixelRange">Distance field range in pixels</param>
        /// <param name="encodeBias">Encoding bias (typically 0.4)</param>
        /// <param name="encodeScale">Encoding scale (typically 0.5)</param>
        /// <returns>RGBA bitmap with SDF packed into RGB channels</returns>
        internal CharacterBitmapRgba GenerateSdfFromCoverage(
            byte[] coverageBuffer,
            int width,
            int height,
            int pitch,
            int padding,
            int pixelRange,
            float encodeBias = 0.4f,
            float encodeScale = 0.5f)
        {
            ArgumentNullException.ThrowIfNull(coverageBuffer);

            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (pitch < width) throw new ArgumentOutOfRangeException(nameof(pitch));
            if (padding < 0) throw new ArgumentOutOfRangeException(nameof(padding));
            if (pixelRange <= 0) throw new ArgumentOutOfRangeException(nameof(pixelRange));

            int outputWidth = width + padding * 2;
            int outputHeight = height + padding * 2;

            // Build binary inside/outside map with padding
            var inside = new bool[outputWidth * outputHeight];

            for (int y = 0; y < height; y++)
            {
                int dstRow = (y + padding) * outputWidth + padding;
                int srcRow = y * pitch;

                for (int x = 0; x < width; x++)
                {
                    inside[dstRow + x] = coverageBuffer[srcRow + x] >= 128;
                }
            }

            // Compute distance to outside
            var distToOutsideSq = new float[outputWidth * outputHeight];
            ComputeEdtSquared(outputWidth, outputHeight, inside, featureIsInside: false, distToOutsideSq);

            // Compute distance to inside
            var distToInsideSq = new float[outputWidth * outputHeight];
            ComputeEdtSquared(outputWidth, outputHeight, inside, featureIsInside: true, distToInsideSq);

            // Pack into RGBA bitmap
            var result = new CharacterBitmapRgba(outputWidth, outputHeight);
            
            float scale = encodeScale / Math.Max(1, pixelRange);

            for (int y = 0; y < outputHeight; y++)
            {
                int baseIdx = y * outputWidth;

                for (int x = 0; x < outputWidth; x++)
                {
                    int i = baseIdx + x;
                    float dOut = MathF.Sqrt(distToOutsideSq[i]);
                    float dIn = MathF.Sqrt(distToInsideSq[i]);
                    float signedDistance = dOut - dIn;

                    float encoded = Math.Clamp(encodeBias + signedDistance * scale, 0f, 1f);
                    byte value = (byte)(encoded * 255f + 0.5f);

                    // Pack into RGB (all channels same for single-channel SDF)
                    int pixelIndex = y * outputWidth + x;
                    result.Buffer[pixelIndex] = new Color(value, value, value, 255);
                }
            }

            return result;
        }

        /// <summary>
        /// Compute squared Euclidean distances to the nearest feature pixels using Felzenszwalb/Huttenlocher EDT.
        /// </summary>
        /// <param name="width">Width of the bitmap</param>
        /// <param name="height">Height of the bitmap</param>
        /// <param name="inside">Binary map indicating inside/outside</param>
        /// <param name="featureIsInside">If true, features are where inside==true; else features are where inside==false</param>
        /// <param name="outDistSq">Output squared distance values</param>
        private static void ComputeEdtSquared(int width, int height, bool[] inside, bool featureIsInside, float[] outDistSq)
        {
            const float INF = 1e20f;
            int maxDim = Math.Max(width, height);

            // Rent buffers from the shared pool to avoid allocations
            float[] tmp = ArrayPool<float>.Shared.Rent(width * height);
            float[] f = ArrayPool<float>.Shared.Rent(maxDim);
            float[] d = ArrayPool<float>.Shared.Rent(maxDim);
            int[] v = ArrayPool<int>.Shared.Rent(maxDim);
            float[] z = ArrayPool<float>.Shared.Rent(maxDim + 1);

            try
            {
                // Stage 1: Vertical transform (process each column)
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        bool isFeature = (inside[y * width + x] == featureIsInside);
                        f[y] = isFeature ? 0f : INF;
                    }

                    DistanceTransform1D(f, height, d, v, z);

                    for (int y = 0; y < height; y++)
                        tmp[y * width + x] = d[y];
                }

                // Stage 2: Horizontal transform (process each row)
                for (int y = 0; y < height; y++)
                {
                    int row = y * width;
                    for (int x = 0; x < width; x++)
                        f[x] = tmp[row + x];

                    DistanceTransform1D(f, width, d, v, z);

                    for (int x = 0; x < width; x++)
                        outDistSq[row + x] = d[x];
                }
            }
            finally
            {
                // Always return rented arrays to the pool
                ArrayPool<float>.Shared.Return(tmp);
                ArrayPool<float>.Shared.Return(f);
                ArrayPool<float>.Shared.Return(d);
                ArrayPool<int>.Shared.Return(v);
                ArrayPool<float>.Shared.Return(z);
            }
        }

        /// <summary>
        /// 1D squared distance transform using lower envelope of parabolas.
        /// Produces d[i] = min_j ( (i-j)^2 + f[j] )
        /// </summary>
        private static void DistanceTransform1D(float[] f, int n, float[] d, int[] v, float[] z)
        {
            int k = 0;
            v[0] = 0;
            z[0] = float.NegativeInfinity;
            z[1] = float.PositiveInfinity;

            // Build lower envelope
            for (int q = 1; q < n; q++)
            {
                float s;
                while (true)
                {
                    int p = v[k];
                    // Intersection of parabolas from p and q
                    s = ((f[q] + q * q) - (f[p] + p * p)) / (2f * (q - p));

                    if (s > z[k]) break;
                    k--;
                }

                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = float.PositiveInfinity;
            }

            // Query lower envelope
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                int p = v[k];
                float dx = q - p;
                d[q] = dx * dx + f[p];
            }
        }
    }
}
