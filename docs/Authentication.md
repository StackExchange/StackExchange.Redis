Authentication
===

There are multiple ways of connecting to a Redis server, depending on the authentication model. The simplest
(but least secure) approach is to use the `default` user, with no authentication, and no transport security.
This is as simple as:

``` csharp
var muxer = ConnectionMultiplexer.Connect("myserver"); // or myserver:1241 to use a custom port
```

This approach is often used for local transient servers - it is simple, but insecure. But from there,
we can get more complex!

TLS
===

If your server has TLS enabled, SE.Redis can be instructed to use it. In some cases (AMR, etc), the
library will recognize the endpoint address, meaning: *you do not need to do anything*. To
*manually* enable TLS, the `ssl` token can be used:

``` csharp
var muxer = ConnectionMultiplexer.Connect("myserver,ssl=true");
```

This will work fine if the server is using a server-certificate that is already trusted by the local
machine. If this is *not* the case, we need to tell the library about the server. This requires
the `ConfigurationOptions` type:

``` csharp
var options = ConfigurationOptions.Parse("myserver,ssl=true");
// or: var options = new ConfigurationOptions { Endpoints = { "myserver" }, Ssl = true };
// TODO configure
var muxer = ConnectionMultiplexer.Connect(options);
```

If we have a local *issuer* public certificate (commonly `ca.crt`), we can use:

``` csharp
options.TrustIssuer(caPath);
```

Alternatively, in advanced scenarios: to provide your own custom server validation, the `options.CertificateValidation` callback
can be used; this uses the normal [`RemoteCertificateValidationCallback`](https://learn.microsoft.com/dotnet/api/system.net.security.remotecertificatevalidationcallback)
API.

Usernames and Passwords
===

Usernames and passwords can be specified with the `user` and `password` tokens, respectively:

``` csharp
var muxer = ConnectionMultiplexer.Connect("myserver,ssl=true,user=myuser,password=mypassword");
```

If no `user` is provided, the `default` user is assumed. In some cases, an authentication-token can be
used in place of a classic password.

Client certificates
===

If the server is configured to require a client certificate, this can be supplied in multiple ways.
If you have a local public / private key pair (such as `MyUser2.crt` and `MyUser2.key`), the
`options.SetUserPemCertificate(...)` method can be used:

``` csharp
config.SetUserPemCertificate(
    userCertificatePath: userCrtPath,
    userKeyPath: userKeyPath
);
```

If you have a single `pfx` file that contains the public / private pair, the `options.SetUserPfxCertificate(...)`
method can be used:

``` csharp
config.SetUserPfxCertificate(
    userCertificatePath: userCrtPath,
    password: filePassword // optional
);
```

Alternatively, in advanced scenarios: to provide your own custom client-certificate lookup, the `options.CertificateSelection` callback
can be used; this uses the normal
[`LocalCertificateSelectionCallback`](https://learn.microsoft.com/dotnet/api/system.net.security.remotecertificatevalidationcallback)
API.

User certificates with implicit user authentication
===

Historically, the client certificate only provided access to the server, but as the `default` user. From 8.6,
the server can be configured to use client certificates to provide user identity. This replaces the
usage of passwords, and requires:

- An 8.6+ server, configured to use TLS with client certificates mapped - typically using the `CN` of the certificate as the user.
- A matching `ACL` user account configured on the server, that is enabled (`on`) - i.e. the `ACL LIST` command should
  display something like `user MyUser2 on sanitize-payload ~* &* +@all` (the details will vary depending on the user permissions).
- At the client: access to the client certificate pair.

For example:

``` csharp
string certRoot = // some path to a folder with ca.crt, MyUser2.crt and MyUser2.key

var options = ConfigurationOptions.Parse("myserver:6380");
options.SetUserPemCertificate(// automatically enables TLS
    userCertificatePath: Path.Combine(certRoot, "MyUser2.crt"),
    userKeyPath: Path.Combine(certRoot, "MyUser2.key"));
options.TrustIssuer(Path.Combine(certRoot, "ca.crt"));
await using var conn = await ConnectionMultiplexer.ConnectAsync(options);

// prove we are connected as MyUser2
var user = (string?)await conn.GetDatabase().ExecuteAsync("acl", "whoami");
Console.WriteLine(user); // writes "MyUser2"
```

More info
===

For more information:

- [Redis Security](https://redis.io/docs/latest/operate/oss_and_stack/management/security/)
  - [ACL](https://redis.io/docs/latest/operate/oss_and_stack/management/security/acl/)
  - [TLS](https://redis.io/docs/latest/operate/oss_and_stack/management/security/encryption/)
