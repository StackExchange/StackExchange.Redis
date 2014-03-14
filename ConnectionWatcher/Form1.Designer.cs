namespace ConnectionWatcher
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.Windows.Forms.Label label1;
            System.Windows.Forms.Label label2;
            System.Windows.Forms.Label label3;
            System.Windows.Forms.Label label4;
            System.Windows.Forms.Label label5;
            this.connect = new System.Windows.Forms.Button();
            this.console = new System.Windows.Forms.TextBox();
            this.endpoints = new System.Windows.Forms.ListBox();
            this.breakSocket = new System.Windows.Forms.Button();
            this.demandMaster = new System.Windows.Forms.Label();
            this.preferMaster = new System.Windows.Forms.Label();
            this.preferSlave = new System.Windows.Forms.Label();
            this.demandSlave = new System.Windows.Forms.Label();
            this.ticker = new System.Windows.Forms.Timer(this.components);
            this.label6 = new System.Windows.Forms.Label();
            this.redisKey = new System.Windows.Forms.Label();
            this.allowConnect = new System.Windows.Forms.CheckBox();
            this.connectionString = new System.Windows.Forms.ComboBox();
            this.clearLog = new System.Windows.Forms.Button();
            this.deslave = new System.Windows.Forms.Button();
            this.shutdown = new System.Windows.Forms.Button();
            this.deify = new System.Windows.Forms.Button();
            this.export = new System.Windows.Forms.Button();
            this.reconfigure = new System.Windows.Forms.Button();
            this.enableLog = new System.Windows.Forms.CheckBox();
            this.disconnect = new System.Windows.Forms.Button();
            this.bulkOps = new System.Windows.Forms.GroupBox();
            this.sameKey = new System.Windows.Forms.CheckBox();
            this.bulkPerThread = new System.Windows.Forms.NumericUpDown();
            this.bulkFF = new System.Windows.Forms.Button();
            this.bulkThreads = new System.Windows.Forms.NumericUpDown();
            this.bulkBatch = new System.Windows.Forms.Button();
            this.bulkSync = new System.Windows.Forms.Button();
            this.bulkAsync = new System.Windows.Forms.Button();
            this.flush = new System.Windows.Forms.Button();
            this.clearStormLog = new System.Windows.Forms.Button();
            label1 = new System.Windows.Forms.Label();
            label2 = new System.Windows.Forms.Label();
            label3 = new System.Windows.Forms.Label();
            label4 = new System.Windows.Forms.Label();
            label5 = new System.Windows.Forms.Label();
            this.bulkOps.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.bulkPerThread)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.bulkThreads)).BeginInit();
            this.SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new System.Drawing.Point(12, 18);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(91, 13);
            label1.TabIndex = 2;
            label1.Text = "Connection String";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new System.Drawing.Point(333, 48);
            label2.Name = "label2";
            label2.Size = new System.Drawing.Size(85, 13);
            label2.TabIndex = 6;
            label2.Text = "Demand Master:";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new System.Drawing.Point(333, 76);
            label3.Name = "label3";
            label3.Size = new System.Drawing.Size(73, 13);
            label3.TabIndex = 7;
            label3.Text = "Prefer Master:";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new System.Drawing.Point(333, 104);
            label4.Name = "label4";
            label4.Size = new System.Drawing.Size(68, 13);
            label4.TabIndex = 8;
            label4.Text = "Prefer Slave:";
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new System.Drawing.Point(333, 131);
            label5.Name = "label5";
            label5.Size = new System.Drawing.Size(80, 13);
            label5.TabIndex = 9;
            label5.Text = "Demand Slave:";
            // 
            // connect
            // 
            this.connect.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.connect.Location = new System.Drawing.Point(808, 13);
            this.connect.Name = "connect";
            this.connect.Size = new System.Drawing.Size(75, 23);
            this.connect.TabIndex = 1;
            this.connect.Text = "Connect";
            this.connect.UseVisualStyleBackColor = true;
            this.connect.Click += new System.EventHandler(this.connect_Clicked);
            // 
            // console
            // 
            this.console.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.console.Location = new System.Drawing.Point(12, 261);
            this.console.Multiline = true;
            this.console.Name = "console";
            this.console.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.console.Size = new System.Drawing.Size(872, 312);
            this.console.TabIndex = 3;
            // 
            // endpoints
            // 
            this.endpoints.Enabled = false;
            this.endpoints.FormattingEnabled = true;
            this.endpoints.Location = new System.Drawing.Point(15, 39);
            this.endpoints.Name = "endpoints";
            this.endpoints.SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended;
            this.endpoints.Size = new System.Drawing.Size(312, 160);
            this.endpoints.TabIndex = 4;
            // 
            // breakSocket
            // 
            this.breakSocket.Enabled = false;
            this.breakSocket.Location = new System.Drawing.Point(15, 205);
            this.breakSocket.Name = "breakSocket";
            this.breakSocket.Size = new System.Drawing.Size(120, 23);
            this.breakSocket.TabIndex = 5;
            this.breakSocket.Text = "Break Socket";
            this.breakSocket.UseVisualStyleBackColor = true;
            this.breakSocket.Click += new System.EventHandler(this.breakSocket_Click);
            // 
            // demandMaster
            // 
            this.demandMaster.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.demandMaster.Location = new System.Drawing.Point(424, 48);
            this.demandMaster.Name = "demandMaster";
            this.demandMaster.Size = new System.Drawing.Size(348, 23);
            this.demandMaster.TabIndex = 10;
            this.demandMaster.Text = "(timings etc)";
            // 
            // preferMaster
            // 
            this.preferMaster.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.preferMaster.Location = new System.Drawing.Point(424, 76);
            this.preferMaster.Name = "preferMaster";
            this.preferMaster.Size = new System.Drawing.Size(348, 23);
            this.preferMaster.TabIndex = 11;
            this.preferMaster.Text = "(timings etc)";
            // 
            // preferSlave
            // 
            this.preferSlave.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.preferSlave.Location = new System.Drawing.Point(424, 104);
            this.preferSlave.Name = "preferSlave";
            this.preferSlave.Size = new System.Drawing.Size(348, 23);
            this.preferSlave.TabIndex = 12;
            this.preferSlave.Text = "(timings etc)";
            // 
            // demandSlave
            // 
            this.demandSlave.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.demandSlave.Location = new System.Drawing.Point(424, 131);
            this.demandSlave.Name = "demandSlave";
            this.demandSlave.Size = new System.Drawing.Size(348, 23);
            this.demandSlave.TabIndex = 13;
            this.demandSlave.Text = "(timings etc)";
            // 
            // ticker
            // 
            this.ticker.Interval = 1000;
            this.ticker.Tick += new System.EventHandler(this.ticker_Tick);
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(333, 158);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(58, 13);
            this.label6.TabIndex = 14;
            this.label6.Text = "Redis Key:";
            // 
            // redisKey
            // 
            this.redisKey.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.redisKey.Location = new System.Drawing.Point(424, 158);
            this.redisKey.Name = "redisKey";
            this.redisKey.Size = new System.Drawing.Size(348, 23);
            this.redisKey.TabIndex = 15;
            this.redisKey.Text = "(timings etc)";
            // 
            // allowConnect
            // 
            this.allowConnect.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.allowConnect.AutoSize = true;
            this.allowConnect.CheckAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.allowConnect.Checked = true;
            this.allowConnect.CheckState = System.Windows.Forms.CheckState.Checked;
            this.allowConnect.Enabled = false;
            this.allowConnect.Location = new System.Drawing.Point(525, 209);
            this.allowConnect.Name = "allowConnect";
            this.allowConnect.Size = new System.Drawing.Size(107, 17);
            this.allowConnect.TabIndex = 16;
            this.allowConnect.Text = "Allow Reconnect";
            this.allowConnect.UseVisualStyleBackColor = true;
            this.allowConnect.CheckedChanged += new System.EventHandler(this.allowConnect_CheckedChanged);
            // 
            // connectionString
            // 
            this.connectionString.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.connectionString.FormattingEnabled = true;
            this.connectionString.Items.AddRange(new object[] {
            "cluster:7000,cluster:7001,cluster:7002,cluster:7003,cluster:7004,cluster:7005,res" +
                "olveDns=true",
            ".,.:6380,resolveDns=true",
            "sslredis:6379,syncTimeout=10000",
            "sslredis:6380,sslHost=anyone,syncTimeout=10000"});
            this.connectionString.Location = new System.Drawing.Point(109, 15);
            this.connectionString.Name = "connectionString";
            this.connectionString.Size = new System.Drawing.Size(612, 21);
            this.connectionString.TabIndex = 17;
            this.connectionString.Text = "localhost,localhost:6380";
            // 
            // clearLog
            // 
            this.clearLog.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.clearLog.Location = new System.Drawing.Point(764, 205);
            this.clearLog.Name = "clearLog";
            this.clearLog.Size = new System.Drawing.Size(120, 23);
            this.clearLog.TabIndex = 18;
            this.clearLog.Text = "Clear Log";
            this.clearLog.UseVisualStyleBackColor = true;
            this.clearLog.Click += new System.EventHandler(this.clearLog_Click);
            // 
            // deslave
            // 
            this.deslave.Enabled = false;
            this.deslave.Location = new System.Drawing.Point(141, 205);
            this.deslave.Name = "deslave";
            this.deslave.Size = new System.Drawing.Size(120, 23);
            this.deslave.TabIndex = 19;
            this.deslave.Text = "Deslave";
            this.deslave.UseVisualStyleBackColor = true;
            this.deslave.Click += new System.EventHandler(this.deslave_Click);
            // 
            // shutdown
            // 
            this.shutdown.Enabled = false;
            this.shutdown.Location = new System.Drawing.Point(15, 234);
            this.shutdown.Name = "shutdown";
            this.shutdown.Size = new System.Drawing.Size(120, 23);
            this.shutdown.TabIndex = 20;
            this.shutdown.Text = "Shutdown";
            this.shutdown.UseVisualStyleBackColor = true;
            this.shutdown.Click += new System.EventHandler(this.shutdown_Click);
            // 
            // deify
            // 
            this.deify.Enabled = false;
            this.deify.Location = new System.Drawing.Point(141, 234);
            this.deify.Name = "deify";
            this.deify.Size = new System.Drawing.Size(120, 23);
            this.deify.TabIndex = 21;
            this.deify.Text = "DEIFY!";
            this.deify.UseVisualStyleBackColor = true;
            this.deify.Click += new System.EventHandler(this.deify_Click);
            // 
            // export
            // 
            this.export.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.export.Enabled = false;
            this.export.Location = new System.Drawing.Point(638, 205);
            this.export.Name = "export";
            this.export.Size = new System.Drawing.Size(120, 23);
            this.export.TabIndex = 22;
            this.export.Text = "Export";
            this.export.UseVisualStyleBackColor = true;
            this.export.Click += new System.EventHandler(this.export_Click);
            // 
            // reconfigure
            // 
            this.reconfigure.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.reconfigure.Enabled = false;
            this.reconfigure.Location = new System.Drawing.Point(638, 234);
            this.reconfigure.Name = "reconfigure";
            this.reconfigure.Size = new System.Drawing.Size(120, 23);
            this.reconfigure.TabIndex = 23;
            this.reconfigure.Text = "Reconfigure";
            this.reconfigure.UseVisualStyleBackColor = true;
            this.reconfigure.Click += new System.EventHandler(this.reconfigure_Click);
            // 
            // enableLog
            // 
            this.enableLog.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.enableLog.AutoSize = true;
            this.enableLog.CheckAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.enableLog.Checked = true;
            this.enableLog.CheckState = System.Windows.Forms.CheckState.Checked;
            this.enableLog.Location = new System.Drawing.Point(552, 238);
            this.enableLog.Name = "enableLog";
            this.enableLog.Size = new System.Drawing.Size(80, 17);
            this.enableLog.TabIndex = 24;
            this.enableLog.Text = "Enable Log";
            this.enableLog.UseVisualStyleBackColor = true;
            // 
            // disconnect
            // 
            this.disconnect.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.disconnect.Enabled = false;
            this.disconnect.Location = new System.Drawing.Point(727, 13);
            this.disconnect.Name = "disconnect";
            this.disconnect.Size = new System.Drawing.Size(75, 23);
            this.disconnect.TabIndex = 25;
            this.disconnect.Text = "Disconnect";
            this.disconnect.UseVisualStyleBackColor = true;
            this.disconnect.Click += new System.EventHandler(this.disconnect_Click);
            // 
            // bulkOps
            // 
            this.bulkOps.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bulkOps.Controls.Add(this.sameKey);
            this.bulkOps.Controls.Add(this.bulkPerThread);
            this.bulkOps.Controls.Add(this.bulkFF);
            this.bulkOps.Controls.Add(this.bulkThreads);
            this.bulkOps.Controls.Add(this.bulkBatch);
            this.bulkOps.Controls.Add(this.bulkSync);
            this.bulkOps.Controls.Add(this.bulkAsync);
            this.bulkOps.Enabled = false;
            this.bulkOps.Location = new System.Drawing.Point(778, 48);
            this.bulkOps.Name = "bulkOps";
            this.bulkOps.Size = new System.Drawing.Size(105, 151);
            this.bulkOps.TabIndex = 31;
            this.bulkOps.TabStop = false;
            this.bulkOps.Text = "Bulk Ops";
            // 
            // sameKey
            // 
            this.sameKey.AutoSize = true;
            this.sameKey.Checked = true;
            this.sameKey.CheckState = System.Windows.Forms.CheckState.Checked;
            this.sameKey.Location = new System.Drawing.Point(6, 109);
            this.sameKey.Name = "sameKey";
            this.sameKey.Size = new System.Drawing.Size(74, 17);
            this.sameKey.TabIndex = 35;
            this.sameKey.Text = "Same Key";
            this.sameKey.UseVisualStyleBackColor = true;
            // 
            // bulkPerThread
            // 
            this.bulkPerThread.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bulkPerThread.Location = new System.Drawing.Point(52, 26);
            this.bulkPerThread.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
            this.bulkPerThread.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.bulkPerThread.Name = "bulkPerThread";
            this.bulkPerThread.Size = new System.Drawing.Size(47, 20);
            this.bulkPerThread.TabIndex = 34;
            this.bulkPerThread.Value = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            // 
            // bulkFF
            // 
            this.bulkFF.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bulkFF.Location = new System.Drawing.Point(6, 81);
            this.bulkFF.Name = "bulkFF";
            this.bulkFF.Size = new System.Drawing.Size(47, 23);
            this.bulkFF.TabIndex = 33;
            this.bulkFF.Text = "F+F";
            this.bulkFF.UseVisualStyleBackColor = true;
            this.bulkFF.Click += new System.EventHandler(this.bulkFF_Click);
            // 
            // bulkThreads
            // 
            this.bulkThreads.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bulkThreads.Location = new System.Drawing.Point(6, 26);
            this.bulkThreads.Maximum = new decimal(new int[] {
            50,
            0,
            0,
            0});
            this.bulkThreads.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.bulkThreads.Name = "bulkThreads";
            this.bulkThreads.Size = new System.Drawing.Size(47, 20);
            this.bulkThreads.TabIndex = 32;
            this.bulkThreads.Value = new decimal(new int[] {
            10,
            0,
            0,
            0});
            // 
            // bulkBatch
            // 
            this.bulkBatch.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bulkBatch.Location = new System.Drawing.Point(52, 81);
            this.bulkBatch.Name = "bulkBatch";
            this.bulkBatch.Size = new System.Drawing.Size(47, 23);
            this.bulkBatch.TabIndex = 31;
            this.bulkBatch.Text = "Batch";
            this.bulkBatch.UseVisualStyleBackColor = true;
            this.bulkBatch.Click += new System.EventHandler(this.bulkBatch_Click);
            // 
            // bulkSync
            // 
            this.bulkSync.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bulkSync.Location = new System.Drawing.Point(52, 52);
            this.bulkSync.Name = "bulkSync";
            this.bulkSync.Size = new System.Drawing.Size(47, 23);
            this.bulkSync.TabIndex = 30;
            this.bulkSync.Text = "Sync";
            this.bulkSync.UseVisualStyleBackColor = true;
            this.bulkSync.Click += new System.EventHandler(this.bulkSync_Click);
            // 
            // bulkAsync
            // 
            this.bulkAsync.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bulkAsync.Location = new System.Drawing.Point(6, 52);
            this.bulkAsync.Name = "bulkAsync";
            this.bulkAsync.Size = new System.Drawing.Size(47, 23);
            this.bulkAsync.TabIndex = 29;
            this.bulkAsync.Text = "Async";
            this.bulkAsync.UseVisualStyleBackColor = true;
            this.bulkAsync.Click += new System.EventHandler(this.bulkAsync_Click);
            // 
            // flush
            // 
            this.flush.Enabled = false;
            this.flush.Location = new System.Drawing.Point(267, 205);
            this.flush.Name = "flush";
            this.flush.Size = new System.Drawing.Size(120, 23);
            this.flush.TabIndex = 32;
            this.flush.Text = "Flush";
            this.flush.UseVisualStyleBackColor = true;
            this.flush.Click += new System.EventHandler(this.flush_Click);
            // 
            // clearStormLog
            // 
            this.clearStormLog.Enabled = false;
            this.clearStormLog.Location = new System.Drawing.Point(267, 234);
            this.clearStormLog.Name = "clearStormLog";
            this.clearStormLog.Size = new System.Drawing.Size(120, 23);
            this.clearStormLog.TabIndex = 33;
            this.clearStormLog.Text = "Clear Storm Log";
            this.clearStormLog.UseVisualStyleBackColor = true;
            this.clearStormLog.Click += new System.EventHandler(this.clearStormLog_Click);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(896, 585);
            this.Controls.Add(this.clearStormLog);
            this.Controls.Add(this.flush);
            this.Controls.Add(this.bulkOps);
            this.Controls.Add(this.disconnect);
            this.Controls.Add(this.enableLog);
            this.Controls.Add(this.reconfigure);
            this.Controls.Add(this.export);
            this.Controls.Add(this.deify);
            this.Controls.Add(this.shutdown);
            this.Controls.Add(this.deslave);
            this.Controls.Add(this.clearLog);
            this.Controls.Add(this.connectionString);
            this.Controls.Add(this.allowConnect);
            this.Controls.Add(this.redisKey);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.demandSlave);
            this.Controls.Add(this.preferSlave);
            this.Controls.Add(this.preferMaster);
            this.Controls.Add(this.demandMaster);
            this.Controls.Add(label5);
            this.Controls.Add(label4);
            this.Controls.Add(label3);
            this.Controls.Add(label2);
            this.Controls.Add(this.breakSocket);
            this.Controls.Add(this.endpoints);
            this.Controls.Add(this.console);
            this.Controls.Add(label1);
            this.Controls.Add(this.connect);
            this.MinimumSize = new System.Drawing.Size(540, 430);
            this.Name = "Form1";
            this.Text = "Connection Watcher";
            this.bulkOps.ResumeLayout(false);
            this.bulkOps.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.bulkPerThread)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.bulkThreads)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.Button connect;
        private System.Windows.Forms.TextBox console;
        private System.Windows.Forms.ListBox endpoints;
        private System.Windows.Forms.Button breakSocket;
        private System.Windows.Forms.Label demandMaster;
        private System.Windows.Forms.Label preferMaster;
        private System.Windows.Forms.Label preferSlave;
        private System.Windows.Forms.Label demandSlave;
        private System.Windows.Forms.Timer ticker;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label redisKey;
        private System.Windows.Forms.CheckBox allowConnect;
        private System.Windows.Forms.ComboBox connectionString;
        private System.Windows.Forms.Button clearLog;
        private System.Windows.Forms.Button deslave;
        private System.Windows.Forms.Button shutdown;
        private System.Windows.Forms.Button deify;
        private System.Windows.Forms.Button export;
        private System.Windows.Forms.Button reconfigure;
        private System.Windows.Forms.CheckBox enableLog;
        private System.Windows.Forms.Button disconnect;
        private System.Windows.Forms.NumericUpDown bulkThreads;
        private System.Windows.Forms.Button bulkBatch;
        private System.Windows.Forms.Button bulkSync;
        private System.Windows.Forms.Button bulkAsync;
        private System.Windows.Forms.GroupBox bulkOps;
        private System.Windows.Forms.Button bulkFF;
        private System.Windows.Forms.NumericUpDown bulkPerThread;
        private System.Windows.Forms.CheckBox sameKey;
        private System.Windows.Forms.Button flush;
        private System.Windows.Forms.Button clearStormLog;
    }
}

