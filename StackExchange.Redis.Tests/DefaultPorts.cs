﻿using System.Linq;
using System.Net;
using Xunit;

namespace StackExchange.Redis.Tests
{
    public class DefaultPorts
    {
        [Theory]
        [InlineData("foo", 6379)]
        [InlineData("foo:6379", 6379)]
        [InlineData("foo:6380", 6380)]
        [InlineData("foo,ssl=false", 6379)]
        [InlineData("foo:6379,ssl=false", 6379)]
        [InlineData("foo:6380,ssl=false", 6380)]

        [InlineData("foo,ssl=true", 6380)]
        [InlineData("foo:6379,ssl=true", 6379)]
        [InlineData("foo:6380,ssl=true", 6380)]
        [InlineData("foo:6381,ssl=true", 6381)]
        public void ConfigStringRoundTripWithDefaultPorts(string config, int expectedPort)
        {
            var options = ConfigurationOptions.Parse(config);
            string backAgain = options.ToString();
            Assert.Equal(config, backAgain.Replace("=True", "=true").Replace("=False", "=false"));

            options.SetDefaultPorts(); // normally it is the multiplexer that calls this, not us
            Assert.Equal(expectedPort, ((DnsEndPoint)options.EndPoints.Single()).Port);
        }

        [Theory]
        [InlineData("foo", 0, false, 6379)]
        [InlineData("foo", 6379, false, 6379)]
        [InlineData("foo", 6380, false, 6380)]

        [InlineData("foo", 0, true, 6380)]
        [InlineData("foo", 6379, true, 6379)]
        [InlineData("foo", 6380, true, 6380)]
        [InlineData("foo", 6381, true, 6381)]

        public void ConfigManualWithDefaultPorts(string host, int port, bool useSsl, int expectedPort)
        {
            var options = new ConfigurationOptions();
            if (port == 0)
            {
                options.EndPoints.Add(host);
            }
            else
            {
                options.EndPoints.Add(host, port);
            }
            if (useSsl) options.Ssl = true;

            options.SetDefaultPorts(); // normally it is the multiplexer that calls this, not us
            Assert.Equal(expectedPort, ((DnsEndPoint)options.EndPoints.Single()).Port);
        }
    }
}