using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using StackExchange.Redis;

namespace ConnectionWatcher
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            foreach(var file in Directory.GetFiles(Environment.CurrentDirectory, "Interactive_*.txt"))
            {
                File.Delete(file);
            }
            foreach (var file in Directory.GetFiles(Environment.CurrentDirectory, "Subscriber_*.txt"))
            {
                File.Delete(file);
            }
            InitializeComponent();

            demandMaster.Text = preferMaster.Text = demandSlave.Text = preferSlave.Text = redisKey.Text = "";

#if !DEBUG
            breakSocket.Text = allowConnect.Text = "#if DEBUG";
#endif
#if LOGOUTPUT
            ConnectionMultiplexer.EchoPath = Environment.CurrentDirectory;
#endif
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            SetEnabled(false);
        }
        private void disconnect_Click(object sender, EventArgs e)
        {
            SetEnabled(false);
        }
        private void SetEnabled(bool running)
        {
            connect.Enabled = connectionString.Enabled = !running;
            shutdown.Enabled = endpoints.Enabled = deslave.Enabled = deify.Enabled = export.Enabled = reconfigure.Enabled =
                disconnect.Enabled = bulkOps.Enabled = flush.Enabled = clearStormLog.Enabled = running;

#if DEBUG
            breakSocket.Enabled = allowConnect.Enabled = running;
#endif
            if (running) ticker.Start();
            else ticker.Stop();

            if (muxer != null)
            {
                muxer.Dispose();
                muxer = null;
            }
            if(running)
            {
                console.Text = "";
                var options = ConfigurationOptions.Parse(connectionString.Text);
                options.AllowAdmin = true;
                options.AbortOnConnectFail = false;
                options.CertificateValidation += (sender, cert, chain, errors) =>
                {
                    Log("cert issued to: " + cert.Subject);
                    return true; // fingers in ears, pretend we don't know this is wrong
                };

                using (var logger = new StringWriter())
                {
                    muxer = ConnectionMultiplexer.Connect(options, logger);
                    Log(logger.ToString());
                }
                endpoints.Items.Clear();
                endpoints.Items.AddRange(Array.ConvertAll(
                    muxer.GetEndPoints(), ep => new EndPointPair(muxer.GetServer(ep))));

                muxer.ConnectionFailed += Muxer_ConnectionFailed;
                muxer.ConnectionRestored += Muxer_ConnectionRestored;
                muxer.ErrorMessage += Muxer_ErrorMessage;
                muxer.ConfigurationChanged += Muxer_ConfigurationChanged;
            }
        }
        private void connect_Clicked(object sender, EventArgs e)
        {
            SetEnabled(true);
        }

        private void Muxer_ConfigurationChanged(object sender, EndPointEventArgs e)
        {
            Log("Configuration changed: " + e.EndPoint);
        }

        private void Muxer_ErrorMessage(object sender, RedisErrorEventArgs e)
        {
            Log(e.EndPoint + ": " + e.Message);
        }

        private void Muxer_ConnectionRestored(object sender, ConnectionFailedEventArgs e)
        {
            Log("Endpoint restored: " + e.EndPoint);
        }
        private void Log(string message)
        {
            if(InvokeRequired)
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (enableLog.Checked)
                        console.Text = message + Environment.NewLine + console.Text;
                });
            } else
            {
                if(enableLog.Checked)
                    console.Text = message + Environment.NewLine + console.Text;
            }
        }

        class EndPointPair
        {
            public EndPointPair(IServer server)
            {
                this.server = server;
            }
            private readonly IServer server;
            public EndPoint EndPoint { get { return server == null ? null : server.EndPoint; } }
            private string state;

            public override string ToString()
            {
                try
                {
                    string spacer;
                    switch (((uint)Thread.VolatileRead(ref loop)) % 4)
                    {
                        case 0: spacer = @" - "; break;
                        case 1: spacer = @" \ "; break;
                        case 2: spacer = @" | "; break;
                        case 3: spacer = @" / "; break;
                        default: spacer = " ! "; break;
                    }
                    return (server.IsSlave ? "S " : "M ") + EndPointCollection.ToString(EndPoint) + spacer + OpCount + ": " + state;
                }
                catch (Exception ex)
                {
                    return ex.Message;
                }

            }
            public long OpCount { get;set; }
            int loop;
            internal void SetState(string msg)
            {
                state = msg;
                Interlocked.Increment(ref loop);
                Interlocked.Exchange(ref concern, 0);
            }
            int concern;
            internal bool Worried()
            {
                return Interlocked.Increment(ref concern) >= 5;
            }
        }
        private void Muxer_ConnectionFailed(object sender, ConnectionFailedEventArgs e)
        {
            Log("Endpoint failed: " + e.EndPoint + ", " + e.FailureType + (e.Exception == null ? "" : (", " + e.Exception.Message)));
        }

        private void ticker_Tick(object sender, EventArgs e)
        {
            RefreshListBox();
            if (muxer == null) return;
            if(endpoints.SelectedIndices.Count == 1)
            {
                var ep = ((EndPointPair)endpoints.SelectedItem).EndPoint;
                var server = muxer.GetServer(ep);
                Text = server.ServerType + " " + server.Version + " " + (server.IsSlave ? "slave" : "master") + "; " + server.GetCounters().ToString();
            }
            foreach (var pair in endpoints.Items.OfType<EndPointPair>())
            {
                
                if(pair.Worried())
                {
                    Log("No response from " + pair.EndPoint);
                }
                var server = muxer.GetServer(pair.EndPoint, pair);
                
                var q = server.GetCounters();
                pair.OpCount = q.Interactive.OperationCount;
                if (q.TotalOutstanding > 5)
                {
                    Log(q.ToString());
//#if DEBUG
//                    if(q.Interactive.PendingUnsentItems != 0 || q.Subscription.PendingUnsentItems != 0)
//                    {
//                        Log(((IRedisServerDebug)server).ListPending(100));
//                    }
//#endif
                }
                var ping = server.PingAsync().ContinueWith(UpdateEndPoint);
            }

            
            string s = Guid.NewGuid().ToString();
            redisKey.Text = s;
            RedisKey key = s;
            var db = muxer.GetDatabase(asyncState: Stopwatch.StartNew());
            db.IdentifyEndpointAsync(key, CommandFlags.DemandMaster).ContinueWith(DemandMaster);
            db.IdentifyEndpointAsync(key, CommandFlags.PreferMaster).ContinueWith(PreferMaster);
            db.IdentifyEndpointAsync(key, CommandFlags.PreferSlave).ContinueWith(PreferSlave);
            db.IdentifyEndpointAsync(key, CommandFlags.DemandSlave).ContinueWith(DemandSlave);           
            
            // ThreadPool.QueueUserWorkItem(AccessSync);
            
        }
        private void AccessSync(object state)
        {
            var ep = muxer.GetEndPoints();
            for (int i = 0; i < ep.Length; i++)
            {
                try
                { muxer.GetServer(ep[i]).Ping(); }
                catch (Exception ex)
                {
                    Log(ep[i] + ":" + ex.Message);
                }
            }
        }

        private void UpdateEndPoint(Task<TimeSpan> task)
        {
            
            string msg = ExtractMessage(task);
            var pair = (EndPointPair)task.AsyncState;
            if (pair != null)
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    pair.SetState(msg);
                    RefreshListBox();
                });
            }
            
        }

        void RefreshListBox()
        {
            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)RefreshListBox);
            }
            else
            {
                // hacky "redraw your damned text", via http://stackoverflow.com/a/4631419/23354
                // unfortunately, while this works, it breaks the selected items
                bool[] selected = new bool[endpoints.Items.Count];
                for (int i = 0; i < selected.Length; i++)
                {
                    selected[i] = endpoints.GetSelected(i);
                }
                typeof(ListBox).InvokeMember("RefreshItems", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.InvokeMethod, null, endpoints, new object[] { });
                for (int i = 0; i < selected.Length; i++)
                {
                    endpoints.SetSelected(i, selected[i]);
                }
            }
        }

        static string ExtractMessage<T>(Task<T> task)
        {
            if (task == null) return "";
            try
            {
                var status = task.Status;
                switch (status)
                {
                    case TaskStatus.RanToCompletion:
                        object result = task.Result;
                        if(result is TimeSpan)
                        {
                            return ((TimeSpan)result).TotalMilliseconds.ToString("###,##0.00ms");
                        }
                        if(result is EndPoint)
                        {
                            return EndPointCollection.ToString((EndPoint)result);
                        }
                        return Convert.ToString(result);
                    case TaskStatus.Faulted:
                        return string.Join(", ", task.Exception.InnerExceptions.Select(x => x.Message));
                    default:
                        task.ContinueWith(x =>
                        {
                            try
                            { // mark observed
                                GC.KeepAlive(x.Exception);
                            }
                            catch
                            { }
                        }, continuationOptions: TaskContinuationOptions.OnlyOnFaulted);
                        return status.ToString();
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }

        }

        private void DemandMaster(Task<EndPoint> task)
        {
            Write(demandMaster, task);
        }
        private void DemandSlave(Task<EndPoint> task)
        {
            Write(demandSlave, task);
        }
        private void PreferMaster(Task<EndPoint> task)
        {
            Write(preferMaster, task);
        }
        private void PreferSlave(Task<EndPoint> task)
        {
            Write(preferSlave, task);
        }

        private void Write(Label output, Task<EndPoint> task)
        {
            var watch = (Stopwatch)task.AsyncState;
            var elapsed = watch.Elapsed;
            output.BeginInvoke((MethodInvoker)delegate
            {
                string msg = ExtractMessage(task);
                output.Text = elapsed.TotalMilliseconds.ToString("###,##0.00ms") + ": " + msg;
            });
        }

        ConnectionMultiplexer muxer;

        private void breakSocket_Click(object sender, EventArgs e)
        {
#if DEBUG
            foreach (var pair in endpoints.SelectedItems.OfType<EndPointPair>())
            {
                try
                {
                    muxer.GetServer(pair.EndPoint).SimulateConnectionFailure();
                } catch(Exception ex)
                {
                    Log(ex.Message);
                }
            }
#endif
        }

        private void allowConnect_CheckedChanged(object sender, EventArgs e)
        {
#if DEBUG
            muxer.AllowConnect = allowConnect.Checked;
#endif
        }

        private void clearLog_Click(object sender, EventArgs e)
        {
            console.Text = "";
        }



        private void deslave_Click(object sender, EventArgs e)
        {
            foreach (var pair in endpoints.SelectedItems.OfType<EndPointPair>())
            {
                var sw = new StringWriter();
                try
                {
                    muxer.GetServer(pair.EndPoint).MakeMaster(ReplicationChangeOptions.None, sw);
                } catch(Exception ex)
                {
                    Log(ex.Message);
                }
                Log(sw.ToString());
            }
        }

        private void shutdown_Click(object sender, EventArgs e)
        {
            foreach (var pair in endpoints.SelectedItems.OfType<EndPointPair>())
            {
                try
                {
                    muxer.GetServer(pair.EndPoint).Shutdown();
                } catch(Exception ex)
                {
                    Log(ex.Message);
                }
            }
        }
        private void flush_Click(object sender, EventArgs e)
        {
            foreach (var pair in endpoints.SelectedItems.OfType<EndPointPair>())
            {
                try
                {
                    muxer.GetServer(pair.EndPoint).FlushDatabase();
                }
                catch (Exception ex)
                {
                    Log(ex.Message);
                }
            }
        }
        private void clearStormLog_Click(object sender, EventArgs e)
        {
            try
            {
                muxer.ResetStormLog();
            }
            catch (Exception ex)
            {
                Log(ex.Message);
            }
        }

        private void deify_Click(object sender, EventArgs e)
        {
            var items = endpoints.SelectedItems.OfType<EndPointPair>().ToList();
            if (items.Count != 1) return;

            var sw = new StringWriter();
            try
            {
                muxer.GetServer(items[0].EndPoint).MakeMaster(ReplicationChangeOptions.SetTiebreaker | ReplicationChangeOptions.Broadcast | ReplicationChangeOptions.EnslaveSubordinates, sw);
            } catch(Exception ex)
            {
                Log(ex.Message);
            }
            Log(sw.ToString());
        }

        private void export_Click(object sender, EventArgs e)
        {
            try
            {
                using (var dlg = new SaveFileDialog())
                {
                    dlg.Filter = "Zip Files | *.zip";
                    dlg.DefaultExt = "zip";
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        string path = dlg.FileName;
                        if(!string.IsNullOrEmpty(path))
                        {
                            using(var file = File.Create(path))
                            {
                                muxer.ExportConfiguration(file);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log(ex.Message);
            }

        }

        private void reconfigure_Click(object sender, EventArgs e)
        {
            using(var writer = new StringWriter())
            {
                try
                {
                    muxer.Configure(writer);
                } catch(Exception ex)
                {
                    Log(ex.Message);
                }
                Log(writer.ToString());
            }
        }

        private void bulkSync_Click(object sender, EventArgs e)
        {
            RunConcurrent(0.1M, (db, count, key) => {
                while(count-- > 0) db.StringIncrement(key());
                return null;
                }, "Bulk sync");
        }

        private void bulkFF_Click(object sender, EventArgs e)
        {
            RunConcurrent(1, (db, count, key) => {
                while (count-- > 0) db.StringIncrement(key(), flags: CommandFlags.FireAndForget);
                return null;
                }, "Bulk F+F");
        }
        private void bulkAsync_Click(object sender, EventArgs e)
        {
            RunConcurrent(1, (db, count, key) => {
                while (count-- > 1) db.StringIncrementAsync(key(), flags: CommandFlags.FireAndForget);
                return db.StringIncrementAsync(key());
            }, "Bulk async");
        }

        private void bulkBatch_Click(object sender, EventArgs e)
        {
            RunConcurrent(1, (db, count, key) =>
            {
                var batch = db.CreateBatch();
                while (count-- > 1) batch.StringIncrementAsync(key(), flags: CommandFlags.FireAndForget);
                Task last = db.StringIncrementAsync(key());
                batch.Execute();
                return last;
            }, "Bulk batch");
        }

        private void RunConcurrent(decimal factor, Func<IDatabase, int, Func<RedisKey>, Task> work, [CallerMemberName] string caller = null, Action<Func<RedisKey>> whenDone = null, int timeout = 10000)
        {
            int threads = (int)bulkThreads.Value;
            int perThread = (int)(bulkPerThread.Value * factor);
            ThreadPool.QueueUserWorkItem(delegate
            {
                Func<RedisKey> keyFunc;
                if(sameKey.Checked)
                {
                    RedisKey key = Guid.NewGuid().ToString();
                    keyFunc = () => key;
                } else
                {
                    keyFunc = () => Guid.NewGuid().ToString();
                }
                    
                Task last = null;
                var db = muxer.GetDatabase();
                db.KeyDelete(keyFunc(), CommandFlags.FireAndForget);
                try
                {
                    if (work == null) return;
                    if (threads < 1) return;
                    Stopwatch watch = null;
                    ManualResetEvent allDone = new ManualResetEvent(false);
                    object token = new object();
                    int active = 0;
                    ThreadStart callback = delegate
                    {
                        lock (token)
                        {
                            int nowActive = Interlocked.Increment(ref active);
                            if (nowActive == threads)
                            {
                                watch = Stopwatch.StartNew();
                                Monitor.PulseAll(token);
                            }
                            else
                            {
                                Monitor.Wait(token);
                            }
                        }
                        var result = work(db, perThread, keyFunc);
                        if (result != null) Interlocked.Exchange(ref last, result);
                        if (Interlocked.Decrement(ref active) == 0)
                        {
                            allDone.Set();
                        }
                    };

                    Thread[] threadArr = new Thread[threads];
                    for (int i = 0; i < threads; i++)
                    {
                        var thd = new Thread(callback);
                        thd.Name = caller;
                        threadArr[i] = thd;
                        thd.Start();
                    }
                    if (allDone.WaitOne(timeout))
                    {
                        if (whenDone != null) whenDone(keyFunc);
                        var result = db.StringGet(keyFunc());
                        watch.Stop();
                        var finalTask = Interlocked.Exchange(ref last, null);
                        if (finalTask != null) Log("Last task is: " + finalTask.Status.ToString());
                        Log(string.Format("{0}, {1} per-thread on {2} threads: {3:###,###,##0.##}ms, {4:###,###,##0}ops/s (result: {5})",
                            caller, perThread, threads,
                            watch.Elapsed.TotalMilliseconds,
                            (int)((perThread * threads) / watch.Elapsed.TotalSeconds),
                            result));
                    }
                    else
                    {
                        for (int i = 0; i < threads; i++)
                        {
                            var thd = threadArr[i];
                            if (thd.IsAlive) thd.Abort();
                        }
                        Log(string.Format("{0} timed out", caller));
                    }
                }
                catch (Exception ex)
                {
                    Log(string.Format("{0} failed: {1}", caller, ex.Message));
                }
                finally
                {
                    db.KeyDelete(keyFunc(), CommandFlags.FireAndForget);
                }
            });
        }


    }
}
