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
    }
}
