using FluentAssertions;
using SqlPilot.Core.Database;
using Xunit;

namespace SqlPilot.Core.Tests
{
    public class ConnectionDescriptorTests
    {
        [Theory]
        [InlineData(@"(localdb)\MSSQLLocalDB")]
        [InlineData(@"(LocalDB)\Projects")]
        [InlineData(@"np:\\.\pipe\LOCALDB#88AE2009\tsql\query")]
        [InlineData(@"\\.\pipe\LOCALDB#88AE2009\tsql\query")]
        [InlineData(@"np:\\.\pipe\localdb#88ae2009\tsql\query")]
        public void IsLocalDbServerName_RecognisesInstanceAndPipeNames(string serverName)
        {
            ConnectionDescriptor.IsLocalDbServerName(serverName).Should().BeTrue();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("localhost")]
        [InlineData(@"MYHOST\SQLEXPRESS")]
        [InlineData("tcp:myhost,1433")]
        [InlineData("localdbserver.contoso.com")]
        [InlineData(@"np:\\myhost\pipe\sql\query")]
        [InlineData(@"np:\\myhost\pipe\LOCALDB#88AE2009\tsql\query")]
        public void IsLocalDbServerName_RejectsOtherServers(string serverName)
        {
            ConnectionDescriptor.IsLocalDbServerName(serverName).Should().BeFalse();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Data Source=myhost;Integrated Security=True")]
        [InlineData("Data Source=myhost;Encrypt=True")]
        [InlineData("Data Source=myhost;encrypt=yes")]
        [InlineData("Data Source=myhost;Encrypt=Mandatory")]
        [InlineData("Data Source=myhost;Encrypt=Strict")]
        [InlineData("Data Source=myhost;Encrypt=True;Password=\"a;Encrypt=False\"")]
        public void RequestsEncryption_UnlessTheStringTurnsItOff(string connectionString)
        {
            new ConnectionDescriptor { ConnectionString = connectionString }
                .RequestsEncryption.Should().BeTrue();
        }

        [Theory]
        [InlineData("Data Source=myhost;Encrypt=False")]
        [InlineData("Data Source=myhost;ENCRYPT=false")]
        [InlineData("Data Source=myhost;Encrypt=No")]
        [InlineData("Data Source=myhost;Encrypt=Optional")]
        public void RequestsEncryption_IsOffWhenTheStringSaysSo(string connectionString)
        {
            new ConnectionDescriptor { ConnectionString = connectionString }
                .RequestsEncryption.Should().BeFalse();
        }

        // SSMS 18 writes the keyword without spaces, SSMS 20 and 22 with them.
        [Theory]
        [InlineData("Data Source=myhost;Encrypt=True;TrustServerCertificate=True")]
        [InlineData("Data Source=myhost;Encrypt=True;Trust Server Certificate=True")]
        [InlineData("Data Source=myhost;encrypt=true;trust server certificate=yes")]
        [InlineData("Data Source=myhost;Encrypt=False;TrustServerCertificate=True")]
        public void TrustsServerCertificate_WhenTheStringSaysSo(string connectionString)
        {
            new ConnectionDescriptor { ConnectionString = connectionString }
                .TrustsServerCertificate.Should().BeTrue();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Data Source=myhost;Encrypt=True")]
        [InlineData("Data Source=myhost;Encrypt=True;TrustServerCertificate=False")]
        [InlineData("Data Source=myhost;Encrypt=True;Trust Server Certificate=False")]
        [InlineData("Data Source=myhost;Encrypt=True;Trust Server Certificate=No")]
        // Strict encryption always validates, whatever the trust keyword says.
        [InlineData("Data Source=myhost;Encrypt=Strict;Trust Server Certificate=True")]
        // A password is a value, not more keywords.
        [InlineData("Data Source=myhost;Encrypt=True;Password=\"a;TrustServerCertificate=True\"")]
        public void TrustsServerCertificate_IsOffOtherwise(string connectionString)
        {
            new ConnectionDescriptor { ConnectionString = connectionString }
                .TrustsServerCertificate.Should().BeFalse();
        }

        // The value is handed on to SSMS as it stands, so it is not normalised.
        [Theory]
        [InlineData("Data Source=myhost;Encrypt=True", "True")]
        [InlineData("Data Source=myhost;Encrypt=False", "False")]
        [InlineData("Data Source=myhost;encrypt=Strict", "Strict")]
        [InlineData("Data Source=myhost;Integrated Security=True", null)]
        [InlineData(null, null)]
        public void EncryptSetting_IsTheKeywordAsWritten(string connectionString, string expected)
        {
            new ConnectionDescriptor { ConnectionString = connectionString }
                .EncryptSetting.Should().Be(expected);
        }

        [Theory]
        [InlineData("Data Source=10.0.0.5;Encrypt=True;Host Name In Certificate=sql.contoso.com", "sql.contoso.com")]
        [InlineData("Data Source=10.0.0.5;Encrypt=True;HostNameInCertificate=sql.contoso.com", "sql.contoso.com")]
        [InlineData("Data Source=10.0.0.5;Encrypt=True", null)]
        [InlineData("Data Source=10.0.0.5;Encrypt=True;Host Name In Certificate=", null)]
        [InlineData(null, null)]
        public void HostNameInCertificate_IsReadInEitherSpelling(string connectionString, string expected)
        {
            new ConnectionDescriptor { ConnectionString = connectionString }
                .HostNameInCertificate.Should().Be(expected);
        }

        [Theory]
        [InlineData(@"Data Source=myhost;Encrypt=True;Server Certificate=C:\certs\sql.cer", @"C:\certs\sql.cer")]
        [InlineData(@"Data Source=myhost;Encrypt=True;ServerCertificate=C:\certs\sql.cer", @"C:\certs\sql.cer")]
        [InlineData("Data Source=myhost;Encrypt=True;Trust Server Certificate=True", null)]
        [InlineData(null, null)]
        public void ServerCertificate_IsReadInEitherSpelling(string connectionString, string expected)
        {
            new ConnectionDescriptor { ConnectionString = connectionString }
                .ServerCertificate.Should().Be(expected);
        }

        [Fact]
        public void AStringThatDoesNotParse_EncryptsAndDoesNotTrust()
        {
            var descriptor = new ConnectionDescriptor { ConnectionString = "Data Source=myhost;=;TrustServerCertificate=True" };

            descriptor.RequestsEncryption.Should().BeTrue();
            descriptor.TrustsServerCertificate.Should().BeFalse();
        }
    }
}
