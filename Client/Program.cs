using System;
using System.Windows.Forms;
using Opc.Ua.Configuration;
using Opc.Ua.Client.Controls;
using Opc.Ua.Bindings.Custom;
using Opc.Ua.Sample.Controls;
using Opc.Ua.Client;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Linq;
using ZeroMQ;
using System.Threading;
using System.ServiceProcess;
using JSON_Library;
using System.Configuration.Install;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Opc.Ua.Sample
{
    static class Program
    {

        #region Private Fields
        private static List<Session> m_session = new List<Session>();
        private static DataValue m_value;
        private static TreeView node_tree = new TreeView();
        private static MonitoredItemNotificationEventHandler m_MonitoredItem_Notification;
        private static string ini_filename = "ConnectConfig.ini";       
        static List<string> ini_nodeid = new List<string>();
        static jsonlib.INI_contex_t INI_data = new jsonlib.INI_contex_t();
        private static JObject g_Capability;
        private static int m_reconnectPeriod = 5;
        private static SessionReconnectHandler m_reconnectHandler;
        //private static EventHandler m_ReconnectStarting;

        [DllImport("kernel32.dll", EntryPoint = "GetPrivateProfileString", SetLastError = true)]
        private static extern uint GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);
        #endregion

        /// <summary>
        /// The main entry point for the application.
        /// </summary>      
        static void Main(string[] args)
        {                       
            try
            {
#if DEBUG
                ServiceProgram service = new ServiceProgram(); //*************************************
                service.Start(null);                           //
                Console.WriteLine("Press enter to exit");      //debug mode (console application)
                Console.ReadLine();                            //
                service.Stop();                                //*************************************
#else

                ServiceBase[] ServicesToRun;
                ServicesToRun = new ServiceBase[]
                {
                   new ServiceProgram()
                };
                ServiceBase.Run(ServicesToRun);
#endif

                //ServiceBase ServicesToRun = service as ServiceBase;
                //if (args.Length == 1)
                //{
                //    ServiceUtilities OPCservice = new ServiceUtilities(typeof(Program).Assembly, ServicesToRun.ServiceName);
                //    List<string> newargs = new List<string>(args);
                //    switch(args[0].ToString().ToLower())
                //    {
                //         case "-install":
                //            OPCservice.Install();
                //            //ManagedInstallerClass.InstallHelper(new string[] { Assembly.GetExecutingAssembly().Location });
                //            newargs.Remove(args[0]);

                //            //ServiceBase.Run(ServicesToRun);
                //            OPCservice.Start(newargs.ToArray());
                //            break;

                //         case "-uninstall":
                //            OPCservice.Uninstall();
                //            break;

                //         case "-cmd":
                //           service.Start(null);
                //           Console.WriteLine("Service running");
                //           Console.WriteLine("Press enter to exit");
                //           Console.ReadLine();
                //           service.Stop();
                //           break;
                //    }                             
                //}
                //else
                //{
                //    Console.WriteLine("Error parameter");
                //}

            }
            catch (Exception e)
            {
                WriteLog("[error] " + e.Message + e.StackTrace);
                Console.WriteLine(e);
            }
        }

        public static void Client_main()
        {
            ApplicationInstance application = new ApplicationInstance();
            application.ApplicationName = "UA Console Client";
            application.ApplicationType = ApplicationType.Client;
            application.ConfigSectionName = "Opc.Ua.ConsoleClient";
            INI_data = jsonlib.Initial(ini_filename);
            g_Capability = jsonlib.CreateCapability();

            //insert Url in Json object
            for(int i = 0; i < INI_data.ServerName.Length; i++) 
            {
                jsonlib.SetStringValue(g_Capability, INI_data.Url[i], INI_data.ServerName[i], "info/Url");
            }          
                        
            m_MonitoredItem_Notification = new MonitoredItemNotificationEventHandler(MonitoredItem_Notification);
            application.LoadApplicationConfiguration(false).Wait();

            // check the application certificate.
            bool certOK = application.CheckApplicationInstanceCertificate(false, 0).Result;
            if (!certOK)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            for (int i =0; i < INI_data.ServerName.Length; i++)
            {
                var identify = new UserIdentity();
                string user = GetUsernameString(INI_data.Authorization[i]);

                if (user != "")
                {
                    string pass = GetPasswordString(INI_data.Authorization[i]);
                    identify = new UserIdentity(user, pass);
                }
                
                Connect(INI_data.Url[i], application.ApplicationConfiguration, identify);

                for(int j = 0; j < INI_data.pNodes[i].Length; j++)
                {
                    string[] TagName = INI_data.pNodes[i][j].NodeName.Split('?');
                    CreateMonitoredItem(INI_data.pNodes[i][j].NodeName, TagName[1], m_session[m_session.Count - 1]);//Add monitor tag
                }
         
            }
            //System.Diagnostics.Debugger.Break();
        }

        public static async void Connect(string serverUrl, ApplicationConfiguration m_configuration, UserIdentity identify)
        {
            foreach(Session find in m_session.ToList())
            {
                if (find.Endpoint.EndpointUrl == serverUrl)
                {
                    Disconnect(find);
                    m_session.Remove(find);
                }                    
            }

            if (serverUrl == null)
            {
                return;
            }
                        
            try
            {
                EndpointDescription endpointDescription = ClientUtils.SelectEndpoint(serverUrl, true);
                EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(m_configuration);
                ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

                Session session = await Session.Create(
                    m_configuration,
                    endpoint,
                    false,
                    m_configuration.ApplicationName,
                    60000,
                    identify,
                    PreferredLocales);

                if (session != null)
                {
                    // stop any reconnect operation.
                    if (m_reconnectHandler != null)
                    {
                        m_reconnectHandler.Dispose();
                        m_reconnectHandler = null;
                    }
                    session.KeepAlive += new KeepAliveEventHandler(Session_KeepAlive);
                    m_session.Add(session);
                    Console.WriteLine(session.Endpoint.EndpointUrl + "   start .......");
                }
                else
                {
                    Console.WriteLine("OPCUA client connect failed");
                }
            }
            catch(Exception e)
            {
                WriteLog("[error] " + e.Message + e.StackTrace);
                Console.WriteLine(e);
            }        
        }

        public static void Disconnect(Session session)
        {
            SessionReconnectHandler m_reconnectHandler = new SessionReconnectHandler();

            // stop any reconnect operation.
            if (m_reconnectHandler != null)
            {
                m_reconnectHandler.Dispose();
                m_reconnectHandler = null;
            }

            // disconnect existing session.
            if (session != null)
            {
                SendDisconnect(session);
                session.Close(10000);
                session = null;
            }
        }

        public static void Disconnect()
        {
            SessionReconnectHandler m_reconnectHandler = new SessionReconnectHandler();

            // stop any reconnect operation.
            if (m_reconnectHandler != null)
            {
                m_reconnectHandler.Dispose();
                m_reconnectHandler = null;
            }

            // disconnect any existing session.
            foreach (Session session in m_session)
            {
                if (session != null)
                {
                    if(session.SubscriptionCount > 0)
                    {                
                        for (int i = 0; i < session.Subscriptions.ElementAt(0).MonitoredItemCount; i++)
                        {
                            session.Subscriptions.ElementAt(0).MonitoredItems.ElementAt(i).Notification -= m_MonitoredItem_Notification;
                        }

                        session.RemoveSubscription(session.Subscriptions.ElementAt(0));
                    }
               
                    SendDisconnect(session);
                    session.Dispose();
                    session.Close(10000);
                }
            }
            
        }

        private static void Session_KeepAlive(Session session, KeepAliveEventArgs e)
        {
            try
            {
                // start reconnect sequence on communication error.
                if (e != null && m_session != null && ServiceResult.IsBad(e.Status))
                {
                    //send connection status
                    SendDisconnect(session);

                    if (m_reconnectPeriod <= 0)
                    {                        
                        return;
                    }

                    if (m_reconnectHandler == null)
                    {
                        m_reconnectHandler = new SessionReconnectHandler();
                        m_reconnectHandler.BeginReconnect(session, m_reconnectPeriod * 1000, Server_ReconnectComplete);
                    }
                }
            }
            catch (Exception exception)
            {
                WriteLog("[error] " + exception.Message + exception.StackTrace);
                Console.WriteLine(exception);
            }
        }

        private static void Server_ReconnectComplete(object sender, EventArgs e)
        {
            try
            {
                // ignore callbacks from discarded objects.
                if (!Object.ReferenceEquals(sender, m_reconnectHandler))
                {
                    return;
                }
                
                Session session = m_reconnectHandler.Session;

                foreach (Session find in m_session.ToList())
                {
                    if (find.ConfiguredEndpoint.EndpointUrl.AbsoluteUri == session.ConfiguredEndpoint.EndpointUrl.AbsoluteUri)
                    {                      
                        m_session.Remove(find);
                        m_session.Add(session);
                    }
                }

                m_reconnectHandler.Dispose();
                m_reconnectHandler = null;
                Session_KeepAlive(session, null);

            }
            catch (Exception exception)
            {
                WriteLog("[error] " + exception.Message + exception.StackTrace);
                Console.WriteLine(exception);
            }
        }

        //public static void init_node(string path)
        //{
        //    m_MonitoredItem_Notification = new MonitoredItemNotificationEventHandler(MonitoredItem_Notification);
        //    string[] node_name = path.Split(':');
        //    Console.WriteLine(node_name[1]);
        //    //fetch_node(path);
        //}

        public static void fetch_node(NodeId sourceId, Session session)
        {
            //node.Clear();

            // find all of the components of the node.
            BrowseDescription nodeToBrowse1 = new BrowseDescription();
            nodeToBrowse1.NodeId = sourceId;
            nodeToBrowse1.BrowseDirection = BrowseDirection.Forward;
            nodeToBrowse1.ReferenceTypeId = ReferenceTypeIds.Aggregates;
            nodeToBrowse1.IncludeSubtypes = true;
            nodeToBrowse1.NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable);
            nodeToBrowse1.ResultMask = (uint)BrowseResultMask.All;

            // find all nodes organized by the node.
            BrowseDescription nodeToBrowse2 = new BrowseDescription();
            nodeToBrowse2.NodeId = sourceId;
            nodeToBrowse2.BrowseDirection = BrowseDirection.Forward;
            nodeToBrowse2.ReferenceTypeId = ReferenceTypeIds.Organizes;
            nodeToBrowse2.IncludeSubtypes = true;
            nodeToBrowse2.NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable);
            nodeToBrowse2.ResultMask = (uint)BrowseResultMask.All;

            BrowseDescriptionCollection nodesToBrowse = new BrowseDescriptionCollection();
            nodesToBrowse.Add(nodeToBrowse1);
            nodesToBrowse.Add(nodeToBrowse2);
            ReferenceDescriptionCollection references = FormUtils.Browse(session, nodesToBrowse, false);

            // process results.
            foreach (ReferenceDescription target in references)
            {
                //ReferenceDescription target = references[ii];

                // add node.
                //TreeNode child = new TreeNode(Utils.Format("{0}", target));
                //child.Tag = target;
                //child.Nodes.Add(new TreeNode());
                //node.Add(child);
                //Console.WriteLine(Readvalue((NodeId)target.NodeId, Attributes.Value));
                //expand_node(node_tree);
                //fetch_node((NodeId)target.NodeId, node);
                Console.WriteLine(target);
            }
        }

        public static void WriteData(NodeId nodeId, uint attributeId, string write,Session session)
        {
            try
            {
                ReadValue(nodeId, attributeId, session);

                WriteValue valueToWrite = new WriteValue();

                valueToWrite.NodeId = nodeId;
                valueToWrite.AttributeId = attributeId;
                valueToWrite.Value.Value = ChangeType(write);
                valueToWrite.Value.StatusCode = StatusCodes.Good;
                valueToWrite.Value.ServerTimestamp = DateTime.MinValue;
                valueToWrite.Value.SourceTimestamp = DateTime.MinValue;

                WriteValueCollection valuesToWrite = new WriteValueCollection();
                valuesToWrite.Add(valueToWrite);

                // write current value.
                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                session.Write(
                    null,
                    valuesToWrite,
                    out results,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(results, valuesToWrite);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToWrite);

                if (StatusCode.IsBad(results[0]))
                {
                    throw new ServiceResultException(results[0]);
                }

                //DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
            }
        }

        //public static string InsertValueInJson ()
        public static string ReadValue(NodeId nodeId, uint attributeId,Session session)
        {
            string value_temp;

            ReadValueId nodeToRead = new ReadValueId();
            nodeToRead.NodeId = nodeId;
            nodeToRead.AttributeId = attributeId;

            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();
            nodesToRead.Add(nodeToRead);

            // read current value.
            DataValueCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            session.Read(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            m_value = results[0];
            value_temp = Utils.Format("{0}", m_value.WrappedValue);

            return value_temp;
        }

        public static object ChangeType(string value_write)
        {
            object value = (m_value != null) ? m_value.Value : null;

            switch (m_value.WrappedValue.TypeInfo.BuiltInType)
            {
                case BuiltInType.Boolean:
                    {
                        value = Convert.ToBoolean(value_write);
                        break;
                    }

                case BuiltInType.SByte:
                    {
                        value = Convert.ToSByte(value_write);
                        break;
                    }

                case BuiltInType.Byte:
                    {
                        value = Convert.ToByte(value_write);
                        break;
                    }

                case BuiltInType.Int16:
                    {
                        value = Convert.ToInt16(value_write);
                        break;
                    }

                case BuiltInType.UInt16:
                    {
                        value = Convert.ToUInt16(value_write);
                        break;
                    }

                case BuiltInType.Int32:
                    {
                        value = Convert.ToInt32(value_write);
                        break;
                    }

                case BuiltInType.UInt32:
                    {
                        value = Convert.ToUInt32(value_write);
                        break;
                    }

                case BuiltInType.Int64:
                    {
                        value = Convert.ToInt64(value_write);
                        break;
                    }

                case BuiltInType.UInt64:
                    {
                        value = Convert.ToUInt64(value_write);
                        break;
                    }

                case BuiltInType.Float:
                    {
                        value = Convert.ToSingle(value_write);
                        break;
                    }

                case BuiltInType.Double:
                    {
                        value = Convert.ToDouble(value_write);
                        break;
                    }

                default:
                    {
                        value = value_write;
                        break;
                    }
            }

            return value;
        }

        public static void CreateMonitoredItem(NodeId nodeId,string displayname, Session session_c)
        {
            Subscription m_subscription = new Subscription();

            if (session_c.SubscriptionCount == 0)
            {
                Subscription subscription = new Subscription(session_c.DefaultSubscription);
                subscription.PublishingEnabled = true;
                subscription.PublishingInterval = 1000;
                subscription.KeepAliveCount = 10;
                subscription.LifetimeCount = 10;
                subscription.MaxNotificationsPerPublish = 1000;
                subscription.Priority = 100;
                session_c.AddSubscription(subscription);
                subscription.Create();

                m_subscription = subscription;
            }

            else
            {
                m_subscription = session_c.Subscriptions.ElementAt(0);
            }

            // add the new monitored item.
            MonitoredItem monitoredItem = new MonitoredItem(m_subscription.DefaultItem);
            monitoredItem.StartNodeId = nodeId;
            monitoredItem.AttributeId = Attributes.Value;
            monitoredItem.DisplayName = displayname;
            monitoredItem.MonitoringMode = MonitoringMode.Reporting;
            monitoredItem.SamplingInterval = 1000;
            monitoredItem.QueueSize = 0;
            monitoredItem.DiscardOldest = true;
            monitoredItem.Notification += m_MonitoredItem_Notification;

            m_subscription.AddItem(monitoredItem);
          
            if (ServiceResult.IsBad(monitoredItem.Status.Error))
            {
                Console.WriteLine(monitoredItem.Status.Error.StatusCode.ToString());
            }

            m_subscription.ApplyChanges();

        }

        public static void MonitoredItem_Notification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                MonitoredItemNotification notification = e.NotificationValue as MonitoredItemNotification;

                if (notification == null)
                {
                    return;
                }
                else
                {           
                    string SenPath;
                    string value = Utils.Format("{0}", notification.Value.WrappedValue);
                    string fullpath = Utils.Format("{0}", monitoredItem.StartNodeId); //OPCUA Nodeid format: ns=2;s=1:SCADA1/Port(3)/PLC1?AI1
                    string ServerUrl = monitoredItem.Subscription.Session.Endpoint.EndpointUrl;                 
                    Nodeid_parse(fullpath, out SenPath);
                    WriteLog("[info] " + SenPath + " type= " + notification.Value.WrappedValue.TypeInfo);

                    if (ServerUrl != null && fullpath != null)
                    {
                      for(int i = 0; i < INI_data.Url.Length; i++)
                      {
                         if(INI_data.Url[i] == ServerUrl)
                         {
                            jsonlib.SetbooleanValue(g_Capability, monitoredItem.Subscription.Session.Connected, INI_data.ServerName[i], "info/Connection");
                            writeValueINI(value, INI_data.ServerName[i], fullpath);
                            JsonSetValue(notification.Value.WrappedValue.TypeInfo.BuiltInType, value, INI_data.ServerName[i], SenPath);                         
                            if (Checkmonitorfinish())
                            {
                                jsonlib.ZMQSend(g_Capability.ToString(Formatting.None));
                                WriteLog("[info] " + g_Capability.ToString(Formatting.None));
                            }
                         }
                      }
                      
                    }
                    else
                    {
                        Console.WriteLine("Invalid NodeID:" + fullpath);
                    }
                }               
            }
            catch (Exception exception)
            {
                WriteLog("[error] " + exception.Message + exception.StackTrace);
                Console.WriteLine(exception);
            }
        }

        public static void JsonSetValue (BuiltInType TypeInfo, string value, string group, string SensPath)
        {
            switch (TypeInfo)
            {
                case BuiltInType.Boolean:
                    {
                        jsonlib.SetbooleanValue(g_Capability, Convert.ToBoolean(value), group, SensPath);
                        break;
                    }

                case BuiltInType.UInt16:
                case BuiltInType.UInt32:
                case BuiltInType.UInt64:
                case BuiltInType.UInteger:
                case BuiltInType.SByte:
                case BuiltInType.Byte:
                case BuiltInType.Float:
                case BuiltInType.Int16:
                case BuiltInType.Int32:
                case BuiltInType.Int64:
                case BuiltInType.Integer:
                case BuiltInType.Double:
                case BuiltInType.Number:               
                    {
                        jsonlib.SetDoubleValue(g_Capability, Convert.ToDouble(value), group, SensPath);
                        break;
                    }

                case BuiltInType.String:
                default:
                    {
                        jsonlib.SetStringValue(g_Capability, value, group, SensPath);
                        break;
                    }

            }
        }

        public static string INItoNodeid(string NodePath)
        {
            int tagindex = NodePath.LastIndexOf("/");
            string nodeid = NodePath.Remove(tagindex, 1);
            nodeid = nodeid.Insert(tagindex, "?");
            nodeid = "ns=2;s=1:" + nodeid;
            return nodeid;
        }

        public static void Nodeid_parse(string nodeID, out string path)
        {
            int tagindex = nodeID.LastIndexOf("?");
            string[] parse_temp;
            if (tagindex != 0)
            {
                nodeID = nodeID.Remove(tagindex, 1);
                nodeID = nodeID.Insert(tagindex, "/");
                parse_temp = nodeID.Split(':');
                path = parse_temp[1];
                /*groupindex = parse_temp[1].IndexOf("/");
                groupname = parse_temp[1].Remove(groupindex);
                path = parse_temp[1].Remove(0, groupindex + 1);*/
            }
            else
            {
                path = null;
            }
        }             

        public static void writeValueINI(string value, string ServerName, string SenPath)
        {
            for (int i = 0; i < INI_data.numberOfServer; i++)
            {
                if(INI_data.ServerName[i] == ServerName)
                {
                    for (int j = 0; j < INI_data.numberOfNode[i]; j++)
                    {
                        if (string.Equals(INI_data.pNodes[i][j].NodeName, SenPath) && INI_data.pNodes[i][j].Value ==null)
                        {
                            INI_data.pNodes[i][j].Value = value;
                            break;
                        }                       
                    }
                }
            }
        }

        public static bool Checkmonitorfinish()
        {
            bool result = true;
                for (int i = 0; i < INI_data.numberOfServer; i++)
                {                   
                     for (int j = 0; j < INI_data.numberOfNode[i]; j++)
                     {
                         if (INI_data.pNodes[i][j].Value == null)
                         {
                             result = false;
                             break;
                         }                       
                     }                    
                }
            return result;
        }

        public static void WriteLog(string msg)
        {
            string log_filename = "Log.txt";
            string logpath = Path.GetDirectoryName(Application.ExecutablePath) + "/" + log_filename;
            using (StreamWriter writer = new StreamWriter(logpath, true))
            {
                writer.WriteLine(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + " " + msg);
                writer.Close();
            }
        }

        //Send disconnect status to server
        public static void SendDisconnect(Session session)
        {
            for (int i = 0; i < INI_data.Url.Length; i++)
            {
                if (session.ConfiguredEndpoint.EndpointUrl.AbsoluteUri == INI_data.Url[i])
                {
                    jsonlib.SetbooleanValue(g_Capability, false, INI_data.ServerName[i], "info/Connection");
                    jsonlib.ZMQSend(g_Capability.ToString(Formatting.None));
                }
            }
        }

        public static string GetUsernameString(string authorization)
        {
            string data = authorization.Replace("Basic ", string.Empty);
            string decode_data = Base64Decode(data);
            string pattern = ":";

            string[] strData = Regex.Split(decode_data, pattern);
            // strData[0]: username
            // strData[1]: password

            return strData[0];
        }

        public static string GetPasswordString(string authorization)
        {
            string data = authorization.Replace("Basic ", string.Empty);
            string decode_data = Base64Decode(data);
            string pattern = ":";

            string[] strData = Regex.Split(decode_data, pattern);
            // strData[0]: username
            // strData[1]: password

            return strData[1];
        }

        public static string GetAuthorizationString(string username, string password)
        {
            return "Basic " + Base64Encode(username + ":" + password);
        }

        private static string Base64Encode(string plainText)
        {
            if (plainText != null)
            {
                var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
                return System.Convert.ToBase64String(plainTextBytes);
            }
            else
                return null;
        }

        private static string Base64Decode(string base64EncodedData)
        {
            if (base64EncodedData != null)
            {
                var base64EncodedBytes = System.Convert.FromBase64String(base64EncodedData);
                return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
            }
            else
                return null;
        }

        public static string[] PreferredLocales { get; set; }
    }
}
