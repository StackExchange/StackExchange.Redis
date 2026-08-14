using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace StackExchange.Redis;

public partial class ConnectionMultiplexer
{
    private const string NoContent = "(no content)";

    /// <summary>
    /// Write the configuration of all servers to an output stream.
    /// </summary>
    /// <param name="destination">The destination stream to write the export to.</param>
    /// <param name="options">The options to use for this export.</param>
    public void ExportConfiguration(Stream destination, ExportOptions options = ExportOptions.All)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));

        // What is possible, given the command map?
        ExportOptions mask = 0;
        if (CommandMap.IsAvailable(RedisCommand.INFO)) mask |= ExportOptions.Info;
        if (CommandMap.IsAvailable(RedisCommand.CONFIG)) mask |= ExportOptions.Config;
        if (CommandMap.IsAvailable(RedisCommand.CLIENT)) mask |= ExportOptions.Client;
        if (CommandMap.IsAvailable(RedisCommand.CLUSTER)) mask |= ExportOptions.Cluster;
        options &= mask;

        using (var zip = new ZipArchive(destination, ZipArchiveMode.Create, true))
        {
            var arr = GetServerSnapshot();
            foreach (var server in arr)
            {
                const CommandFlags flags = CommandFlags.None;
                if (!server.IsConnected) continue;
                var api = GetServer(server.EndPoint);

                List<Task> tasks = new List<Task>();
                if ((options & ExportOptions.Info) != 0)
                {
                    tasks.Add(api.InfoRawAsync(flags: flags));
                }
                if ((options & ExportOptions.Config) != 0)
                {
                    tasks.Add(api.ConfigGetAsync(flags: flags));
                }
                if ((options & ExportOptions.Client) != 0)
                {
                    tasks.Add(api.ClientListAsync(flags: flags));
                }
                if ((options & ExportOptions.Cluster) != 0)
                {
                    tasks.Add(api.ClusterNodesRawAsync(flags: flags));
                    tasks.Add(api.ClusterSlotsAsync(flags: flags));
                }

                WaitAllIgnoreErrors(tasks.ToArray());

                int index = 0;
                var prefix = Format.ToString(server.EndPoint);
                if ((options & ExportOptions.Info) != 0)
                {
                    Write<string>(zip, prefix + "/info.txt", tasks[index++], WriteNormalizingLineEndings);
                }
                if ((options & ExportOptions.Config) != 0)
                {
                    Write<KeyValuePair<string, string>[]>(zip, prefix + "/config.txt", tasks[index++], (settings, writer) =>
                    {
                        foreach (var setting in settings)
                        {
                            writer.WriteLine("{0}={1}", setting.Key, setting.Value);
                        }
                    });
                }
                if ((options & ExportOptions.Client) != 0)
                {
                    Write<ClientInfo[]>(zip, prefix + "/clients.txt", tasks[index++], (clients, writer) =>
                    {
                        if (clients == null)
                        {
                            writer.WriteLine(NoContent);
                        }
                        else
                        {
                            foreach (var client in clients)
                            {
                                writer.WriteLine(client.Raw);
                            }
                        }
                    });
                }
                if ((options & ExportOptions.Cluster) != 0)
                {
                    Write<string>(zip, prefix + "/nodes.txt", tasks[index++], WriteNormalizingLineEndings);

                    // the two views can legitimately disagree about how a node is *named*, since SLOTS
                    // renders endpoints in whichever form the answering node prefers - so capture both
                    Write<ClusterSlotsResult?>(zip, prefix + "/slots.txt", tasks[index++], WriteClusterSlots);
                }
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times", Justification = "We're not double disposing.")]
    private static void Write<T>(ZipArchive zip, string name, Task task, Action<T, StreamWriter> callback)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using (var stream = entry.Open())
        using (var writer = new StreamWriter(stream))
        {
            TaskStatus status = task.Status;
            switch (status)
            {
                case TaskStatus.RanToCompletion:
                    T val = ((Task<T>)task).Result;
                    callback(val, writer);
                    break;
                case TaskStatus.Faulted:
                    writer.WriteLine(string.Join(", ", task.Exception!.InnerExceptions.Select(x => x.Message)));
                    break;
                default:
                    writer.WriteLine(status.ToString());
                    break;
            }
        }
    }

    // one line per node per range, so it greps like nodes.txt does. The endpoint is written exactly as the
    // server reported it - including the placeholders, which do not mean the same thing as each other - so
    // nothing the reply carried is lost in the rendering
    private static void WriteClusterSlots(ClusterSlotsResult? slots, StreamWriter writer)
    {
        if (slots is null)
        {
            writer.WriteLine(NoContent);
            return;
        }

        foreach (var assignment in slots.Assignments)
        {
            WriteNode(writer, assignment, "primary", assignment.Primary);
            foreach (var replica in assignment.Replicas)
            {
                WriteNode(writer, assignment, "replica", replica);
            }
        }

        static void WriteNode(StreamWriter writer, ClusterSlotAssignment assignment, string role, ClusterSlotNode node)
        {
            writer.Write("{0} {1} endpoint={2} port={3} id={4}", assignment.Slots, role, Describe(node.AnnouncedEndpoint), node.Port, node.NodeId ?? "(none)");
            foreach (var pair in node.Metadata)
            {
                writer.Write(" {0}={1}", pair.Key, Describe(pair.Value));
            }
            writer.WriteLine();
        }

        static string Describe(string? value) => value switch
        {
            null => "(null)",  // the server does not know this node's address
            "" => "(empty)",   // the node does not know its own address
            _ => value,        // may be an address, a hostname, or "?" for an unknown node
        };
    }

    private static void WriteNormalizingLineEndings(string source, StreamWriter writer)
    {
        if (source == null)
        {
            writer.WriteLine(NoContent);
        }
        else
        {
            using (var reader = new StringReader(source))
            {
                while (reader.ReadLine() is string line)
                {
                    writer.WriteLine(line); // normalize line endings
                }
            }
        }
    }
}
