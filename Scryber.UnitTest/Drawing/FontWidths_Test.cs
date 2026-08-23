using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scryber.Drawing;
using Scryber.Logging;
using Scryber.PDF;
using Scryber.PDF.Native;
using Scryber.PDF.Resources;

namespace Scryber.Core.UnitTests.Drawing
{
    /// <summary>
    /// A simple (non-composite) TrueType font is required to carry /FirstChar, /LastChar and
    /// /Widths. Both RenderAnsiFont implementations gate those entries on
    /// "widths.IsEmpty == false", so an inverted IsEmpty silently drops all three and Acrobat
    /// rejects the font with "contains bad /Widths". Browsers hide the fault, because PDFium falls
    /// back to the embedded font programme's own hmtx table rather than refusing the font.
    /// </summary>
    [TestClass()]
    public class FontWidths_Test
    {
        private const int FirstChar = 0;
        private const int LastChar = 255;

        //Century Gothic's own numbers, which is the font the bug was reported against.
        private const int UnitsPerEm = 2048;
        private const int AscenderFU = 2060;
        private const int DescenderFU = -451;
        private const int CapHeightFU = 1434;

        //The unit size of the glyph space a PDF font descriptor's metrics are expressed in.
        private const int PDFGlyphUnits = 1000;

        public TestContext TestContext { get; set; }

        //
        // IsEmpty
        //

        [TestMethod()]
        public void ArrayFontWidths_WithEntriesIsNotEmpty()
        {
            var widths = GetArrayWidths();

            Assert.IsFalse(widths.IsEmpty, "A widths array holding 256 entries is not empty");
        }

        [TestMethod()]
        public void ArrayFontWidths_WithoutEntriesIsEmpty()
        {
            var widths = new PDFArrayFontWidths(FirstChar, LastChar, new int[0],
                Scryber.OpenType.SubTables.CMapEncoding.MacRoman);

            Assert.IsTrue(widths.IsEmpty, "A widths array holding no entries is empty");
        }

        [TestMethod()]
        public void CompositeFontWidths_WithoutRegisteredGlyphsIsEmpty()
        {
            var widths = new PDFCompositeFontWidths(
                Scryber.OpenType.SubTables.CMapEncoding.WindowsUnicode, null, null,
                UnitsPerEm, PDFGlyphUnits);

            Assert.IsTrue(widths.IsEmpty, "No glyphs are registered, so the widths are empty");
        }

        //
        // the rendered font dictionary
        //

        /// <summary>
        /// The regression this class exists for: all three width entries have to reach the font
        /// dictionary.
        /// </summary>
        [TestMethod()]
        public void RenderAnsiFont_WritesTheWidths()
        {
            string font, descriptor;
            Render(out font, out descriptor);

            StringAssert.Contains(font, "/FirstChar " + FirstChar,
                "The font dictionary must declare the first character the widths cover");
            StringAssert.Contains(font, "/LastChar " + LastChar,
                "The font dictionary must declare the last character the widths cover");
            StringAssert.Contains(font, "/Widths ",
                "The font dictionary must reference the widths array");
        }

        /// <summary>
        /// The descriptor's /FontName has to match the font dictionary's /BaseFont. It used to be
        /// handed the page resource name (bdt_frsc1) instead.
        /// </summary>
        [TestMethod()]
        public void RenderAnsiFont_DescriptorFontNameMatchesBaseFont()
        {
            string font, descriptor;
            Render(out font, out descriptor);

            StringAssert.Contains(font, "/BaseFont /TestGothic",
                "The font dictionary names the font by its base type");
            StringAssert.Contains(descriptor, "/FontName /TestGothic",
                "The descriptor names the font by its BaseFont name, not the resource name");
            StringAssert.Contains(font, "/Name /bdt_frsc1",
                "Only the dictionary's own /Name entry carries the page resource name");
        }

        /// <summary>
        /// A hardcoded "/FontWeight 700" used to follow the conditional one, so every font claimed
        /// weight 700 and any font that was not weight 400 emitted the key twice. Removing it
        /// exposed that the descriptor's weight was never set from the font either, so the loaders
        /// now take it from the OS/2 table and a bold font says so.
        /// </summary>
        [TestMethod()]
        public void RenderAnsiFont_WritesFontWeightOnceAndOnlyWhenItIsNotRegular()
        {
            string font, regular;
            Render(out font, out regular, weight: 400);

            Assert.AreEqual(0, Occurrences(regular, "/FontWeight"),
                "A regular weight font takes the PDF default and writes no /FontWeight");

            string bold;
            Render(out font, out bold, weight: 700);

            Assert.AreEqual(1, Occurrences(bold, "/FontWeight"),
                "A bold font writes /FontWeight exactly once");
            StringAssert.Contains(bold, "/FontWeight 700", "The weight written is the font's own");
        }

