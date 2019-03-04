using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Windows.Forms;

using ZeroMQ;
using System.Threading;

namespace JSON_Library
{
    public class jsonlib
    {

        [DllImport("kernel32.dll", EntryPoint = "GetPrivateProfileString", SetLastError = true)]
        private static extern uint GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);

        public struct Node_context_t
        {
            public int NodeNumber;
            public string NodeName;
            public string Value;
        };

        public struct INI_contex_t
        {
            public string[] ServerName;
            public string[] Url;
            public string MachineName;
            public string[] Authorization;
            public int[] numberOfNode;
            public int numberOfServer;
            public Node_context_t[][] pNodes;
        }

        public static class Globals
        {
            public static INI_contex_t INI_data;
            public static JObject g_Capability;
        }

        static public INI_contex_t Initial(string Filename)
        {

            string iniPath = Path.GetDirectoryName(Application.ExecutablePath) + "\\" + Filename;
            StreamReader sr = new StreamReader(iniPath);


            int Nodenumber = 0;
            int Servernumber = 0;
            List<int> nodenumberlist = new List<int>();
            List<string> Servernamelist = new List<string>();
            List<string> Urllist = new List<string>();
            List<string> Authorizationlist = new List<string>();

            while (sr.Peek() != -1)
            {
                string read = sr.ReadLine().Trim();
                if (read.Equals("[Setting]"))
                {
                }
                else if (read.Equals(""))
                {
                    if (Nodenumber != 0)
					{
                        nodenumberlist.Add(Nodenumber);
					}
                    Nodenumber = 0;
                }
                else if (read[0].Equals('['))
                {
                    Servernumber++;
                    read = read.Trim(new Char[] { '[', ']' });
                    Servernamelist.Add(read);
                }
                else if (read.Length > 13 && read.Substring(0,13).Equals("Authorization"))
                {
                    int findequal = read.IndexOf('=');
                    read = read.Remove(0, findequal + 1);
                    Authorizationlist.Add(read);
                }
                else if (read.Length > 3 && read.Substring(0, 3).Equals("Url"))
                {
                    int findequal = read.IndexOf('=');
                    read = read.Remove(0, findequal + 1);
                    Urllist.Add(read);
                }
                else if (read.Length > 3 && read.Substring(0, 3).Equals("Tag"))
                {
                    Nodenumber++;
                }
            }



            StringBuilder sbValue = new StringBuilder(255);

            GetPrivateProfileString("Setting", "MachineName", null, sbValue, 255, iniPath); //fill in MachineName
            Globals.INI_data.MachineName = sbValue.ToString();

            Globals.INI_data.numberOfServer = Servernumber; //fill in numberOfGroup

            Globals.INI_data.ServerName = new string[Servernumber];  //fill in Servernumber
            Globals.INI_data.ServerName = Servernamelist.ToArray();

            Globals.INI_data.Authorization = new string[Servernumber];  //fill in Authorization
            Globals.INI_data.Authorization = Authorizationlist.ToArray();

            Globals.INI_data.Url = new string[Servernumber];  //fill in Url
            Globals.INI_data.Url = Urllist.ToArray();

            Globals.INI_data.numberOfNode = new int[Servernumber];  //fill in numberOfNode
            Globals.INI_data.numberOfNode = nodenumberlist.ToArray();

            Globals.INI_data.pNodes = new Node_context_t[Servernumber][];  //fill in NodeNumber & NodeName
            for (int i = 0; i < Servernumber; i++)
            {
                Globals.INI_data.pNodes[i] = new Node_context_t[Globals.INI_data.numberOfNode[i]];
            }

            for (int i = 0; i < Globals.INI_data.numberOfServer; i++)
            {
                for (int j = 0; j < Globals.INI_data.numberOfNode[i]; j++)
                {
                    Globals.INI_data.pNodes[i][j].NodeNumber = j;
                    GetPrivateProfileString(Globals.INI_data.ServerName[i], "Tag" + j, null, sbValue, 255, iniPath);
                    Globals.INI_data.pNodes[i][j].NodeName = sbValue.ToString();
                }
            }
            sr.Close();
            return Globals.INI_data;
        }


