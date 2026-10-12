// Self-contained QR Code encoder (ISO/IEC 18004), byte mode, versions 1-40.
// No external libraries required.

using System;
using System.Collections.Generic;
using System.Text;

namespace QRCodeWin
{
    /// <summary>Error correction level.</summary>
    public enum Ecc { L = 0, M = 1, Q = 2, H = 3 }

    public sealed class QrCode
    {
        public int Version { get; private set; }
        public int Size { get; private set; }
        public Ecc ErrorCorrection { get; private set; }
        public int Mask { get; private set; }

        private readonly bool[,] modules;   // [y, x], true = dark
        private bool[,] isFunction;          // only used while building

        /// <summary>True if the module at column x, row y is dark.</summary>
        public bool this[int x, int y] { get { return modules[y, x]; } }

        // ---------- Public factory methods ----------

        public static QrCode EncodeText(string text, Ecc ecl)
        {
            if (text == null) throw new ArgumentNullException("text");
            return EncodeBytes(Encoding.UTF8.GetBytes(text), ecl);
        }

        public static QrCode EncodeBytes(byte[] data, Ecc ecl)
        {
            if (data == null) throw new ArgumentNullException("data");

            // Find the smallest version that fits the data.
            int version = 1;
            int capacityBits;
            while (true)
            {
                capacityBits = GetNumDataCodewords(version, ecl) * 8;
                int countBits = version <= 9 ? 8 : 16;
                int usedBits = 4 + countBits + data.Length * 8;
                if (usedBits <= capacityBits) break;
                if (version >= 40)
                    throw new ArgumentException("The text is too long to fit in a QR code.");
                version++;
            }

            // Build the bit stream: mode indicator, length, data, terminator, padding.
            var bits = new List<bool>();
            AppendBits(bits, 0x4, 4);                                   // byte mode
            AppendBits(bits, data.Length, version <= 9 ? 8 : 16);
            foreach (byte b in data) AppendBits(bits, b, 8);
            AppendBits(bits, 0, Math.Min(4, capacityBits - bits.Count)); // terminator
            AppendBits(bits, 0, (8 - bits.Count % 8) % 8);              // byte align
            for (int pad = 0xEC; bits.Count < capacityBits; pad ^= 0xEC ^ 0x11)
                AppendBits(bits, pad, 8);

            byte[] codewords = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++)
                if (bits[i]) codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));

            return new QrCode(version, ecl, codewords);
        }

        // ---------- Construction ----------

        private QrCode(int version, Ecc ecl, byte[] dataCodewords)
        {
            Version = version;
            ErrorCorrection = ecl;
            Size = version * 4 + 17;
            modules = new bool[Size, Size];
            isFunction = new bool[Size, Size];

            DrawFunctionPatterns();
            DrawCodewords(AddEccAndInterleave(dataCodewords));

            // Pick the mask with the lowest penalty score.
            int best = 0, minPenalty = int.MaxValue;
            for (int m = 0; m < 8; m++)
            {
                ApplyMask(m);
                DrawFormatBits(m);
                int p = GetPenaltyScore();
                if (p < minPenalty) { minPenalty = p; best = m; }
                ApplyMask(m); // XOR again to undo
            }
            Mask = best;
            ApplyMask(best);
            DrawFormatBits(best);
            isFunction = null;
        }

        private void DrawFunctionPatterns()
        {
            // Timing patterns
            for (int i = 0; i < Size; i++)
            {
                SetFunction(6, i, i % 2 == 0);
                SetFunction(i, 6, i % 2 == 0);
            }

            // Finder patterns (with separators)
            DrawFinder(3, 3);
            DrawFinder(Size - 4, 3);
            DrawFinder(3, Size - 4);

            // Alignment patterns
            int[] pos = GetAlignmentPatternPositions();
            int n = pos.Length;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    bool overlapsFinder = (i == 0 && j == 0) || (i == 0 && j == n - 1) || (i == n - 1 && j == 0);
                    if (!overlapsFinder) DrawAlignment(pos[i], pos[j]);
                }

            DrawFormatBits(0); // placeholder, overwritten later
            DrawVersion();
        }

        private void DrawFinder(int cx, int cy)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || x >= Size || y < 0 || y >= Size) continue;
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    SetFunction(x, y, dist != 2 && dist != 4);
                }
        }

        private void DrawAlignment(int cx, int cy)
        {
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    SetFunction(cx + dx, cy + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        }

        private void DrawFormatBits(int mask)
        {
            int eccBits;
            switch (ErrorCorrection)
            {
                case Ecc.L: eccBits = 1; break;
                case Ecc.M: eccBits = 0; break;
                case Ecc.Q: eccBits = 3; break;
                default: eccBits = 2; break;
            }
            int data = (eccBits << 3) | mask;
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = ((data << 10) | rem) ^ 0x5412;

            // First copy (around top-left finder)
            for (int i = 0; i <= 5; i++) SetFunction(8, i, GetBit(bits, i));
            SetFunction(8, 7, GetBit(bits, 6));
            SetFunction(8, 8, GetBit(bits, 7));
            SetFunction(7, 8, GetBit(bits, 8));
            for (int i = 9; i < 15; i++) SetFunction(14 - i, 8, GetBit(bits, i));

            // Second copy (split between the other two finders)
            for (int i = 0; i < 8; i++) SetFunction(Size - 1 - i, 8, GetBit(bits, i));
            for (int i = 8; i < 15; i++) SetFunction(8, Size - 15 + i, GetBit(bits, i));
            SetFunction(8, Size - 8, true); // dark module
        }

        private void DrawVersion()
        {
            if (Version < 7) return;
            int rem = Version;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = (Version << 12) | rem;
            for (int i = 0; i < 18; i++)
            {
                bool bit = GetBit(bits, i);
                int a = Size - 11 + i % 3, b = i / 3;
                SetFunction(a, b, bit);
                SetFunction(b, a, bit);
            }
        }

        private int[] GetAlignmentPatternPositions()
        {
            if (Version == 1) return new int[0];
            int numAlign = Version / 7 + 2;
            int step = (Version * 8 + numAlign * 3 + 5) / (numAlign * 4 - 4) * 2;
            int[] result = new int[numAlign];
            result[0] = 6;
            for (int i = numAlign - 1, pos = Size - 7; i >= 1; i--, pos -= step)
                result[i] = pos;
            return result;
        }

        private void SetFunction(int x, int y, bool dark)
        {
            modules[y, x] = dark;
            isFunction[y, x] = true;
        }

        // ---------- Error correction ----------

        private byte[] AddEccAndInterleave(byte[] data)
        {
            int numBlocks = NumErrorCorrectionBlocks[(int)ErrorCorrection, Version];
            int blockEccLen = EccCodewordsPerBlock[(int)ErrorCorrection, Version];
            int rawCodewords = GetNumRawDataModules(Version) / 8;
            int numShortBlocks = numBlocks - rawCodewords % numBlocks;
            int shortBlockLen = rawCodewords / numBlocks;

            byte[] divisor = ReedSolomonDivisor(blockEccLen);
            var blocks = new List<byte[]>();
            for (int i = 0, k = 0; i < numBlocks; i++)
            {
                int datLen = shortBlockLen - blockEccLen + (i < numShortBlocks ? 0 : 1);
                byte[] dat = new byte[datLen];
                Array.Copy(data, k, dat, 0, datLen);
                k += datLen;
                byte[] ecc = ReedSolomonRemainder(dat, divisor);

                byte[] block = new byte[shortBlockLen + 1];
                Array.Copy(dat, 0, block, 0, datLen);
                Array.Copy(ecc, 0, block, block.Length - blockEccLen, blockEccLen);
                blocks.Add(block);
            }

            // Interleave, skipping the padding byte of short blocks.
            byte[] result = new byte[rawCodewords];
            int idx = 0;
            for (int i = 0; i < blocks[0].Length; i++)
                for (int j = 0; j < blocks.Count; j++)
                    if (i != shortBlockLen - blockEccLen || j >= numShortBlocks)
                        result[idx++] = blocks[j][i];
            return result;
        }

        private static byte[] ReedSolomonDivisor(int degree)
        {
            byte[] result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    result[j] = (byte)GfMultiply(result[j], root);
                    if (j + 1 < degree) result[j] ^= result[j + 1];
                }
                root = GfMultiply(root, 0x02);
            }
            return result;
        }

        private static byte[] ReedSolomonRemainder(byte[] data, byte[] divisor)
        {
            byte[] result = new byte[divisor.Length];
            foreach (byte b in data)
            {
                int factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int i = 0; i < result.Length; i++)
                    result[i] ^= (byte)GfMultiply(divisor[i], factor);
            }
            return result;
        }

        private static int GfMultiply(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return z;
        }

        // ---------- Data placement and masking ----------

        private void DrawCodewords(byte[] data)
        {
            int i = 0;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5; // skip vertical timing column
                for (int vert = 0; vert < Size; vert++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? Size - 1 - vert : vert;
                        if (!isFunction[y, x] && i < data.Length * 8)
                        {
                            modules[y, x] = GetBit(data[i >> 3], 7 - (i & 7));
                            i++;
                        }
                    }
                }
            }
        }

        private void ApplyMask(int mask)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    bool invert;
                    switch (mask)
                    {
                        case 0: invert = (x + y) % 2 == 0; break;
                        case 1: invert = y % 2 == 0; break;
                        case 2: invert = x % 3 == 0; break;
                        case 3: invert = (x + y) % 3 == 0; break;
                        case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                        case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (invert && !isFunction[y, x]) modules[y, x] = !modules[y, x];
                }
        }

        private int GetPenaltyScore()
        {
            int penalty = 0;

            // Rule 1: runs of 5+ same-colored modules in a row or column
            for (int a = 0; a < Size; a++)
            {
                int runRow = 1, runCol = 1;
                for (int b = 1; b < Size; b++)
                {
                    if (modules[a, b] == modules[a, b - 1]) runRow++;
                    else { if (runRow >= 5) penalty += runRow - 2; runRow = 1; }
                    if (modules[b, a] == modules[b - 1, a]) runCol++;
                    else { if (runCol >= 5) penalty += runCol - 2; runCol = 1; }
                }
                if (runRow >= 5) penalty += runRow - 2;
                if (runCol >= 5) penalty += runCol - 2;
            }

            // Rule 2: 2x2 blocks of the same color
            for (int y = 0; y < Size - 1; y++)
                for (int x = 0; x < Size - 1; x++)
                {
                    bool c = modules[y, x];
                    if (c == modules[y, x + 1] && c == modules[y + 1, x] && c == modules[y + 1, x + 1])
                        penalty += 3;
                }

            // Rule 3: finder-like patterns 1:1:3:1:1 with 4 light modules on one side
            for (int a = 0; a < Size; a++)
                for (int b = 0; b + 10 < Size; b++)
                {
                    if (MatchesFinderLike(a, b, true)) penalty += 40;
                    if (MatchesFinderLike(a, b, false)) penalty += 40;
                }

            // Rule 4: balance of dark and light modules
            int dark = 0;
            foreach (bool m in modules) if (m) dark++;
            int total = Size * Size;
            int percent = dark * 100 / total;
            penalty += Math.Abs(percent - 50) / 5 * 10;

            return penalty;
        }

        private static readonly bool[] PatternA = { true, false, true, true, true, false, true, false, false, false, false };
        private static readonly bool[] PatternB = { false, false, false, false, true, false, true, true, true, false, true };

        private bool MatchesFinderLike(int line, int start, bool horizontal)
        {
            bool matchA = true, matchB = true;
            for (int k = 0; k < 11; k++)
            {
                bool m = horizontal ? modules[line, start + k] : modules[start + k, line];
                if (m != PatternA[k]) matchA = false;
                if (m != PatternB[k]) matchB = false;
                if (!matchA && !matchB) return false;
            }
            return true;
        }

        // ---------- Helpers and tables ----------

        private static void AppendBits(List<bool> bits, int value, int length)
        {
            for (int i = length - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
        }

        private static bool GetBit(int x, int i) { return ((x >> i) & 1) != 0; }

        private static int GetNumRawDataModules(int ver)
        {
            int result = (16 * ver + 128) * ver + 64;
            if (ver >= 2)
            {
                int numAlign = ver / 7 + 2;
                result -= (25 * numAlign - 10) * numAlign - 55;
                if (ver >= 7) result -= 36;
            }
            return result;
        }

        private static int GetNumDataCodewords(int ver, Ecc ecl)
        {
            return GetNumRawDataModules(ver) / 8
                - EccCodewordsPerBlock[(int)ecl, ver] * NumErrorCorrectionBlocks[(int)ecl, ver];
        }

        private static readonly int[,] EccCodewordsPerBlock =
        {
            // Version: (index 0 unused) 1 .. 40
            { -1,  7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 }, // L
            { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 }, // M
            { -1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 }, // Q
            { -1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 }, // H
        };

        private static readonly int[,] NumErrorCorrectionBlocks =
        {
            { -1, 1, 1, 1, 1, 1, 2, 2, 2, 2,  4,  4,  4,  4,  4,  6,  6,  6,  6,  7,  8,  8,  9,  9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25 }, // L
            { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5,  5,  5,  8,  9,  9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 }, // M
            { -1, 1, 1, 2, 2, 4, 4, 6, 6, 8,  8,  8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68 }, // Q
            { -1, 1, 1, 2, 4, 4, 4, 5, 6, 8,  8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81 }, // H
        };
    }
}
