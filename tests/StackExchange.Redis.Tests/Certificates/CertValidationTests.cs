using System;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

public class CertValidationTests : TestBase
{
    public CertValidationTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void CheckIssuerValidity()
    {
        // The endpoint cert is the same here
        var endpointCert = LoadCert(Path.Combine("Certificates", "device01.foo.com.pem"));

        // Trusting CA explicitly
        var callback = ConfigurationOptions.TrustIssuerCallback(Path.Combine("Certificates", "ca.foo.com.pem"));
        Assert.True(callback(this, endpointCert, null, SslPolicyErrors.None));
        Assert.True(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateNameMismatch));
        Assert.False(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateNotAvailable));

        // Trusting the remote endpoint cert directly
        callback = ConfigurationOptions.TrustIssuerCallback(Path.Combine("Certificates", "device01.foo.com.pem"));
        Assert.True(callback(this, endpointCert, null, SslPolicyErrors.None));
        Assert.True(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateNameMismatch));
        Assert.False(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateNotAvailable));

        // Attempting to trust another CA (mismatch)
        callback = ConfigurationOptions.TrustIssuerCallback(Path.Combine("Certificates", "ca2.foo.com.pem"));
        Assert.True(callback(this, endpointCert, null, SslPolicyErrors.None));
        Assert.False(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateNameMismatch));
        Assert.False(callback(this, endpointCert, null, SslPolicyErrors.RemoteCertificateNotAvailable));
    }

    private static X509Certificate2 LoadCert(string certificatePath) =>
        new X509Certificate2(StripPEM(File.ReadAllBytes(certificatePath)));
    private static byte[] StripPEM(byte[] pemData) =>
        Convert.FromBase64String(Regex.Replace(Encoding.UTF8.GetString(pemData), "(-+BEGIN CERTIFICATE-+)|(-+END CERTIFICATE-+)", "").Trim());

    [Fact]
    public void CheckIssuerArgs()
    {
        Assert.ThrowsAny<Exception>(() => ConfigurationOptions.TrustIssuerCallback(""));

        var opt = new ConfigurationOptions();
        Assert.Throws<ArgumentNullException>(() => opt.TrustIssuer((X509Certificate2)null!));
    }
}