        static JObject CreateRoot()
        {
            JObject myCapability = new JObject();
            myCapability.Add("WISE-PaaS-RMM-Sender", new JObject(new JProperty(Globals.INI_data.MachineName, new JObject())));
            return myCapability;
        }
        static public JObject CreateCapability()
        {
            JObject myCapability = null;
            JObject current = null;
            JObject temp = null;
            myCapability = CreateRoot();

            for (int i = 0; i < Globals.INI_data.numberOfServer; i++)
            {
                current = FindGroup(myCapability, "WISE-PaaS-RMM-Sender");
                current = FindGroup(current, Globals.INI_data.MachineName);
                current = AddGroup(current, Globals.INI_data.ServerName[i]);
                current = FindGroup(current, Globals.INI_data.ServerName[i]);
                current = AddGroup(current, "info");
                current = FindGroup(current, "info");
                current = AddSensorNode(current, "Url");
                current = AddSensorNode(current, "Connection");

                for (int j = 0; j < Globals.INI_data.numberOfNode[i]; j++)
                {
                    current = FindGroup(myCapability, "WISE-PaaS-RMM-Sender");
                    current = FindGroup(current, Globals.INI_data.MachineName);
                    current = FindGroup(current, Globals.INI_data.ServerName[i]);
                    string temp_id = Globals.INI_data.pNodes[i][j].NodeName.Replace('?', '/');
                    string[] tempArray = temp_id.Split(':');
                    string[] sArray = tempArray[1].Split('/');
                    for (int len = 0; len < sArray.Length; len++)
                    {
                        if (len == sArray.Length - 1) //element
                        {
                            temp = FindSensorNode(current, sArray[len]);
                            if (temp == null)
                            {
                               current = AddSensorNode(current, sArray[len]);
                            }                                                        
                        }
                        else // layer
                        {
                            temp = FindGroup(current, sArray[len]);
                            if (temp == null)
                            {
                                current = AddGroup(current, sArray[len]);
                            }                          
                            current = FindGroup(current, sArray[len]);
                        }
                    }
                }

            }
            return myCapability;
        }
        static JObject FindGroup(JObject Capability, string group)
        {
            Capability = (JObject)Capability[group];
            return Capability;
        }
        static JObject AddGroup(JObject Capability, string group)
        {
            Capability.Add(group, new JObject(new JProperty("bn", group)));
            Globals.g_Capability = (JObject)Capability.Root;

            return Capability;
        }

        static JObject FindSensorNode(JObject Capability, string node)
        {
            Capability = (JObject)Capability[node];
            return Capability;
        }

        static JObject AddSensorNode(JObject Capability, string node)
        {
            JArray channelarray;
            channelarray = (JArray)Capability["e"];
            //Capability = (JObject) Capability["e"];
            if (channelarray == null)
            {
                Capability.Add(new JProperty("e", new JArray()));
                ((JArray)Capability.GetValue("e")).Add( // <- add cast
                new JObject(
                    new JProperty("n", node)
                    )
                );
            }
            else
            {
                ((JArray)Capability.GetValue("e")).Add( // <- add cast
                new JObject(
                    new JProperty("n", node)
                    )
                );
            }
            Globals.g_Capability = (JObject)Capability.Root;
            return Capability;
        }

        static JObject FindSensorNodeByPath(JObject Capability, string path)
        {
            bool find = false;
            //JObject tmp = Capability;// JObject.Parse(Capability.ToString()); 
            string[] sArray = path.Split('/');
            //if(sArray.Length ==1 )
            //    Capability = (JObject)Capability[sArray[0]];
            for (int i = 0; i < sArray.Length - 1; i++)
            {
                Capability = (JObject)Capability[sArray[i]];
            }

            JArray channelarray;
            channelarray = (JArray)Capability["e"];
            for (int i = 0; i < channelarray.Count(); i++)
            {
                if (channelarray[i]["n"].ToString() == sArray[sArray.Length - 1])
                {
                    find = true;
                }
            }
            if (find)
                return Capability;
            else
                return null;
        }

        static JObject AddSensorNodeByPath(JObject Capability, string path)
        {
            string[] sArray = path.Split('/');
            JArray channelarray;
            channelarray = (JArray)Capability["e"];
            //Capability = (JObject) Capability["e"];
            if (channelarray == null)
            {
                Capability.Add(new JProperty("e", new JArray()));
                ((JArray)Capability.GetValue("e")).Add( // <- add cast
                new JObject(
                    new JProperty("n", sArray[sArray.Length - 1])
                    )
                );
            }
            else
            {
                ((JArray)Capability.GetValue("e")).Add( // <- add cast
                new JObject(
                    new JProperty("n", sArray[sArray.Length - 1])
                    )
                );
            }
            Globals.g_Capability = (JObject)Capability.Root;
            return Capability;
        }

