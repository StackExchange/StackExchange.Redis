using System;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace StackExchange.Redis.Tests;

public class CertValidationTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public void CheckIssuerValidity()
    {
        // The endpoint cert is the same here
        var endpointCert = LoadCert(Path.Combine("Certificates", "device01.foo.com.pem"));

        // Trusting CA explicitly
        var callback = ConfigurationOptions.TrustIssuerCallback(Path.Combine("Certificates", "ca.foo.com.pem"));
        Assert.True(callback(this, endpointCert, null, SslPolicyErrors.None), "subtest 1a");
        Assert.True(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateChainErrors), "subtest 1b");
        Assert.False(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateNameMismatch), "subtest 1c");
        Assert.False(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateNotAvailable), "subtest 1d");
        Assert.False(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch), "subtest 1e");
        Assert.False(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNotAvailable), "subtest 1f");

        // Trusting the remote endpoint cert directly
        callback = ConfigurationOptions.TrustIssuerCallback(Path.Combine("Certificates", "device01.foo.com.pem"));
        Assert.True(callback(this, endpointCert, null, SslPolicyErrors.None), "subtest 2a");
        if (Runtime.IsMono)
        {
            // Mono doesn't support this cert usage, reports as rejection (happy for someone to work around this, but isn't high priority)
            Assert.False(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateChainErrors), "subtest 2b");
        }
        else
        {
            Assert.True(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateChainErrors), "subtest 2b");
        }

        Assert.False(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateNameMismatch), "subtest 2c");
        Assert.False(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateNotAvailable), "subtest 2d");
        Assert.False(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch), "subtest 2e");
        Assert.False(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNotAvailable), "subtest 2f");

        // Attempting to trust another CA (mismatch)
        callback = ConfigurationOptions.TrustIssuerCallback(Path.Combine("Certificates", "ca2.foo.com.pem"));
        Assert.True(callback(this, endpointCert, null, SslPolicyErrors.None), "subtest 3a");
        Assert.False(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateChainErrors), "subtest 3b");
        Assert.False(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateNameMismatch), "subtest 3c");
        Assert.False(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateNotAvailable), "subtest 3d");
        Assert.False(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch), "subtest 3e");
        Assert.False(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNotAvailable), "subtest 3f");
    }

    private static X509Certificate2 LoadCert(string certificatePath) => new X509Certificate2(File.ReadAllBytes(certificatePath));

    [Fact]
    public void CheckIssuerArgs()
    {
        Assert.ThrowsAny<Exception>(() => ConfigurationOptions.TrustIssuerCallback(""));

        var opt = new ConfigurationOptions();
        Assert.Throws<ArgumentNullException>(() => opt.TrustIssuer((X509Certificate2)null!));
    }
}