        /// <summary>
        /// A font descriptor is measured in glyph space whatever design units the font programme
        /// uses. Ascent used to be multiplied by an arbitrary 0.6 and the rest written raw.
        /// </summary>
        [TestMethod()]
        public void RenderAnsiFont_ConvertsDescriptorMetricsToGlyphSpace()
        {
            string font, descriptor;
            Render(out font, out descriptor);

            StringAssert.Contains(descriptor, "/Ascent 1006",
                "2060 of 2048 design units per em is 1006 in glyph space");
            StringAssert.Contains(descriptor, "/Descent -220",
                "-451 of 2048 design units per em is -220 in glyph space");
            StringAssert.Contains(descriptor, "/CapHeight 700",
                "1434 of 2048 design units per em is 700 in glyph space");
        }

        /// <summary>
        /// A font already designed on a 1000 unit em passes through untouched.
        /// </summary>
        [TestMethod()]
        public void RenderAnsiFont_LeavesGlyphSpaceMetricsAlone()
        {
            string font, descriptor;
            Render(out font, out descriptor, unitsPerEm: PDFGlyphUnits, ascent: 750, descent: -250,
                capHeight: 700);

            StringAssert.Contains(descriptor, "/Ascent 750");
            StringAssert.Contains(descriptor, "/Descent -250");
            StringAssert.Contains(descriptor, "/CapHeight 700");
        }

        //
        // helpers
        //

        private static PDFArrayFontWidths GetArrayWidths()
        {
            return new PDFArrayFontWidths(FirstChar, LastChar,
                Enumerable.Repeat(500, (LastChar - FirstChar) + 1),
                Scryber.OpenType.SubTables.CMapEncoding.MacRoman);
        }

        private static int Occurrences(string haystack, string needle)
        {
            var count = 0;
            var at = haystack.IndexOf(needle, StringComparison.Ordinal);

            while (at >= 0)
            {
                count++;
                at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
            }

            return count;
        }

        /// <summary>
        /// None of the fonts in Mocks/Fonts take the ANSI path - they all carry a Unicode platform
        /// cmap subtable and so render as composite fonts - and a font that does take it cannot be
        /// committed here for licensing reasons. So the definition is assembled by hand.
        /// </summary>
        private class TestFontDefinition : PDFOpenTypeFontDefinition
        {
            public TestFontDefinition() : base()
            { }
        }

        /// <summary>
        /// The writer hands back a reference to the font dictionary only, and the widths and the
        /// descriptor are written as further indirect objects while it is open. Recording every
        /// object begun lets a test read those back out.
        /// </summary>
        private class RecordingWriter : PDFWriter14
        {
            public List<PDFObjectRef> Objects { get; private set; }

            public RecordingWriter(Stream stream, TraceLog log) : base(stream, log)
            {
                this.Objects = new List<PDFObjectRef>();
            }

            public override PDFObjectRef BeginObject(string name)
            {
                var oref = base.BeginObject(name);
                this.Objects.Add(oref);

                return oref;
            }

            /// <summary>
            /// Returns the rendered content of the first object holding the given text.
            /// </summary>
            public string Find(string containing)
            {
                foreach (var oref in this.Objects)
                {
                    var data = Read(oref);

                    if (data.Contains(containing))
                        return data;
                }

                Assert.Fail("No rendered object contained '" + containing + "'");

                return null;
            }

            public static string Read(PDFObjectRef oref)
            {
                return Encoding.ASCII.GetString(oref.Reference.GetObjectData());
            }
        }

        private static TestFontDefinition GetDefinition(int weight, int unitsPerEm, int ascent,
            int descent, int capHeight)
        {
            var descriptor = new PDFFontDescriptor()
            {
                FontName = "TestGothic",
                FontType = FontType.TrueType,
                FontUnitsPerEm = unitsPerEm,
                Weight = weight,
                Flags = 32,
                StemV = 80,
                ItalicAngle = 0,
                Ascent = ascent,
                Descent = descent,
                CapHeight = capHeight,
                //No FontFile: the embedded font programme is not what these assertions are about.
                FontFile = null
            };

            return new TestFontDefinition()
            {
                SubType = FontType.TrueType,
                BaseType = "TestGothic",
                Family = "Test Gothic",
                Weight = weight,
                FontEncoding = FontEncoding.MacRomanEncoding,
                IsEmbedable = true,
                Descriptor = descriptor
            };
        }

        /// <summary>
        /// Renders a simple TrueType font and returns the font dictionary and its descriptor.
        /// </summary>
        private static void Render(out string font, out string descriptor, int weight = 400,
            int unitsPerEm = UnitsPerEm, int ascent = AscenderFU, int descent = DescenderFU,
            int capHeight = CapHeightFU)
        {
            var definition = GetDefinition(weight, unitsPerEm, ascent, descent, capHeight);
            var context = new LoadContext(new ItemCollection(null),
                new DoNothingTraceLog(TraceRecordLevel.Off),
                new PerformanceMonitor(false), null, OutputFormat.PDF);

            using (var stream = new MemoryStream())
            using (var writer = new RecordingWriter(stream, context.TraceLog))
            {
                //Sets up the cross reference table the indirect objects are appended to.
                writer.OpenDocument();

                //The resource name deliberately differs from the base font name, which is what let
                //the descriptor's /FontName pick up the wrong one of the two.
                var rendered = definition.RenderAnsiFont("bdt_frsc1", GetArrayWidths(), context,
                    writer);

                font = RecordingWriter.Read(rendered);
                descriptor = writer.Find("/Type /FontDescriptor");
            }
        }
    }
}
