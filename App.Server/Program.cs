using App.Business.Concrete;
using App.Entities.Concrete;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace App.Server
{
    public class Program
    {
        private static readonly Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private static readonly List<Socket> clientSockets = new List<Socket>();
        private const int BUFFER_SIZE = 2048;
        private const int PORT = 27001;
        private static readonly byte[] buffer = new byte[BUFFER_SIZE];
        static void Main(string[] args)
        {
            Console.Title = "Server";
            SetupServer();
            Console.ReadLine();
            CloseAllSockets();
        }

        private static void CloseAllSockets()
        {
            foreach (var client in clientSockets)
            {
                client.Shutdown(SocketShutdown.Both);
                client.Close();
            }
            serverSocket.Close();
        }

        private static void SetupServer()
        {
            Console.WriteLine("Setting up server . . .");
            serverSocket.Bind(new IPEndPoint(IPAddress.Parse("10.1.18.13"), PORT));
            serverSocket.Listen(0);
            serverSocket.BeginAccept(AcceptCallback, null);
        }

        private static void AcceptCallback(IAsyncResult ar)
        {
            Socket socket;
            try
            {
                socket = serverSocket.EndAccept(ar);
            }
            catch (Exception)
            {
                return;
            }

            SendServiceResponseToClient(socket);
            clientSockets.Add(socket);
            socket.BeginReceive(buffer, 0, BUFFER_SIZE, SocketFlags.None, ReceiveCallBack, socket);
            Console.WriteLine("Client connected, waiting for request . . . ");
            serverSocket.BeginAccept(AcceptCallback, null);
        }

        private static void ReceiveCallBack(IAsyncResult ar)
        {
            Socket current = (Socket)ar.AsyncState;
            int received;
            try
            {
                received = current.EndReceive(ar);
            }
            catch (Exception)
            {
                Console.WriteLine("Client forcefully disconnected");
                current.Close();
                clientSockets.Remove(current);
                return;
            }

            byte[] recBuf = new byte[received];
            Array.Copy(buffer, recBuf, received);
            string msg = Encoding.ASCII.GetString(recBuf);
            Console.WriteLine("Received Text : " + msg);

            if (msg.ToLower() == "exit")
            {
                current.Shutdown(SocketShutdown.Both);
                current.Close();
                clientSockets.Remove(current);
                Console.WriteLine("Client disconnected");
                return;
            }
            else if (msg != String.Empty)
            {
                try
                {
                    var result = msg.Split(new[] { ' ' }, 2);
                    if (result.Length >= 2)
                    {
                        var jsonPart = result[1];

                        var subResult = result[0].Split('\\');

                        var className = subResult[0];
                        var methodName = subResult[1];


                        var myType = Assembly.GetAssembly(typeof(ProductService)).GetTypes()
                          .FirstOrDefault(t => t.Name.Contains(className));

                        var myEntityType = Assembly.GetAssembly(typeof(Product)).GetTypes()
                            .FirstOrDefault(a => a.FullName.Contains(className));



                        var obj = JsonConvert.DeserializeObject(jsonPart, myEntityType);

                        var methods = myType.GetMethods();
                        MethodInfo myMethod = methods.FirstOrDefault(m => m.Name.Contains(methodName));

                        object myInstance = Activator.CreateInstance(myType);

                        myMethod.Invoke(myInstance, new object[] { obj });

                        byte[] data = Encoding.ASCII.GetBytes("POST Operation SUCCESSFULLY");
                        current.Send(data);
                    }
                    else
                    {
                        result = msg.Split('\\');
                        var className = result[0];
                        var methodName = result[1];

                        var myType = Assembly.GetAssembly(typeof(ProductService)).GetTypes()
                            .FirstOrDefault(t => t.Name.Contains(className));

                        if (myType != null)
                        {
                            var methods = myType.GetMethods();
                            MethodInfo myMethod = methods.FirstOrDefault(m => m.Name.Contains(methodName));

                            object myInstance = Activator.CreateInstance(myType);

                            var paramId = -1;
                            var jsonString = String.Empty;
                            object objectResponse = null;

                            if (methodName.EndsWith("Search"))
                            {
                                result[2] = $@"?{result[2]}";
                            }

                            if (result.Length >= 3)
                            {
                                if (result[2].Contains('?'))
                                {
                                    if (result[2].Contains('&'))
                                    {
                                        var res = result[2].Split(new[] { '&' }, 2);
                                        var prodName = res[0];
                                        var prodPrice = res[1];
                                        prodName = prodName.Remove(0, 1);
                                        objectResponse = myMethod.Invoke(myInstance, new object[] { prodName, decimal.Parse(prodPrice) });
                                    }
                                    else
                                    {
                                        var prodName = result[2];
                                        prodName = prodName.Remove(0, 1);
                                        objectResponse = myMethod.Invoke(myInstance, new object[] { prodName,0m });
                                    }
                                }
                                else
                                {

                                    paramId = int.Parse(result[2]);
                                    objectResponse = myMethod.Invoke(myInstance, new object[] { paramId });
                                }

                                jsonString = JsonConvert.SerializeObject(objectResponse);
                                byte[] data = Encoding.ASCII.GetBytes(jsonString);
                                current.Send(data);
                            }
                            else
                            {
                                objectResponse = myMethod.Invoke(myInstance, null);


                                jsonString = JsonConvert.SerializeObject(objectResponse);
                                byte[] data = Encoding.ASCII.GetBytes(jsonString);
                                current.Send(data);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }

            current.BeginReceive(buffer, 0, BUFFER_SIZE, SocketFlags.None, ReceiveCallBack, current);

        }

        private static void SendServiceResponseToClient(Socket client)
        {
            var result = GetAllServicesAsText();
            byte[] data = Encoding.ASCII.GetBytes(result);
            client.Send(data);
        }

        private static string GetAllServicesAsText()
        {
            var myTypes = Assembly.GetAssembly(typeof(ProductService)).GetTypes()
                .Where(t => t.Name.EndsWith("Service") && !t.Name.StartsWith("I"));

            var sb = new StringBuilder();
            foreach (var type in myTypes)
            {
                var className = type.Name.Remove(type.Name.Length - 7, 7);
                var methods = type.GetMethods().Reverse().Skip(4);

                foreach (var m in methods)
                {
                    string responseText = $@"{className}\{m.Name}";
                    var parameters = m.GetParameters();
                    foreach (var param in parameters)
                    {
                        if (param.ParameterType != typeof(string) && param.ParameterType.IsClass)
                        {
                            responseText += $@"\{param.Name}[json]";
                        }
                        else
                        {
                            responseText += $@"\{param.Name}";
                        }
                    }
                    sb.AppendLine(responseText);
                }
            }
            var result = sb.ToString();
            return result;

        }
    }
}

