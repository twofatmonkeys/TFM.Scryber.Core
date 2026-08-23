/*  Copyright 2012 PerceiveIT Limited
 *  This file is part of the Scryber library.
 *
 *  You can redistribute Scryber and/or modify
 *  it under the terms of the GNU Lesser General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  Scryber is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU Lesser General Public License for more details.
 *
 *  You should have received a copy of the GNU Lesser General Public License
 *  along with Scryber source code in the COPYING.txt file.  If not, see <http://www.gnu.org/licenses/>.
 *
 */

using System;
using System.Collections.Generic;

namespace Scryber.PDF.Resources
{
    /// <summary>
    /// The font widths for a simple font rendered with the PDF /WinAnsiEncoding, which converts
    /// each character to the single byte that encoding gives it.
    /// </summary>
    /// <remarks>
    /// The base class writes a character straight out as its own code point, which only agrees
    /// with the declared encoding over ASCII. /WinAnsiEncoding is CP1252, and CP1252 differs from
    /// Latin-1 over 0x80 to 0x9F, where it carries the curly quotes, the dashes and the rest of
    /// the punctuation a document is most likely to contain outside ASCII. Writing those code
    /// points raw selects the wrong glyph, so they are converted here.
    /// </remarks>
    /// <remarks>
    /// THREAD SAFETY: as for the base class, a single instance is shared process-wide between
    /// concurrently rendering documents, so this must hold no per-call mutable state. The lookup
    /// below is static and read only.
    /// </remarks>
    public class PDFWinAnsiFontWidths : PDFArrayFontWidths
    {
        /// <summary>
        /// The characters CP1252 places in 0x80 to 0x9F, in order from 0x80. Latin-1 leaves that
        /// range to control codes, so these are the only codes where the two disagree.
        /// </summary>
        /// <remarks>
        /// CP1252 leaves 0x81, 0x8D, 0x8F, 0x90 and 0x9D undefined. They are held as '\0' here and
        /// no character maps to them.
        /// </remarks>
        private static readonly char[] HighRange = new char[]
        {
            '€', '\0',     '‚', 'ƒ', '„', '…', '†', '‡',
            'ˆ', '‰', 'Š', '‹', 'Œ', '\0',     'Ž', '\0',
            '\0',     '‘', '’', '“', '”', '•', '–', '—',
            '˜', '™', 'š', '›', 'œ', '\0',     'ž', 'Ÿ'
        };

        private const int HighRangeStart = 0x80;

        /// <summary>
        /// The reverse of HighRange, so a character can be taken back to its CP1252 code.
        /// </summary>
        private static readonly Dictionary<char, char> ToHighRange = BuildReverseLookup();

        private static Dictionary<char, char> BuildReverseLookup()
        {
            var all = new Dictionary<char, char>(HighRange.Length);

            for (var i = 0; i < HighRange.Length; i++)
            {
                if (HighRange[i] != '\0')
                    all[HighRange[i]] = (char)(HighRangeStart + i);
            }

            return all;
        }

        /// <summary>
        /// Returns the character CP1252 assigns to a code, or '\0' if the code is unassigned.
        /// </summary>
        public static char GetCharacterForCode(int code)
        {
            if (code < 0 || code > 255)
                return '\0';

            if (code < HighRangeStart || code >= HighRangeStart + HighRange.Length)
                return (char)code;

            return HighRange[code - HighRangeStart];
        }

        public PDFWinAnsiFontWidths(int first, int last, IEnumerable<int> widths,
            Scryber.OpenType.SubTables.CMapEncoding encoding)
            : base(first, last, widths, encoding)
        {
        }

        public override char RegisterGlyph(char c)
        {
            char code;

            if (ToHighRange.TryGetValue(c, out code))
                c = code;

            return base.RegisterGlyph(c);
        }
    }
}
