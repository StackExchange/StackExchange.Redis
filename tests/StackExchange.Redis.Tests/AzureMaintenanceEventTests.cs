using System;
using System.Net;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class AzureMaintenanceEventTests : TestBase
    {
        public AzureMaintenanceEventTests(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [InlineData("NotificationType|NodeMaintenanceStarting|StartTimeInUTC|2021-03-02T23:26:57|IsReplica|False|IPAddress||SSLPort|15001|NonSSLPort|13001", "NodeMaintenanceStarting", "2021-03-02T23:26:57", false, null, 15001, 13001)]
        [InlineData("NotificationType|NodeMaintenanceFailover|StartTimeInUTC||IsReplica|False|IPAddress||SSLPort|15001|NonSSLPort|13001", "NodeMaintenanceFailover", null, false, null, 15001, 13001)]
        [InlineData("NotificationType|NodeMaintenanceFailover|StartTimeInUTC||IsReplica|True|IPAddress||SSLPort|15001|NonSSLPort|13001", "NodeMaintenanceFailover", null, true, null, 15001, 13001)]
        [InlineData("NotificationType|NodeMaintenanceStarting|StartTimeInUTC|2021-03-02T23:26:57|IsReplica|j|IPAddress||SSLPort|char|NonSSLPort|char", "NodeMaintenanceStarting", "2021-03-02T23:26:57", false, null, 0, 0)]
        [InlineData("NotificationType|NodeMaintenanceStarting|somejunkkey|somejunkvalue|StartTimeInUTC|2021-03-02T23:26:57|IsReplica|False|IPAddress||SSLPort|15999|NonSSLPort|139991", "NodeMaintenanceStarting", "2021-03-02T23:26:57", false, null, 15999, 139991)]
        [InlineData("NotificationType|NodeMaintenanceStarting|somejunkkey|somejunkvalue|StartTimeInUTC|2021-03-02T23:26:57|IsReplica|False|IPAddress|127.0.0.1|SSLPort|15999|NonSSLPort|139991", "NodeMaintenanceStarting", "2021-03-02T23:26:57", false, "127.0.0.1", 15999, 139991)]
        [InlineData("NotificationType|", null, null, false, null, 0, 0)]
        [InlineData("NotificationType|NodeMaintenanceStarting1", "NodeMaintenanceStarting1", null, false, null, 0, 0)]
        [InlineData("1|2|3", null, null, false, null, 0, 0)]
        [InlineData("StartTimeInUTC|", null, null, false, null, 0, 0)]
        [InlineData("IsReplica|", null, null, false, null, 0, 0)]
        [InlineData("SSLPort|", null, null, false, null, 0, 0)]
        [InlineData("NonSSLPort |", null, null, false, null, 0, 0)]
        [InlineData("StartTimeInUTC|thisisthestart", null, null, false, null, 0, 0)]
        [InlineData(null, null, null, false, null, 0, 0)]
        public void TestAzureMaintenanceEventStrings(string message, string expectedEventType, string expectedStart, bool expectedIsReplica, string expectedIP, int expectedSSLPort, int expectedNonSSLPort)
        {
            DateTime? expectedStartTimeUtc = null;
            if (expectedStart != null && DateTime.TryParse(expectedStart, out DateTime startTimeUtc))
            {
                expectedStartTimeUtc = DateTime.SpecifyKind(startTimeUtc, DateTimeKind.Utc);
            }
            IPAddress.TryParse(expectedIP, out IPAddress expectedIPAddress);

            var azureMaintenance = new AzureMaintenanceEvent(message);

            Assert.Equal(expectedEventType, azureMaintenance.NotificationType);
            Assert.Equal(expectedStartTimeUtc, azureMaintenance.StartTimeUtc);
            Assert.Equal(expectedIsReplica, azureMaintenance.IsReplica);
            Assert.Equal(expectedIPAddress, azureMaintenance.IPAddress);
            Assert.Equal(expectedSSLPort, azureMaintenance.SSLPort);
            Assert.Equal(expectedNonSSLPort, azureMaintenance.NonSSLPort);
        }
    }
}
