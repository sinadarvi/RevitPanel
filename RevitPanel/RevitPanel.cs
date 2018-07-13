using Autodesk.Revit.UI;
using System.Reflection;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using SimpleTCP;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Net.NetworkInformation;
using System.Collections;
using System.Collections.Concurrent;
using System.Timers;
using System.IO;

namespace RevitPanel
{
    public class RevitPaneler : IExternalApplication
    {
        public static string msg = "nothing";
        public static bool status = false;

        public static bool devmod = true;



        // Both OnStartup and OnShutdown must be implemented as public method
        public Result OnStartup(UIControlledApplication application)
        {
            // Add a new ribbon panel
            RibbonPanel ribbonPanel = application.CreateRibbonPanel("Strength Concrete");

            string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;
            PushButtonData connect = new PushButtonData("connect"
                , "Connecting To Device and get Log", thisAssemblyPath, "RevitPanel.Connect");
            
            PushButtonData disconnect = new PushButtonData("disconnect"
                , "Disconnect and show the result", thisAssemblyPath, "RevitPanel.Disconnect");
            

            IList<RibbonItem> stackedItems = ribbonPanel.AddStackedItems(connect, disconnect);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            // nothing to clean up in this simple case
            return Result.Succeeded;
        }
    }

    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class Connect : IExternalCommand
    {
        public static Socket NetSocket;
        public static ConcurrentQueue<String> DataRXQueue = new ConcurrentQueue<string>();
        public static ConcurrentQueue<String> DataTXQueue = new ConcurrentQueue<string>();
        public static ConcurrentQueue<String> ResponseRXQueue = new ConcurrentQueue<string>();
        public static ConcurrentQueue<Byte[]> LogRXQueue = new ConcurrentQueue<Byte[]>();
        private static Thread DataTXThread;
        private static Thread DataRXThread;

        public static AutoResetEvent DataTXNew = new AutoResetEvent(false);
        private static ManualResetEvent DataRXDone = new ManualResetEvent(false);
        private static AutoResetEvent ResponseRXDone = new AutoResetEvent(false);
        private static AutoResetEvent LogRXDone = new AutoResetEvent(false);
        string LastRXData = string.Empty;
        string[] ParserBuffer;
        Double LastVal = 0;
        int XAxis = 1;
        public static string rxbuffer;
        public static List<LogRecord> LogRecordArray = new List<LogRecord>();
        ArrayList array = new ArrayList();
        public static bool ready = false;
        public static System.Timers.Timer aTimer;
        public static bool sw = false;


        private void AddData(DateTime DataTime, double DataValue1, double DataValue2, double DataValue3)
        {
                LogRecordArray.Add(new LogRecord(DataTime, DataValue1, DataValue2, DataValue3));
        }

        public class LogRecord
        {
            public DateTime Time;
            public double Val1, Val2, Val3;
            public double M_red, M_blue, M_green;
            public LogRecord(DateTime Time,double Val1, double Val2, double Val3)
            {
                this.Time = Time;
                this.Val1 = Val1;
                this.Val2 = Val2;
                this.Val3 = Val3;
            }

            public DateTime GetTime()
            {
                return this.Time;
            }

            public double getM_red()
            {
                return M_red;
            }

            public double getM_blue()
            {
                return M_blue;
            }

            public double getM_green()
            {
                return M_green;
            }

            public void setM_red(double M_red)
            {
                this.M_red = M_red;
            }

            public void setM_blue(double M_blue)
            {
                this.M_blue = M_blue;
            }

            public void setM_green(double M_green)
            {
                this.M_green = M_green;
            }
            //public string Time { get => time; set => time = value; }
            //public float Val { get => val; set => val = value; }
        }


        public class StateObject
        {
            // Client socket.  
            public Socket workSocket = null;
            // Size of receive buffer.  
            public const int BufferSize = 256;
            // Receive buffer.  
            public byte[] buffer = new byte[BufferSize];
            // Received data string.  
            public StringBuilder sb = new StringBuilder();
        }

        public static void SocketTransmit(string str)
        {
            DataTXQueue.Enqueue("@" + str);
            DataTXNew.Set();
        }

        public static bool FindString(string MainStr, string FindStr)
        {
            bool res = false;
            if (MainStr != null)
            {
                if (MainStr.IndexOf(FindStr) == -1) res = false;
                else res = true;
            }
            return res;
        }


        void ParseRXData()
        {
            if(sw)
                aTimer.Stop();

                string TempCommand;

                if (FindString(LastRXData, "#data") == true)
                {
                    

                    TempCommand = LastRXData.Substring(6, LastRXData.Length - 6);
                    ParserBuffer = TempCommand.Split(',');
                    
                    int i = 0;
                    while (i < ParserBuffer.Length - 1)
                    {
                        
                        try
                        {
                            var tmp = ParserBuffer[i].Split('-');
                            DateTime tmpdate = new DateTime(2000 + Convert.ToInt16(tmp[0]), Convert.ToInt16(tmp[1]), Convert.ToInt16(tmp[2]), Convert.ToInt16(tmp[3]), Convert.ToInt16(tmp[4]), 0);
                            var Val1 = Double.Parse(ParserBuffer[i + 1]);
                            var Val2 = Double.Parse(ParserBuffer[i + 2]);
                            var Val3 = Double.Parse(ParserBuffer[i + 3]);
                            

                            if (Val1 > 80 || Val1 < -20 || ((Math.Abs(LastVal - Val3) > 30) && (LastVal != 0)))
                            {
                                Console.WriteLine("Out of Range Data : " + Val1.ToString());
                            }
                            else
                            {
                                if (LogRecordArray.Count == 0)
                                {
                                    AddData(tmpdate, Val1, Val2, Val3);
                                }
                                else if (LogRecordArray[(LogRecordArray.Count - 1)].GetTime().Hour < tmpdate.Hour)
                                {
                                    AddData(tmpdate, Val1, Val2, Val3);
                                }
                                
                                LastVal = Val3;
                            }
                            XAxis++;
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Parse-Add Error > " + e.ToString());
                            Console.WriteLine("Details Time: " + ParserBuffer[i] + ",Value:" + ParserBuffer[i + 1]);
                        }
                        i += 4;
                    }

                }
            
            aTimer = new System.Timers.Timer();
            aTimer.Interval = 2000;

            // Hook up the Elapsed event for the timer. 
            aTimer.Elapsed += result;

            // Have the timer fire repeated events (true is the default)
            aTimer.AutoReset = true;

            // Start the timer
            aTimer.Enabled = true;

            sw = true;

        }

        public static void result(Object source, System.Timers.ElapsedEventArgs e)
        {
            for (int z = 0; z < LogRecordArray.Count; z++)
            {
                double t_avg_red = 0;
                double t_avg_blue = 0;
                double t_avg_green = 0;

                double t_all_red = 0;
                double t_all_blue = 0;
                double t_all_green = 0;

                double M_red = 0;
                double M_green = 0;
                double M_blue = 0;

                for (int k = 0; k <= z; k++)
                {
                    t_all_red += LogRecordArray[k].Val3;
                    t_all_blue += LogRecordArray[k].Val2;
                    t_all_green += LogRecordArray[k].Val1;
                }

                t_avg_red = t_all_red / (z + 1);
                t_avg_blue = t_all_blue / (z + 1);
                t_avg_green = t_all_green / (z + 1);

                int i = 0;

                for (int k = 0; k <= z; k++)
                {
                    M_red += ((t_avg_red - LogRecordArray[k].Val3) * i);
                    M_blue += ((t_avg_blue - LogRecordArray[k].Val2) * i);
                    M_green += ((t_avg_green - LogRecordArray[k].Val1) * i);
                    i++;
                }

                LogRecordArray[z].setM_red(M_red);
                LogRecordArray[z].setM_blue(M_blue);
                LogRecordArray[z].setM_green(M_green);
            }

            Connect.ready = true;
            sw = false;
        }
        void DataRXThreadFunction()
        {
            Console.WriteLine("Reception Thread Launched!");
            while (true)
            {
                if (DataRXQueue.Count == 0)
                {
                    DataRXDone.Reset();
                }
                DataRXDone.WaitOne();
                DataRXQueue.TryDequeue(out LastRXData);
                ParseRXData();
            }
        }

        public static void DataTXThreadFunction()
        {
            Console.WriteLine("Transmission Thread Launched!");
            while (true)
            {
                if (DataTXQueue.Count > 0)
                {
                    //Sending Procedure
                    string DataToSend = String.Empty;
                    DataTXQueue.TryDequeue(out DataToSend);
                    if (DataToSend == String.Empty)
                    {
                        Console.WriteLine("wrong empty Data to send ?!");
                    }
                    int TimeoutCounter = 0;
                    string garbage;
                    while (ResponseRXQueue.TryDequeue(out garbage)) { } // Clear responses
                    while (true)
                    {
                        if (NetSocket.Connected == true) NetSocket.Send(Encoding.ASCII.GetBytes(DataToSend + "\r\n"));
                        Console.WriteLine("TX>" + DataToSend);
                        string LastReceivedData = string.Empty;
                        ResponseRXDone.WaitOne(1000);
                        ResponseRXQueue.TryDequeue(out LastReceivedData);
                        DataToSend = DataToSend.Replace("\r\n", "");
                        if (LastReceivedData == "@ok:" + DataToSend)
                        {
                            Console.WriteLine("Sent:OK");
                            break;
                        }
                        else
                        {
                            Console.WriteLine( "Sent:Failed>LastResponse:" + LastReceivedData + "\t| Trying Again...");
                            Thread.Sleep(1000);
                        }
                        if (TimeoutCounter > 1)
                        {
                            break;
                        }
                        {
                            TimeoutCounter++;
                        }
                    }
                }
                else
                {
                    //Thread goes into sleep+
                    DataTXNew.WaitOne();

                }
            }
        }


        public static void SocketReceive(Socket targetsocket)
        {
            try
            {
                // Create the state object.  
                StateObject state = new StateObject();
                state.workSocket = targetsocket;

                // Begin receiving the data from the remote device.  
                targetsocket.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                    new AsyncCallback(ReceiveCallback), state);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
        private static void ReceiveCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the state object and the client socket   
                // from the asynchronous state object.  
                StateObject state = (StateObject)ar.AsyncState;
                Socket client = state.workSocket;

                // Read data from the remote device. 
                int bytesRead = 0;
                if (client.Connected == true)
                {
                    bytesRead = client.EndReceive(ar);
                }

                if (bytesRead > 0)
                {
                    // There might be more data, so store the data received so far. 
                    rxbuffer += Encoding.ASCII.GetString(state.buffer, 0, bytesRead);
                    while (rxbuffer.IndexOf("\r\n") > 0)
                    {
                        int terminatorpos = rxbuffer.IndexOf("\r\n");
                        string cmd = rxbuffer.Substring(0, terminatorpos);
                        if (rxbuffer.Length - terminatorpos > 5)
                        {
                            rxbuffer = rxbuffer.Substring(terminatorpos + 2, rxbuffer.Length - 2 - terminatorpos);
                        }
                        else { rxbuffer = String.Empty; }
                        if (cmd.Length > 2)
                        {
                            if (cmd.ElementAt(0) == '#')
                            {
                                DataRXQueue.Enqueue(cmd.Replace("\r\n", ""));
                                DataRXDone.Set();
                            }
                            else if (cmd.ElementAt(0) == '@')
                            {
                                ResponseRXQueue.Enqueue(cmd.Replace("\r\n", ""));
                                ResponseRXDone.Set();
                                Console.WriteLine("RX < " + Encoding.ASCII.GetString(state.buffer, 0, bytesRead));

                                if (Connect.FindString(cmd, "@enddata") == true)
                                {
                                    
                                }
                            }
                            else if (cmd.ElementAt(0) == '$')
                            {
                                //LogRXQueue.Enqueue(cmd.Replace("\r", ""));
                                //LogRXDone.Set();
                                //if (dbgflag) Log.Log(Level.Debug, "Log Packet Received!");
                            }
                            else
                            {
                                Console.WriteLine("Corrupted Packet RX < " + Encoding.ASCII.GetString(state.buffer, 0, bytesRead));
                            }
                            // Get the rest of the data.  
                        }
                    }
                    client.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
    new AsyncCallback(ReceiveCallback), state);
                }
                else
                {
                    // All the data has arrived; put it in response.  
                    // Signal that all bytes have been received.  

                }


            }
            catch (Exception e)
            {
                Console.WriteLine( "Error in Receiving > " + e.Message);
            }
        }
        private static void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.  
                Socket client = (Socket)ar.AsyncState;

                // Complete the connection.  
                client.EndConnect(ar);
                SocketReceive(NetSocket);

                DataTXThread.Start();
                DataRXThread.Start();
                

                SocketTransmit("reqdata\r\n");

            }
            catch (Exception e)
            {
                Console.WriteLine( "Connectection Error! > " + e.Message.ToString());
            }
        }
        // The main Execute method (inherited from IExternalCommand) must be public
        public Autodesk.Revit.UI.Result Execute(ExternalCommandData revit,
            ref string message, ElementSet elements)
        {
            
            if (RevitPaneler.status == false)
            {
                // checking having connection to device
                Ping myPing = new Ping();
                String host = "192.168.1.85";
                byte[] buffer = new byte[32];
                int timeout = 1000;
                PingOptions pingOptions = new PingOptions();
                PingReply reply = myPing.Send(host, timeout, buffer, pingOptions);
                // if true we having connectin to device
                if (reply.Status == IPStatus.Success)
                {
                    DataTXThread = new Thread(new ThreadStart(DataTXThreadFunction));
                    DataRXThread = new Thread(new ThreadStart(DataRXThreadFunction));
                    
                    IPAddress ipAddr = IPAddress.Parse("192.168.1.85");
                    IPEndPoint ipEndPoint = new IPEndPoint(ipAddr, 2688);

                    NetSocket = new Socket(ipAddr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    NetSocket.NoDelay = true;
                    NetSocket.BeginConnect(ipEndPoint, new AsyncCallback(ConnectCallback), NetSocket);

      
                    TaskDialog.Show("Data Logger", "Device Connected. Wait 5 min and then check the result");
                    
                    RevitPaneler.status = true;
                }
                else
                {
                    TaskDialog.Show("Data Logger", "You are not connected to Device");
                }
            }
            else if (RevitPaneler.status == true)
            {
                TaskDialog.Show("Data Logger", "Device already connected!");
            }


            return Autodesk.Revit.UI.Result.Succeeded;
        }
    }

    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class Disconnect : IExternalCommand
    {
        // The main Execute method (inherited from IExternalCommand) must be public
        public Autodesk.Revit.UI.Result Execute(ExternalCommandData revit,
            ref string message, ElementSet elements)
        {
            if(Connect.ready == true)
            { 
                if (RevitPaneler.status == false)
                {
                  TaskDialog.Show("Data Logger", "Device already disconnected");
                }
                else if (RevitPaneler.status == true)
                {
                    RevitPaneler.status = false;
                    Connect.NetSocket.Shutdown(SocketShutdown.Both);
                    Connect.NetSocket.Close();


                    int counter = 0;
                    string line;
                    List<string> array = new List<string>();

                    TaskDialog.Show("Data Logger", "Maturety (Red): " + Connect.LogRecordArray[Connect.LogRecordArray.Count - 1].getM_red() + "Centigrade Per Hour"
                        + "\r\n" + "Maturety (Blue): " + Connect.LogRecordArray[Connect.LogRecordArray.Count - 1].getM_blue() + "Centigrade Per Hour"
                        + "\r\n" + "Maturety (Green): " + Connect.LogRecordArray[Connect.LogRecordArray.Count - 1].getM_green() + "Centigrade Per Hour");


                    System.IO.StreamReader file = new System.IO.StreamReader(@"d:\test.ifc");
                    while ((line = file.ReadLine()) != null)
                    {
                        array.Add(line);
                        counter++;
                    }
                    file.Close();
                    try
                    {
                        if (array[array.Count - 1].Contains("END-ISO"))
                        {
                            for (int i = 0; i < Connect.LogRecordArray.Count; i++)
                            {
                                using (StreamWriter sw = File.AppendText(@"d:\test.ifc"))
                                {
                                    sw.WriteLine("#data:" + i + ":time:" + Connect.LogRecordArray[i].GetTime());
                                    sw.WriteLine("ObjectStrength("
                                        + Connect.LogRecordArray[i].getM_red() + ","
                                        + Connect.LogRecordArray[i].getM_blue() + ","
                                        + Connect.LogRecordArray[i].getM_green() + ");");
                                    sw.WriteLine("#enddata:" + i + ":time:" + Connect.LogRecordArray[i].GetTime());
                                }

                            }
                        }
                        else
                        {
                            string temp = array[array.Count - 1].Substring(9);
                            int z = Int32.Parse(temp.Substring(0, temp.IndexOf(":")));
                            for (int i = z + 1; i < Connect.LogRecordArray.Count; i++)
                            {
                                using (StreamWriter sw = File.AppendText(@"d:\test.ifc"))
                                {
                                    sw.WriteLine("#data:" + i + ":time:" + Connect.LogRecordArray[i].GetTime());
                                    sw.WriteLine("ObjectStrength("
                                        + Connect.LogRecordArray[i].getM_red() + ","
                                        + Connect.LogRecordArray[i].getM_blue() + ","
                                        + Connect.LogRecordArray[i].getM_green() + ");");
                                    sw.WriteLine("#enddata:" + i + ":time:" + Connect.LogRecordArray[i].GetTime());
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                    }

                    Connect.ready = true;
                }
            }
            else if(Connect.ready == false)
            {
                TaskDialog.Show("Data Logger", "Wait a Moment Please, Just a Matter");
            }


            return Autodesk.Revit.UI.Result.Succeeded;
        }
    }
}