        static public bool SetStringValue(JObject Capability, string value, string Group, string sensorpath)
        {
            JObject tmp = FindGroup(Capability, "WISE-PaaS-RMM-Sender");
            tmp = FindGroup(tmp, Globals.INI_data.MachineName);
            tmp = FindGroup(tmp, Group);


            tmp = FindSensorNodeByPath(tmp, sensorpath);
            JArray channelarray;
            channelarray = (JArray)tmp["e"];
            string[] sArray = sensorpath.Split('/');
            for (int i = 0; i < channelarray.Count(); i++)
            {
                if (channelarray[i]["n"].ToString() == sArray[sArray.Length - 1])
                {
                    if (channelarray[i]["sv"] == null)
                    {
                        tmp = (JObject)channelarray[i];
                        tmp.Add(new JProperty("sv", value));
                        tmp.Add(new JProperty("asm", "r"));

                        Globals.g_Capability = (JObject)tmp.Root;

                    }
                    else
                    {
                        channelarray[i]["sv"] = value;
                        Globals.g_Capability = (JObject)channelarray.Root;

                    }
                    break;

                }
            }
            return true;
        }

        static public bool SetDoubleValue(JObject Capability, double value, string Group, string sensorpath)
        {
            JObject tmp = FindGroup(Capability, "WISE-PaaS-RMM-Sender");
            tmp = FindGroup(tmp, Globals.INI_data.MachineName);
            tmp = FindGroup(tmp, Group);

            tmp = FindSensorNodeByPath(tmp, sensorpath);
            JArray channelarray;
            channelarray = (JArray)tmp["e"];
            string[] sArray = sensorpath.Split('/');
            for (int i = 0; i < channelarray.Count(); i++)
            {
                if (channelarray[i]["n"].ToString() == sArray[sArray.Length - 1])
                {
                    if (channelarray[i]["v"] == null)
                    {
                        tmp = (JObject)channelarray[i];
                        tmp.Add(new JProperty("v", value));
                        tmp.Add(new JProperty("asm", "r"));

                        Globals.g_Capability = (JObject)tmp.Root;

                    }
                    else
                    {
                        channelarray[i]["v"] = value;
                        Globals.g_Capability = (JObject)channelarray.Root;

                    }
                    break;

                }
            }
            return true;
        }

        static public bool SetbooleanValue(JObject Capability, bool value, string Group, string sensorpath)
        {
            JObject tmp = FindGroup(Capability, "WISE-PaaS-RMM-Sender");
            tmp = FindGroup(tmp, Globals.INI_data.MachineName);
            tmp = FindGroup(tmp, Group);

            tmp = FindSensorNodeByPath(tmp, sensorpath);
            JArray channelarray;
            channelarray = (JArray)tmp["e"];
            string[] sArray = sensorpath.Split('/');
            for (int i = 0; i < channelarray.Count(); i++)
            {
                if (channelarray[i]["n"].ToString() == sArray[sArray.Length - 1])
                {
                    if (channelarray[i]["bv"] == null)
                    {
                        tmp = (JObject)channelarray[i];
                        tmp.Add(new JProperty("bv", value));
                        tmp.Add(new JProperty("asm", "r"));

                        Globals.g_Capability = (JObject)tmp.Root;

                    }
                    else
                    {
                        channelarray[i]["bv"] = value;
                        Globals.g_Capability = (JObject)channelarray.Root;

                    }
                    break;

                }
            }
            return true;
        }

        static void Console_WriteZMessage(string format, ZMessage message, params object[] data)
        {
            Console_WriteZMessage(format, 0, message, data);
        }

        static void Console_WriteZMessage(string format, int messagesNotToRead, ZMessage message, params object[] data)
        {
            var renderer = new StringBuilder();

            var list = new List<object>(data);

            for (int i = messagesNotToRead, c = message.Count; i < c; ++i)
            {
                // here the renderer
                if (i == messagesNotToRead)
                {
                    renderer.Append(format);
                    renderer.Append(": ");
                }
                else
                {
                    renderer.Append(", ");
                }
                renderer.Append("{");
                renderer.Append((i - messagesNotToRead) + data.Length);
                renderer.Append("}");

                // now the message
                ZFrame frame = message[i];

                frame.Position = 0;

                if (frame.Length == 0)
                    list.Add("0");
                else
                    list.Add(frame.ReadString());

                frame.Position = 0;
            }

            Console.WriteLine(renderer.ToString(), list.ToArray());
        }

        static public void ZMQSend(string value)
        {
            string ZMQ_URL = "tcp://127.0.0.1:50001";
            // Prepare our context and publisher
            using (var context = new ZContext())
            using (var publisher = new ZSocket(context, ZSocketType.PUB))
            {
                publisher.Linger = TimeSpan.Zero;
                publisher.Bind(ZMQ_URL);

                int published = 0;
                using (var message = new ZMessage())
                {
                        published++;
                        message.Add(new ZFrame(value));
                        Thread.Sleep(1000);
                        Console_WriteZMessage("Publishing ", message);
                        publisher.Send(message);
                }

                publisher.Unbind(ZMQ_URL);
                publisher.Close();
            }
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
    }
}

