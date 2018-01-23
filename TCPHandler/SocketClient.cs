using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace TCPHandler
{
    public class SocketClient : IDisposable
    {
        #region 私有变量
        /// <summary>
        /// 客户端连接Socket
        /// </summary>
        private Socket clientSocket;
        /// <summary>
        /// 连接点
        /// </summary>
        private IPEndPoint hostEndPoint;
        /// <summary>
        /// 连接信号量
        /// </summary>
        private static AutoResetEvent autoConnectEvent = new AutoResetEvent(false);
        /// <summary>
        /// 监听接收的SocketAsyncEventArgs
        /// </summary>
        private SocketAsyncEventArgs listenerSocketAsyncEventArgs;
        /// <summary>
        /// 接收数据的对象
        /// </summary>
        private List<byte> receiveBufferList;
        /// <summary>
        /// 监听发送的SocketAsyncEventArgs
        /// </summary>
        private List<MySocketAsyncEventArgs> senderSocketAsyncEventArgsList;
        /// <summary>
        /// 当前连接状态
        /// </summary>
        public Boolean Connected { get { return clientSocket != null && clientSocket.Connected; } }
        #endregion

        #region 委托和方法
        /// <summary>
        /// 开始监听数据的委托
        /// </summary>
        public delegate void StartListenHandler();
        /// <summary>
        /// 开始监听数据的事件
        /// </summary>
        public event StartListenHandler StartListenThread;
        /// <summary>
        /// 与服务端断开连接的委托
        /// </summary>
        public delegate void ServerStopHandler();
        /// <summary>
        /// 与服务端断开连接的事件
        /// </summary>
        public event ServerStopHandler OnServerStop;
        /// <summary>
        /// 接受到数据时的委托
        /// </summary>
        /// <param name="info"></param>
        public delegate void ReceiveMsgHandler(byte[] info);
        /// <summary>
        /// 接收到数据时调用的事件
        /// </summary>
        public event ReceiveMsgHandler OnMsgReceived;
        /// <summary>
        /// 发送信息完成的委托
        /// </summary>
        /// <param name="successorfalse"></param>
        public delegate void SendCompleted(bool successorfalse);
        /// <summary>
        /// 发送信息完成的事件
        /// </summary>
        public event SendCompleted OnSended;
        /// <summary>
        /// 获取数据包长度的委托
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public delegate int GetPackageLengthHandler(byte[] data, out int headLength);
        /// <summary>
        /// 获取数据包长度的事件
        /// </summary>
        public event GetPackageLengthHandler GetPackageLength;
        /// <summary>
        /// 获取处理后的发送信息的委托
        /// </summary>
        /// <param name="msg"></param>
        /// <returns></returns>
        public delegate byte[] GetSendMessageHandler(string msg);
        /// <summary>
        /// 获取处理后的发送信息的事件
        /// </summary>
        public event GetSendMessageHandler GetSendMessage;
        #endregion

        #region 初始化
        /// <summary>
        /// 初始化客户端
        /// </summary>
        public SocketClient(IPEndPoint endPoint)
        {
            this.hostEndPoint = endPoint;
            this.clientSocket = new Socket(this.hostEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            receiveBufferList = new List<byte>();
            senderSocketAsyncEventArgsList = new List<MySocketAsyncEventArgs>();
        }
        #endregion

        #region 连接服务端
        /// <summary>
        /// 连接服务端
        /// </summary>
        public SocketError Connect()
        {
            SocketAsyncEventArgs connectArgs = new SocketAsyncEventArgs();
            connectArgs.UserToken = this.clientSocket;
            connectArgs.RemoteEndPoint = this.hostEndPoint;
            connectArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnConnect);
            clientSocket.ConnectAsync(connectArgs);
            //等待连接结果
            autoConnectEvent.WaitOne();
            return connectArgs.SocketError;
        }

        /// <summary>
        /// 连接的完成方法
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnConnect(object sender, SocketAsyncEventArgs e)
        {
            autoConnectEvent.Set();

            //如果连接成功,则初始化socketAsyncEventArgs
            if (e.SocketError != SocketError.Success) { return; }

            InitReciveArg();

            InitSendArg();
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            clientSocket.Disconnect(false);
        }
        #endregion

        #region 接收消息
        private void InitReciveArg()
        {
            listenerSocketAsyncEventArgs = new SocketAsyncEventArgs();
            byte[] receiveBuffer = new byte[32768];
            listenerSocketAsyncEventArgs.UserToken = clientSocket;
            listenerSocketAsyncEventArgs.SetBuffer(receiveBuffer, 0, receiveBuffer.Length);
            listenerSocketAsyncEventArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnReceiveCompleted);

            StartListenThread?.Invoke();

            if (!clientSocket.ReceiveAsync(listenerSocketAsyncEventArgs))
                ProcessReceive(listenerSocketAsyncEventArgs);
        }

        private void OnReceiveCompleted(object sender, SocketAsyncEventArgs e)
        {
            ProcessReceive(e);
        }

        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            if (!(e.BytesTransferred > 0 && e.SocketError == SocketError.Success))
            {
                ProcessError(e);
                return;
            }

            if (GetPackageLength == null)
                throw new ArgumentException("The function GetPackageLength can not be null!");

            if (e.LastOperation != SocketAsyncOperation.Receive)
                return;

            byte[] data = new byte[e.BytesTransferred];
            Array.Copy(e.Buffer, e.Offset, data, 0, e.BytesTransferred);
            lock (receiveBufferList)
            {
                receiveBufferList.AddRange(data);
            }

            int headLen = 0;
            int packageLen = 0;

            //如果当客户发送大数据流的时候,e.BytesTransferred的大小就会比客户端发送过来的要小,  
            //需要分多次接收.所以收到包的时候,先判断包头的大小.够一个完整的包再处理.  
            //如果客户短时间内发送多个小数据包时, 服务器可能会一次性把他们全收了.  
            //这样如果没有一个循环来控制,那么只会处理第一个包,  
            //剩下的包全部留在token.Buffer中了,只有等下一个数据包过来后,才会放出一个来.  
            do
            {
                packageLen = GetPackageLength(receiveBufferList.ToArray(), out headLen); //获取包头标记的数据长度以及包头的长度

                if (packageLen > receiveBufferList.Count - headLen) { break; }    //如果实际数据的长度小于数据中标记的长度,则退出循环,让程序继续接收  

                byte[] rev = receiveBufferList.GetRange(headLen, packageLen).ToArray();    //包够长时,则提取出来,交给后面的程序去处理  

                //从数据池中移除这组数据，若是同时接收了多个数据包，则token.Buffer中仍会存在数据，循环会继续
                lock (receiveBufferList)
                {
                    receiveBufferList.RemoveRange(0, packageLen + headLen);
                }

                //开启新线程处理消息，不影响继续接收消息
                Thread thread = new Thread(new ParameterizedThreadStart((obj) =>
                {
                    OnMsgReceived?.Invoke((byte[])obj);
                }));
                thread.IsBackground = true;
                thread.Start(rev);

            } while (receiveBufferList.Count > headLen);

            if (!(e.UserToken as Socket).ReceiveAsync(e))
                ProcessReceive(e);
        }
        #endregion

        #region 发送消息
        private MySocketAsyncEventArgs InitSendArg()
        {
            MySocketAsyncEventArgs senderSocketAsyncEventArgs = new MySocketAsyncEventArgs("Send");
            senderSocketAsyncEventArgs.UserToken = this.clientSocket;
            senderSocketAsyncEventArgs.RemoteEndPoint = this.hostEndPoint;
            senderSocketAsyncEventArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnSendCompleted);
            lock (senderSocketAsyncEventArgsList)
            {
                senderSocketAsyncEventArgsList.Add(senderSocketAsyncEventArgs);
            }
            return senderSocketAsyncEventArgs;
        }

        private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
        {
            MySocketAsyncEventArgs senderSocketAsyncEventArgs = (MySocketAsyncEventArgs)e;

            senderSocketAsyncEventArgs.State = 0;

            if (OnSended == null)
                throw new ArgumentException("The function OnSended can not be null!");

            if (e.LastOperation != SocketAsyncOperation.Send)
                return;

            if (e.SocketError == SocketError.Success)
            {
                OnSended(true);
            }
            else
            {
                OnSended(false);
                this.ProcessError(e);
            }
        }

        /// <summary>
        /// 发送信息
        /// </summary>
        /// <param name="msg">消息体</param>
        public void Send(string msg)
        {
            byte[] sendbuffer = GetSendMessage(msg);

            Send(sendbuffer);
        }

        /// <summary>
        /// 发送信息
        /// </summary>
        /// <param name="sendbuffer">消息体</param>
        public void Send(byte[] sendbuffer)
        {
            if (!Connected) { throw new SocketException((Int32)SocketError.NotConnected); }

            if (GetSendMessage == null)
                throw new ArgumentException("The function GetSendMessage can not be null!");

            if (sendbuffer.Length == 0)
                return;

            MySocketAsyncEventArgs senderSocketAsyncEventArgs = senderSocketAsyncEventArgsList.Find(q => q.State == 0);

            if (senderSocketAsyncEventArgs == null) { senderSocketAsyncEventArgs = InitSendArg(); }

            lock (senderSocketAsyncEventArgs)
            {
                senderSocketAsyncEventArgs.State = 1;

                senderSocketAsyncEventArgs.SetBuffer(sendbuffer, 0, sendbuffer.Length);
            }

            clientSocket.SendAsync(senderSocketAsyncEventArgs);
        }
        #endregion

        #region 关闭连接
        private void ProcessError(SocketAsyncEventArgs e)
        {
            Socket s = (Socket)e.UserToken;
            if (s.Connected)
            {
                try
                {
                    s.Shutdown(SocketShutdown.Both);
                }
                catch (Exception)
                {
                    // throws if client process has already closed  
                }
                finally
                {
                    if (s.Connected)
                    {
                        s.Close();
                    }
                }
            }

            //这里一定要记得把事件移走,如果不移走,当断开服务器后再次连接上,会造成多次事件触发.  
            foreach (MySocketAsyncEventArgs arg in senderSocketAsyncEventArgsList)
                arg.Completed -= OnSendCompleted;

            listenerSocketAsyncEventArgs.Completed -= OnReceiveCompleted;

            OnServerStop?.Invoke();
        }
        #endregion

        #region IDisposable Members
        public void Dispose()
        {
            autoConnectEvent.Close();
            if (this.clientSocket.Connected)
            {
                this.clientSocket.Close();
            }
        }
        #endregion
    }
}
