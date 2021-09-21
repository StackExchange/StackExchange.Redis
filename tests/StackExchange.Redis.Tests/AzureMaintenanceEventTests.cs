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
        [InlineData("NotificationType|NodeMaintenanceStarting|StartTimeInUTC|2021-03-02T23:26:57|IsReplica|False|IPAddress||SSLPort|15001|NonSSLPort|13001", false)]
        [InlineData("NotificationType|NodeMaintenanceStarting|StartTimeInUTC|2021-03-02T23:26:57|IsReplica|False|IPAddress||SSLPort|15001|NonSSLPort|13001", true)]
        [InlineData("NotificationType|NodeMaintenanceFailover|StartTimeInUTC||IsReplica|False|IPAddress||SSLPort|15001|NonSSLPort|13001", true)]
        [InlineData("NotificationType|", false)]
        [InlineData("NotificationType|NodeMaintenanceStarting1", false)]
        [InlineData("1|2|3", false)]
        [InlineData("StartTimeInUTC|", false)]
        [InlineData("IsReplica|", true)]
        [InlineData("SSLPort|", true)]
        [InlineData("NonSSLPort |", true)]
        [InlineData("StartTimeInUTC|thisisthestart", false)]
        [InlineData("NotificationType|NodeMaintenanceStarting|StartTimeInUTC|2021-03-02T23:26:57|IsReplica|j|IPAddress||SSLPort|char|NonSSLPort|char", false)]
        [InlineData("NotificationType|NodeMaintenanceStarting|somejunkkey|somejunkvalue|StartTimeInUTC|2021-03-02T23:26:57|IsReplica|False|IPAddress||SSLPort|15999|NonSSLPort|139991", false)]
        [InlineData(null, false)]
        public void TestAzureMaintenanceEventStrings(string message, bool isconnectedSLB)
        {
            var azureMaintenance = new AzureMaintenanceEvent(message, isconnectedSLB);
            Assert.True(ValidateAzureMaintenanceEvent(azureMaintenance, message, isconnectedSLB));
        }

        [Fact]
        public void TestAzureMaintenanceEventStringsIgnoreCase()
        {
            var azureMaintenance = new AzureMaintenanceEvent("NotiFicationType|NodeMaintenanceStarTing|StarttimeinUTc|2021-03-02T23:26:57|Isreplica|false|Ipaddress|127.0.0.1|sslPort|12345|NonSSlPort|6789", false);
            Assert.Equal("NodeMaintenanceStarTing", azureMaintenance.NotificationType);
            Assert.Equal(DateTime.Parse("2021-03-02T23:26:57"), azureMaintenance.StartTimeUtc);
            Assert.False(azureMaintenance.IsReplica);
            Assert.Equal("127.0.0.1", azureMaintenance.IpAddress.ToString());
            Assert.Equal(12345, azureMaintenance.SSLPort);
            Assert.Equal(6789, azureMaintenance.NonSSLPort);
        }

        private bool ValidateAzureMaintenanceEvent(AzureMaintenanceEvent azureMaintenanceEvent, string message, bool isSLB)
        {
            if (azureMaintenanceEvent.RawMessage != message) return false;
            var info = message?.Split('|');
            for (int i = 0; i < info?.Length / 2; i++)
            {
                string key = null, value = null;
                if (2 * i < info.Length) { key = info[2 * i].Trim(); }
                if (2 * i + 1 < info.Length) { value = info[2 * i + 1].Trim(); }
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                {
                    switch (key)
                    {
                        case "NotificationType":
                            if (!StringComparer.Ordinal.Equals(azureMaintenanceEvent.NotificationType, value))
                                return false;
                            break;

                        case "StartTimeInUTC":
                            if (DateTime.TryParse(value, out var startTime)
                                && DateTime.Compare((DateTime)azureMaintenanceEvent.StartTimeUtc, startTime) != 0)
                                return false;
                            break;

                        case "IsReplica":
                            bool.TryParse(value, out var isReplica);
                            if (azureMaintenanceEvent.IsReplica != isReplica)
                                return false;
                            break;

                        case "IPAddress":
                            IPAddress.TryParse(value, out var ipAddress);
                            if (azureMaintenanceEvent.IpAddress.Equals(ipAddress))
                                return false;
                            break;

                        case "SSLPort":
                            Int32.TryParse(value, out var port);
                            var sslPort = isSLB ? 6380 : port;
                            if (azureMaintenanceEvent.SSLPort != sslPort)
                                return false;
                            break;

                        case "NonSSLPort":
                            Int32.TryParse(value, out var port2);
                            var nonSSLPort = isSLB ? 6379 : port2;
                            if (azureMaintenanceEvent.NonSSLPort != nonSSLPort)
                                return false;
                            break;

                        default:
                            break;
                    }
                }
            }
            return true;
        }
    }
}
