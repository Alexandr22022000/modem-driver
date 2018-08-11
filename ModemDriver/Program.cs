using System;
using System.Text;
using System.IO.Ports;
using SMSPDULib;
using Quobject.SocketIoClientDotNet.Client;

namespace TestSMS_2
{
    class Program
    {
        static Modem modem = null;
        static Socket socket = null;
        static bool needCheck = false;
        static bool isControll = false;

        static void Main(string[] args)
        {
            socket = IO.Socket("http://localhost:6000");
            socket.On(Socket.EVENT_CONNECT, () =>
            {
                Console.WriteLine("CONNECT - command");
                SendMsgToServer("driver");
            });

            socket.On("start", (port) =>
            {
                Console.WriteLine("start - command");
                modem = new Modem((string)port);
                modem.GetCIMI(CallbackCIMI);
            });

            socket.On("clean", (port) =>
            {
                Console.WriteLine("clean - command");
                modem.DelAllMessage();
            });

            socket.On("send-msg", (phone) =>
            {
                Console.WriteLine("send-msg - command");
                modem.SendMessage((string)phone, "Krivetka");
            });

            socket.On("check-msg", () =>
            {
                Console.WriteLine("check-msg - command");
                isControll = false;
                needCheck = true;
                while (needCheck)
                {
                    modem.CheckMessage(CallbackMsg);
                    System.Threading.Thread.Sleep(2000);
                }
            });

            socket.On("check-number", () =>
            {
                Console.WriteLine("check-number - command");
                isControll = true;
                needCheck = true;
                while (needCheck)
                {
                    modem.CheckMessage(CallbackMsg);
                    System.Threading.Thread.Sleep(2000);
                }
            });

            socket.On("stop", () =>
            {
                Console.WriteLine("stop - command");
                modem.Stop();
            });

            Console.ReadLine();
        }

        static private void CallbackCIMI(string data)
        {
            try
            {
                int i = data.IndexOf("\n", 0) + 1;
                data = data.Substring(i, data.IndexOf("\n", i) - (i + 1));
                SendMsgToServer("CIMI", data);
                Console.WriteLine(data + " - sending msg");
            }
            catch (Exception e) { Console.WriteLine(e); } 
        }

        static private void CallbackMsg(string data)
        {
            try
            {
                if (data.IndexOf("CMGL", 0) == -1)
                {
                    Console.WriteLine("None msg");
                    return;
                }

                int i = data.IndexOf("\n", 0) + 1;
                i = data.IndexOf("\n", i) + 1;
                data = data.Substring(i, data.IndexOf("\n", i) - (i + 1));

                SMS sms = new SMS();
                SMS.Fetch(sms, ref data);

                needCheck = false;

                if (isControll)
                {
                    Console.WriteLine(sms.PhoneNumber + " - sending msg");
                    SendMsgToServer("number", sms.PhoneNumber);

                }
                else
                {
                    Console.WriteLine(sms.Message + " - sending msg");
                    SendMsgToServer("code", sms.Message);
                }
            }
            catch (Exception e) { Console.WriteLine(e); }
        }

        private static void SendMsgToServer(string action)
        {
            socket.Emit("my-test");
            socket.Emit(action);
        }

        private static void SendMsgToServer(string action, object data)
        {
            socket.Emit("my-test");
            socket.Emit(action, data);
        }
    }

    class Modem
    {
        private SerialPort port;
        private bool isError = false;
        private Action<string> callback = null;
        private int stopReadCount = 0;
        private Callbacks callbackType;

        public Modem(string comPort)
        {
            port = new SerialPort();

            port.BaudRate = 2400; // еще варианты 4800, 9600, 28800 или 56000
            port.DataBits = 7; // еще варианты 8, 9

            port.StopBits = StopBits.One; // еще варианты StopBits.Two StopBits.None или StopBits.OnePointFive         
            port.Parity = Parity.Odd; // еще варианты Parity.Even Parity.Mark Parity.None или Parity.Space

            port.ReadTimeout = 500; // самый оптимальный промежуток времени
            port.WriteTimeout = 500; // самый оптимальный промежуток времени

            port.Encoding = Encoding.GetEncoding("windows-1251");
            port.PortName = comPort;

            // незамысловатая конструкция для открытия порта
            if (port.IsOpen)
                port.Close(); // он мог быть открыт с другими параметрами
            try
            {
                port.Open();
            }
            catch (Exception e)
            {
                isError = true;
                Console.WriteLine("Connecting error!");
            }

            port.DataReceived += ReadData;
        }

        public void Stop()
        {
            if (isError) return;
            port.Close();
        }

        private void ReadData(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort sp = (SerialPort)sender;
            string data = sp.ReadExisting();
            Console.WriteLine("Data Received:");
            Console.WriteLine(data);

            if (callback != null)
            {
                switch (callbackType)
                {
                    case Callbacks.CIMI:
                        CallbackCIMI(data);
                        break;

                    case Callbacks.Msg:
                        CallbackMsg(data);
                        break;
                }
            }
        }

        public void GetCIMI(Action<string> callback)
        {
            if (isError) return;

            this.callback = callback;
            callbackType = Callbacks.CIMI;
            port.Write("AT+CIMI" + "\r\n");
            System.Threading.Thread.Sleep(500);
        }

        public void CheckMessage(Action<string> callback)
        {
            if (isError) return;

            port.Write("AT+CMGF=0" + "\r\n");
            System.Threading.Thread.Sleep(500);

            port.Write("AT+CPMS =\"SM\"" + "\r\n");
            System.Threading.Thread.Sleep(500);

            this.callback = callback;
            callbackType = Callbacks.Msg;
            port.Write("AT+CMGL=4" + "\r\n");
            System.Threading.Thread.Sleep(500);
        }

        public void SendMessage(string phone, string text)
        {
            if (isError) return;

            port.WriteLine("AT \r\n"); // значит Внимание! для модема 
            System.Threading.Thread.Sleep(500);

            port.Write("AT+CMGF=1 \r\n"); // устанавливается текстовый режим для отправки сообщений
            System.Threading.Thread.Sleep(500);

            port.Write("AT+CMGS=\"" + phone + "\"" + "\r\n");
            System.Threading.Thread.Sleep(500);

            port.Write(text + char.ConvertFromUtf32(26) + "\r\n");
            System.Threading.Thread.Sleep(500);
        }

        public void DelAllMessage()
        {
            if (isError) return;

            port.Write("AT+CPMS =\"SM\"" + "\r\n");
            System.Threading.Thread.Sleep(500);

            for (int i = 0; i < 10; i++)
            {
                port.Write("AT+CMGD=" + i + "\r\n");
                System.Threading.Thread.Sleep(500);
            }
        }

        enum Callbacks { CIMI, Msg };

        private void CallbackCIMI(string data)
        {
            if (data.Length > 10)
            {
                callback(data);
                callback = null;
            }
            else
            {
                stopReadCount++;

                if (stopReadCount == 2)
                {
                    callback(data);
                    stopReadCount = 0;
                    callback = null;
                }
            }
        }

        private void CallbackMsg(string data)
        {
            stopReadCount++;

            if (stopReadCount == 2)
            {
                callback(data);
                stopReadCount = 0;
                callback = null;
            }
        }
    }
}
