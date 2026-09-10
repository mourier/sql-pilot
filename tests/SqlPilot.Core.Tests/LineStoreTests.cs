using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using SqlPilot.Core.Database;
using SqlPilot.Core.Persistence;
using Xunit;

namespace SqlPilot.Core.Tests
{
    public class LineStoreTests : IDisposable
    {
        private readonly string _tempFile;

        public LineStoreTests()
        {
            _tempFile = Path.Combine(Path.GetTempPath(), $"sqlpilot_linestore_{Guid.NewGuid()}.txt");
        }

        [Fact]
        public void SaveAndLoadSettings_RoundTripsPlainValues()
        {
            LineStore.SaveSettings(_tempFile, new Dictionary<string, string> { ["Debounce"] = "150" });

            LineStore.LoadSettings(_tempFile)["Debounce"].Should().Be("150");
        }

        [Fact]
        public void SaveAndLoadSettings_RoundTripsKeysContainingTheSeparator()
        {
            LineStore.SaveSettings(_tempFile, new Dictionary<string, string> { ["D|server|Db|Name"] = "1" });

            LineStore.LoadSettings(_tempFile).Should().ContainKey("D|server|Db|Name");
        }

        [Fact]
        public void SaveAndLoadSettings_RoundTripsKeysAndValuesContainingBackslashes()
        {
            LineStore.SaveSettings(_tempFile, new Dictionary<string, string> { [@"SQL\INSTANCE"] = @"C:\path\to" });

            LineStore.LoadSettings(_tempFile)[@"SQL\INSTANCE"].Should().Be(@"C:\path\to");
        }

        [Fact]
        public void SaveAndLoadSettings_RoundTripsValuesContainingTheSeparator()
        {
            LineStore.SaveSettings(_tempFile, new Dictionary<string, string> { ["Key"] = "a|b|c" });

            LineStore.LoadSettings(_tempFile)["Key"].Should().Be("a|b|c");
        }

        [Fact]
        public void SaveAndLoadObjects_RoundTripsNamesContainingTheSeparator()
        {
            var original = new DatabaseObject
            {
                ServerName = @"SQL\INSTANCE",
                DatabaseName = "Sales",
                SchemaName = "dbo",
                ObjectName = "Weird|Name",
                ObjectType = DatabaseObjectType.View
            };

            LineStore.SaveObjects(_tempFile, new[] { original });

            var loaded = LineStore.LoadObjects(_tempFile).Should().ContainSingle().Subject;
            loaded.ServerName.Should().Be(@"SQL\INSTANCE");
            loaded.ObjectName.Should().Be("Weird|Name");
            loaded.ObjectType.Should().Be(DatabaseObjectType.View);
        }

        public void Dispose()
        {
            if (File.Exists(_tempFile))
                File.Delete(_tempFile);
        }
    }
}
