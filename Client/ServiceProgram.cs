using System.ServiceProcess;
using System.Timers;
using Common.Logging;
using System;
using System.ComponentModel;
using Opc.Ua.Sample;
using Opc.Ua.Client;

namespace Opc.Ua.Sample
{
    class ServiceProgram : ServiceBase
    {

        public ServiceProgram()
        {
            InitializeComponent();
        }

        //private BackgroundWorker client_worker = null;
        protected override void OnStart (string[] args)
        {
            Program.Client_main();
            Program.WriteLog("[info] Start");

        }

        void client_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;
            try
            {
                //Program.Client_main();
            }
            catch (Exception err )
            {
                Program.WriteLog("[error] " + err.Message + err.StackTrace);
            }

            if (worker.CancellationPending)
                return;

        }

        public void Start(string[] args)
        {
            this.OnStart(args);
        }

        protected override void OnStop()
        {
            Program.Disconnect();
            Program.WriteLog("[info] Stop");

        }

        public void Stop()
        {
            this.OnStop();
        }

        private void InitializeComponent()
        {
            this.ServiceName = "OpcuaClient";

        }
    }
}
