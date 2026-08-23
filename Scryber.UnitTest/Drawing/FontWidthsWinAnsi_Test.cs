using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scryber.PDF.Resources;

namespace Scryber.Core.UnitTests.Drawing
{
    /// <summary>
    /// A simple font declares an encoding, and the bytes written into the content stream have to
    /// be codes in that encoding. The base widths class writes a character out as its own code
    /// point, which agrees with the declared /MacRomanEncoding over ASCII and disagrees with it
    /// everywhere above, so a font on that path rendered the wrong glyph for every curly quote,
    /// dash and accented letter. A font carrying a Windows Unicode subtable is now written as
    /// /WinAnsiEncoding instead, and these are the conversions that encoding needs.
    /// </summary>
    /// <remarks>
    /// The character going in is written as itself, because it is legible. What comes back out is
    /// a CP1252 code, which for this range is a character no editor will show, so the expected
    /// side is built from the code instead. The code is the whole point of the assertion anyway.
    /// </remarks>
    [TestClass()]
    public class FontWidthsWinAnsi_Test
    {
        private const int FirstChar = 0;
        private const int LastChar = 255;

        public TestContext TestContext { get; set; }

        private static PDFWinAnsiFontWidths GetWidths()
        {
            return new PDFWinAnsiFontWidths(FirstChar, LastChar,
                Enumerable.Repeat(500, (LastChar - FirstChar) + 1),
                Scryber.OpenType.SubTables.CMapEncoding.WindowsUnicode);
        }

        [TestMethod()]
        public void RegisterGlyphs_LeavesAsciiAlone()
        {
            var widths = GetWidths();

            Assert.AreEqual("The quick brown fox, 0123456789.",
                widths.RegisterGlyphs("The quick brown fox, 0123456789."),
                "ASCII is the same in every one of these encodings");
        }

        /// <summary>
        /// CP1252 and Latin-1 agree from 0xA0 up, so those characters are already their own code.
        /// </summary>
        [TestMethod()]
        public void RegisterGlyphs_LeavesTheLatin1RangeAlone()
        {
            var widths = GetWidths();

            Assert.AreEqual("é", widths.RegisterGlyphs("é"), "e acute is 0xE9 in CP1252");
            Assert.AreEqual("£", widths.RegisterGlyphs("£"), "a pound sign is 0xA3");
            Assert.AreEqual("ÿ", widths.RegisterGlyphs("ÿ"), "y diaeresis is 0xFF");
        }

        /// <summary>
        /// The range the whole fix is about. These characters sit well above 0xFF in Unicode, so
        /// before this they fell outside the widths range and came out as '?'.
        /// </summary>
        [TestMethod()]
        public void RegisterGlyphs_ConvertsTheCP1252PunctuationRange()
        {
            var widths = GetWidths();

            //The code is what the assertion is about, and the characters it produces are control
            //codes, so the expected side is built from the code rather than written as a literal.
            AssertConverts(widths, "€", 0x80, "a euro sign");
            AssertConverts(widths, "…", 0x85, "an ellipsis");
            AssertConverts(widths, "‘", 0x91, "a left single quote");
            AssertConverts(widths, "’", 0x92, "a right single quote");
            AssertConverts(widths, "“", 0x93, "a left double quote");
            AssertConverts(widths, "”", 0x94, "a right double quote");
            AssertConverts(widths, "•", 0x95, "a bullet");
            AssertConverts(widths, "–", 0x96, "an en dash");
            AssertConverts(widths, "—", 0x97, "an em dash");
            AssertConverts(widths, "™", 0x99, "a trade mark sign");
        }

        private static void AssertConverts(PDFWinAnsiFontWidths widths, string character, int code,
            string named)
        {
            Assert.AreEqual(((char)code).ToString(), widths.RegisterGlyphs(character),
                "CP1252 puts " + named + " at 0x" + code.ToString("X2"));
        }

        [TestMethod()]
        public void RegisterGlyphs_ConvertsWithinALongerRun()
        {
            var widths = GetWidths();

            //Don't "stop" - ever, written with the curly quotes and the en dash a word processor
            //would have put there.
            var run = "Don’t “stop” – ever";
            var expected = "Don" + (char)0x92 + "t " + (char)0x93 + "stop" + (char)0x94 + " " +
                (char)0x96 + " ever";

            Assert.AreEqual(expected, widths.RegisterGlyphs(run),
                "Every character in the run is converted, in place");
        }

        /// <summary>
        /// A character CP1252 cannot represent still falls back to the missing glyph.
        /// </summary>
        [TestMethod()]
        public void RegisterGlyphs_SubstitutesWhatTheEncodingCannotHold()
        {
            var widths = GetWidths();

            Assert.AreEqual("?", widths.RegisterGlyphs("中"),
                "A CJK character has no CP1252 code");
            Assert.AreEqual("a?b", widths.RegisterGlyphs("a中b"),
                "Only the character out of range is replaced");
        }

        /// <summary>
        /// The reverse of the lookup RegisterGlyph uses, which is what indexes the widths array.
        /// </summary>
        [TestMethod()]
        public void GetCharacterForCode_ReturnsTheCharacterTheEncodingAssigns()
        {
            Assert.AreEqual('A', PDFWinAnsiFontWidths.GetCharacterForCode(0x41),
                "ASCII is its own code");
            Assert.AreEqual('é', PDFWinAnsiFontWidths.GetCharacterForCode(0xE9),
                "e acute is its own code, as everything from 0xA0 up is");
            Assert.AreEqual('’', PDFWinAnsiFontWidths.GetCharacterForCode(0x92),
                "0x92 is a right single quote");
            Assert.AreEqual('€', PDFWinAnsiFontWidths.GetCharacterForCode(0x80),
                "0x80 is a euro sign");
        }

        [TestMethod()]
        public void GetCharacterForCode_ReturnsNothingForAnUnassignedCode()
        {
            //CP1252 leaves these five codes undefined.
            foreach (var code in new[] { 0x81, 0x8D, 0x8F, 0x90, 0x9D })
            {
                Assert.AreEqual('\0', PDFWinAnsiFontWidths.GetCharacterForCode(code),
                    "0x" + code.ToString("X2") + " is not assigned by CP1252");
            }

            Assert.AreEqual('\0', PDFWinAnsiFontWidths.GetCharacterForCode(256),
                "A code beyond a single byte is not assigned");
            Assert.AreEqual('\0', PDFWinAnsiFontWidths.GetCharacterForCode(-1),
                "A negative code is not assigned");
        }

        /// <summary>
        /// Every code the encoding assigns has to round trip, or the widths array and the content
        /// stream would disagree about which glyph a code selects.
        /// </summary>
        [TestMethod()]
        public void EveryAssignedCodeRoundTrips()
        {
            var widths = GetWidths();

            for (var code = FirstChar; code <= LastChar; code++)
            {
                var c = PDFWinAnsiFontWidths.GetCharacterForCode(code);

                if (c == '\0')
                    continue;

                Assert.AreEqual((char)code, widths.RegisterGlyph(c),
                    "0x" + code.ToString("X2") + " did not round trip");
            }
        }
    }
}
