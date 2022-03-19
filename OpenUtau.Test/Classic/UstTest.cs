﻿using System.IO;
using System.Reflection;
using System.Text;
using OpenUtau.Core.Ustx;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using Xunit;
using Xunit.Abstractions;

namespace OpenUtau.Classic {
    public class UstTest {
        readonly ITestOutputHelper output;

        public UstTest(ITestOutputHelper output) {
            this.output = output;
        }

        [Fact]
        public void UstLoadingTest() {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            dir = Path.Join(dir, "Usts");
            var encoding = Encoding.GetEncoding("shift_jis");
            var readerOptions = new ReaderOptions {
                ArchiveEncoding = new ArchiveEncoding(encoding, encoding)
            };
            foreach (var file in Directory.GetFiles(dir, "*.zip")) {
                using (var archive = ArchiveFactory.Open(file, readerOptions)) {
                    foreach (var entry in archive.Entries) {
                        if (Path.GetExtension(entry.Key) != ".ust") {
                            continue;
                        }
                        output.WriteLine(Path.GetFileName(file) + ":" + entry.Key);
                        using (var reader = new StreamReader(entry.OpenEntryStream(), encoding)) {
                            var project = Ust.Load(reader, entry.Key);
                            project.AfterLoad();
                            project.ValidateFull();
                            Assert.Single(project.parts);
                            var part = project.parts[0] as UVoicePart;
                            Assert.True(part.notes.Count > 0);
                        }
                    }
                }
            }
        }
    }
}
