using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Diagnostics;
using System.Configuration.Install;
using System.Collections;
using System.Threading;

namespace Opc.Ua.Sample
{
    public class ServiceUtilities
    {
        private string serviceName = "";
        private string[] arguments; 
        private System.Reflection.Assembly parent;

        #region Properties
        public string Name
        {
            get { return serviceName; }
            set { serviceName = value; }
        }

        public string[] Arguments
        {
            get { return arguments; }
            set { arguments = value; }
        }
        #endregion

        #region Construcators
        public ServiceUtilities(System.Reflection.Assembly From, string ServiceName)
        {
            serviceName = ServiceName;
            parent = From;
        }
        #endregion

        #region Service Information
        private bool IsInstalled()
        {
            using (ServiceController controller = new ServiceController(serviceName))
            {
                try
                {
                    ServiceControllerStatus status = controller.Status;
                }
                catch
                {
                    return false;
                }
                return true;
            }
        }

        private bool IsRunning()
        {
            using (ServiceController controller = new ServiceController(serviceName))
            {
                if (!IsInstalled())
                    return false;

                return (controller.Status == ServiceControllerStatus.Running);
            }
        }

        private AssemblyInstaller GetInstaller()
        {
            AssemblyInstaller installer = new AssemblyInstaller(parent, arguments);
            installer.UseNewContext = true;
            return installer;
        }
        #endregion

        #region Service Control
        public void Install()
        {
            if (IsInstalled())
                return;

            try
            {
                using (AssemblyInstaller installer = GetInstaller())
                {
                    IDictionary state = new Hashtable();
                    try
                    {
                        installer.Install(state);
                        installer.Commit(state);
                    }
                    catch
                    {
                        try
                        {
                            installer.Rollback(state);
                        }
                        catch { }
                        throw;
                    }
                }
            }
            catch
            {
                throw;
            }
        }

        public void Uninstall()
        {
            if (!IsInstalled())
                return;

            try
            {
                using (AssemblyInstaller installer = GetInstaller())
                {
                    IDictionary state = new Hashtable();
                    try
                    {
                        installer.Uninstall(state);
                    }
                    catch
                    {
                        throw;
                    }
                }
            }
            catch
            {
                throw;
            }
        }

        public void Start()
        {
            using (ServiceController controller = new ServiceController(serviceName))
            {
                try
                {
                    if (!IsRunning())
                    {
                        controller.Start();
                        controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                    }
                }
                catch (Exception e)
                {
                    //Logger.WriteLog(Logger.EventSeverity.NOTIFY_SEVERITY_EMERG, string.Format("Start: {0} {1}", e.Source, e.Message));
                    //Logger.WriteLog(Logger.EventSeverity.NOTIFY_SEVERITY_EMERG, e.StackTrace.ToString());
                    throw;
                }
            }
        }

        public void Start(string[] args)
        {
            using (ServiceController controller = new ServiceController(serviceName))
            {
                try
                {
                    if (!IsRunning())
                    {
                        controller.Start(args);
                        Thread.Sleep(2500);
                        controller.WaitForStatus(ServiceControllerStatus.Running);
                    }
                }
                catch (Exception e)
                {
                    //Logger.WriteLog(Logger.EventSeverity.NOTIFY_SEVERITY_EMERG, string.Format("Start[arg]: {0} {1}", e.Source, e.Message));
                    //Logger.WriteLog(Logger.EventSeverity.NOTIFY_SEVERITY_EMERG, e.StackTrace.ToString());
                    throw;
                }
            }
        }

        public void Stop()
        {
            if (!IsInstalled())
                return;

            using (ServiceController controller = new ServiceController(serviceName))
            {
                try
                {
                    if (controller.Status != ServiceControllerStatus.Stopped)
                    {
                        controller.Stop();
                        controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    }
                }
                catch
                {
                    throw;
                }
            }
        }
        #endregion

        #region Override
        public override string ToString()
        {
            return serviceName;
        }
        #endregion
    }
}