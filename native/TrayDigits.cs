using System;

namespace MouseBattery
{
    // A fixed 16x16 grid avoids font padding, wrapping and DPI-dependent clipping.
    internal static class TrayDigits
    {
        private static readonly string[] Glyphs = {
            "111101101101101101111", // 0
            "010110010010010010111", // 1
            "111001001111100100111", // 2
            "111001001111001001111", // 3
            "101101101111001001001", // 4
            "111100100111001001111", // 5
            "111100100111101101111", // 6
            "111001001010010010010", // 7
            "111101101111101101111", // 8
            "111101101111001001111", // 9
            "000000000111000000000"  // unknown marker
        };

        public static bool[,] Render(int? percent)
        {
            if (percent.HasValue && (percent.Value < 0 || percent.Value > 100))
                throw new ArgumentOutOfRangeException("percent");
            string text = percent.HasValue ? percent.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "--";
            int units = text.Length * 3 + text.Length - 1;
            int scaleX = Math.Max(1, Math.Min(3, 14 / units)), scaleY = 2;
            int left = (16 - units * scaleX) / 2, top = 1;
            bool[,] pixels = new bool[16, 16];
            for (int digit = 0; digit < text.Length; digit++)
            {
                string glyph = Glyphs[text[digit] == '-' ? 10 : text[digit] - '0'];
                for (int row = 0; row < 7; row++)
                    for (int column = 0; column < 3; column++)
                        if (glyph[row * 3 + column] == '1')
                            for (int y = 0; y < scaleY; y++)
                                for (int x = 0; x < scaleX; x++)
                                    pixels[left + (digit * 4 + column) * scaleX + x, top + row * scaleY + y] = true;
            }
            return pixels;
        }
    }
}
