using StackExchange.Redis;

// example server:
//  docker run -d --name redis-tls -p 6379:3000 -p 6380:4430 -e TLS_ENABLED=yes -v ./my-tls:/redis/work/tls -e "TLS_CLIENT_CNS=MyUser1 MyUser2 MyUser3" redislabs/client-libs-test:custom-21183968220-debian-amd64
//
// mimic:
// redis-cli
//   -p 6380
//   --tls
//   --cacert /home/marc/tls/my-tls/ca.crt
//   --cert /home/marc/tls/my-tls/MyUser2.crt
//   --key /home/marc/tls/my-tls/MyUser2.key client info
string certRoot = "/home/marc/tls/my-tls/";
var config = ConfigurationOptions.Parse("localhost:6380");
config.SetUserPemCertificate(// automatically enables TLS
    userCertificatePath: Path.Combine(certRoot, "MyUser2.crt"),
    userKeyPath: Path.Combine(certRoot, "MyUser2.key"));
config.TrustIssuer(Path.Combine(certRoot, "ca.crt"));

await using var conn = await ConnectionMultiplexer.ConnectAsync(config, Console.Out);

// prove we are connected as MyUser2
var info = (string?)await conn.GetDatabase().ExecuteAsync("CLIENT", "INFO");
Console.WriteLine();
Console.WriteLine(info);
