using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace TCPHandler
{
    public sealed class SocketListener : IDisposable
    {
        #region 私有变量
        /// <summary>
        /// 缓冲区
        /// </summary>
        private BufferManager bufferManager;
        /// <summary>
        /// 服务器端Socket
        /// </summary>
        private Socket listenSocket;
        /// <summary>
        /// 服务同步锁
        /// </summary>
        private static Mutex mutex = new Mutex();
        /// <summary>
        /// 当前连接数
        /// </summary>
        private Int32 numConnections;
        /// <summary>
        /// 最大并发量
        /// </summary>
        private Int32 numConcurrence;
        /// <summary>
        /// 服务器状态
        /// </summary>
        private ServerState serverstate;
        /// <summary>
        /// 读取写入字节
        /// </summary>
        private const Int32 opsToPreAlloc = 1;
        /// <summary>
        /// Socket连接池
        /// </summary>
        private SocketAsyncEventArgsPool readWritePool;
        /// <summary>
        /// 并发控制信号量
        /// </summary>
        private Semaphore semaphoreAcceptedClients;
        #endregion

        #region 委托和方法
        /// <summary>
        /// 回调委托
        /// </summary>
        /// <param name="IP"></param>
        /// <returns></returns>
        public delegate string GetIDByEndPointFun(IPEndPoint endPoint);
        /// <summary>
        /// 回调方法实例
        /// </summary>
        public event GetIDByEndPointFun GetIDByEndPoint;
        /// <summary>
        /// 接收到信息时的事件委托
        /// </summary>
        /// <param name="info"></param>
        public delegate void ReceiveMsgHandler(string uid, byte[] info);
        /// <summary>
        /// 接收到信息时的事件
        /// </summary>
        public event ReceiveMsgHandler OnMsgReceived;
        /// <summary>
        /// 开始监听数据的委托
        /// </summary>
        public delegate void StartListenHandler();
        /// <summary>
        /// 开始监听数据的事件
        /// </summary>
        public event StartListenHandler StartListenThread;
        /// <summary>
        /// 发送信息完成后的委托
        /// </summary>
        /// <param name="successorfalse"></param>
        public delegate void SendCompletedHandler(string uid, SocketError error);
        /// <summary>
        /// 发送信息完成后的事件
        /// </summary>
        public event SendCompletedHandler OnSended;
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

        #region 属性
        /// <summary>
        /// 获取当前的并发数
        /// </summary>
        public Int32 NumConnections
        {
            get { return this.numConnections; }
        }
        /// <summary>
        /// 最大并发数
        /// </summary>
        public Int32 MaxConcurrence
        {
            get { return this.numConcurrence; }
        }
        /// <summary>
        /// 返回服务器状态
        /// </summary>
        public ServerState State
        {
            get { return serverstate; }
        }
        /// <summary>
        /// 获取当前在线用户的UID
        /// </summary>
        public string[] OnlineUID
        {
            get { return readWritePool.OnlineUID; }
        }
        #endregion

        #region 初始化和启动
        /// <summary>
        /// 初始化服务器端
        /// </summary>
        /// <param name="numConcurrence">并发的连接数量(1000以上)</param>
        /// <param name="receiveBufferSize">每一个收发缓冲区的大小(32768)</param>
        public SocketListener(Int32 numConcurrence, Int32 receiveBufferSize)
        {
            serverstate = ServerState.Initialing;
            this.numConnections = 0;
            this.numConcurrence = numConcurrence;
            this.bufferManager = new BufferManager(receiveBufferSize * numConcurrence * opsToPreAlloc, receiveBufferSize);
            this.readWritePool = new SocketAsyncEventArgsPool(numConcurrence);
            this.semaphoreAcceptedClients = new Semaphore(numConcurrence, numConcurrence);
        }

        /// <summary>
        /// 服务端初始化
        /// </summary>
        public void Init()
        {
            this.bufferManager.InitBuffer();
            SocketAsyncEventArgsWithId readWriteEventArgWithId;
            for (Int32 i = 0; i < this.numConcurrence; i++)
            {
                readWriteEventArgWithId = new SocketAsyncEventArgsWithId();
                readWriteEventArgWithId.ReceiveSAEA.Completed += new EventHandler<SocketAsyncEventArgs>(OnReceiveCompleted);
                readWriteEventArgWithId.SendSAEA.Completed += new EventHandler<SocketAsyncEventArgs>(OnSendCompleted);
                //只给接收的SocketAsyncEventArgs设置缓冲区
                this.bufferManager.SetBuffer(readWriteEventArgWithId.ReceiveSAEA);
                this.readWritePool.Push(readWriteEventArgWithId);
            }
            serverstate = ServerState.Inited;
        }

        /// <summary>
        /// 启动服务器
        /// </summary>
        /// <param name="data">端口号</param>
        public void Start(Object data)
        {
            Int32 port = (Int32)data;
            IPAddress[] addresslist = Dns.GetHostEntry(Environment.MachineName).AddressList;
            IPEndPoint localEndPoint = new IPEndPoint(addresslist[addresslist.Length - 1], port);
            this.listenSocket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            if (localEndPoint.AddressFamily == AddressFamily.InterNetworkV6)
            {
                this.listenSocket.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)27, false);
                this.listenSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, localEndPoint.Port));
            }
            else
            {
                this.listenSocket.Bind(localEndPoint);
            }
            this.listenSocket.Listen(100);
            this.StartAccept(null);
            //开始监听已连接用户的发送数据
            StartListenThread?.Invoke();
            serverstate = ServerState.Running;
            mutex.WaitOne();
        }

        /// <summary>
        /// 停止服务器
        /// </summary>
        public void Stop()
        {
            if (listenSocket != null)
                listenSocket.Close();
            listenSocket = null;
            Dispose();
            mutex.ReleaseMutex();
            serverstate = ServerState.Stoped;
        }
        #endregion

        #region 接入客户端
        /// <summary>
        /// 接入连接
        /// </summary>
        /// <param name="acceptEventArg"></param>
        private void StartAccept(SocketAsyncEventArgs acceptEventArg)
        {
            if (acceptEventArg == null)
            {
                acceptEventArg = new SocketAsyncEventArgs();
                acceptEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(OnAcceptCompleted);
            }
            else
                acceptEventArg.AcceptSocket = null;

            this.semaphoreAcceptedClients.WaitOne();

            if (!this.listenSocket.AcceptAsync(acceptEventArg))
            {
                this.ProcessAccept(acceptEventArg);
            }
        }

        private void OnAcceptCompleted(object sender, SocketAsyncEventArgs e)
        {
            this.ProcessAccept(e);
        }

        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            if (GetIDByEndPoint == null)
                throw new ArgumentException("The function GetIDByEndPoint can not be null!");
            if (e.LastOperation != SocketAsyncOperation.Accept)    //检查上一次操作是否是Accept，不是就返回
                return;
            if (e.BytesTransferred <= 0)    //检查发送的长度是否大于0,不是就返回
                return;

            string UID = GetIDByEndPoint(e.AcceptSocket.RemoteEndPoint as IPEndPoint);   //根据IP获取用户的UID

            if (string.IsNullOrEmpty(UID))
                return;
            if (readWritePool.BusyPoolContains(UID))    //判断现在的用户是否已经连接，避免同一用户开两个连接
                return;

            SocketAsyncEventArgsWithId readEventArgsWithId = this.readWritePool.Pop(UID);

            AsyncUserToken userToken = new AsyncUserToken
            {
                Socket = e.AcceptSocket,
                ConnectTime = DateTime.Now,
                Remote = e.AcceptSocket.RemoteEndPoint,
                IPAddress = ((IPEndPoint)(e.AcceptSocket.RemoteEndPoint)).Address
            };

            readEventArgsWithId.ReceiveSAEA.UserToken = userToken;
            readEventArgsWithId.SendSAEA.UserToken = userToken;

            Interlocked.Increment(ref this.numConnections);
            this.StartAccept(e);
        }
        #endregion

        #region 接收消息
        private void OnReceiveCompleted(object sender, SocketAsyncEventArgs e)
        {
            ProcessReceive(e);
        }

        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            if (GetPackageLength == null)
                throw new ArgumentException("The function GetPackageLength can not be null!");

            if (e.LastOperation != SocketAsyncOperation.Receive)
                return;

            if (!(e.BytesTransferred > 0 && e.SocketError == SocketError.Success))
            {
                CloseClientSocket(((MySocketAsyncEventArgs)e).UID);
                return;
            }

            AsyncUserToken token = (AsyncUserToken)e.UserToken;
            byte[] data = new byte[e.BytesTransferred];
            Array.Copy(e.Buffer, e.Offset, data, 0, e.BytesTransferred);
            lock (token.Buffer)
            {
                token.Buffer.AddRange(data);
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
                packageLen = GetPackageLength(token.Buffer.ToArray(), out headLen); //获取包头标记的数据长度以及包头的长度

                if (packageLen > token.Buffer.Count - headLen) { break; }    //如果实际数据的长度小于数据中标记的长度,则退出循环,让程序继续接收  

                byte[] rev = token.Buffer.GetRange(headLen, packageLen).ToArray();    //包够长时,则提取出来,交给后面的程序去处理  

                //从数据池中移除这组数据，若是同时接收了多个数据包，则token.Buffer中仍会存在数据，循环会继续
                lock (token.Buffer)
                {
                    token.Buffer.RemoveRange(0, packageLen + headLen);
                }

                OnMsgReceived?.Invoke(((MySocketAsyncEventArgs)e).UID, rev);

            } while (token.Buffer.Count > headLen);

            if (!token.Socket.ReceiveAsync(e))
                ProcessReceive(e);
        }
        #endregion

        #region 发送消息
        private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
        {
            ProcessSend(e);
        }

        private void ProcessSend(SocketAsyncEventArgs e)
        {
            if (OnSended == null)
                throw new ArgumentException("The function OnSended can not be null!");

            if (e.LastOperation != SocketAsyncOperation.Send)
                return;

            if (e.BytesTransferred > 0)
            {
                OnSended(((MySocketAsyncEventArgs)e).UID, e.SocketError);
            }
            else
            {
                this.CloseClientSocket(((MySocketAsyncEventArgs)e).UID);
            }
        }

        /// <summary>
        /// 发送信息
        /// </summary>
        /// <param name="uid">要发送的用户的uid</param>
        /// <param name="msg">消息体</param>
        public void Send(string uid, string msg)
        {
            if (GetSendMessage == null)
                throw new ArgumentException("The function GetSendMessage can not be null!");

            if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(msg))
                return;

            SocketAsyncEventArgsWithId socketWithId = readWritePool.FindByUID(uid);

            if (socketWithId == null) { OnSended(uid, SocketError.NotSocket); return; }

            MySocketAsyncEventArgs e = socketWithId.SendSAEA;

            if (e.SocketError != SocketError.Success)
            {
                OnSended(uid, e.SocketError);
                this.CloseClientSocket(e.UID);
                return;
            }

            int i = 0;

            try
            {
                byte[] sendbuffer = GetSendMessage(msg);

                e.SetBuffer(sendbuffer, 0, sendbuffer.Length);

                AsyncUserToken token = (AsyncUserToken)e.UserToken;

                if (!token.Socket.SendAsync(e))
                {
                    this.ProcessSend(e);
                }
            }
            catch (Exception)
            {
                if (i <= 5)
                {
                    i++;
                    //如果发送出现异常就延迟0.01秒再发
                    Thread.Sleep(10);
                    Send(uid, msg);
                }
                else
                {
                    OnSended(uid, SocketError.SocketError);
                }
            }
        }
        #endregion

        #region 关闭连接
        private void CloseClientSocket(string uid)
        {
            if (string.IsNullOrEmpty(uid))
                return;
            SocketAsyncEventArgsWithId saeaw = readWritePool.FindByUID(uid);
            if (saeaw == null)
                return;
            Socket s = saeaw.ReceiveSAEA.UserToken as Socket;
            try
            {
                s.Shutdown(SocketShutdown.Both);
            }
            catch (Exception)
            {
                //客户端已经关闭
            }
            this.semaphoreAcceptedClients.Release();
            Interlocked.Decrement(ref this.numConnections);
            this.readWritePool.Push(saeaw);
        }
        #endregion

        #region IDisposable Members
        public void Dispose()
        {
            bufferManager.Dispose();
            bufferManager = null;
            readWritePool.Dispose();
            readWritePool = null;
        }
        #endregion
    }

    public enum ServerState { Initialing, Inited, Ready, Running, Stoped }
}
