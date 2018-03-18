using System;
using System.Collections.Generic;
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
        public delegate void ReceiveMsgHandler(AsyncUserToken token, byte[] info);
        /// <summary>
        /// 接收到信息时的事件
        /// </summary>
        public event ReceiveMsgHandler OnMsgReceived;
        /// <summary>
        /// 发送信息完成后的委托
        /// </summary>
        /// <param name="successorfalse"></param>
        public delegate void SendCompletedHandler(AsyncUserToken token, SocketError error);
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
        /// <summary>
        /// 客户端连接数量变化的委托
        /// </summary>
        /// <param name="number"></param>
        /// <param name="token"></param>
        public delegate void ClientNumberChangeHandler(int number, AsyncUserToken token);
        /// <summary>
        /// 客户端连接数量变化的事件
        /// </summary>
        public event ClientNumberChangeHandler OnClientNumberChange;
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

        /// <summary>
        /// 获取当前在线用户的UserToken
        /// </summary>
        public List<AsyncUserTokenInfo> OnlineUserToken
        {
            get { return readWritePool.OnlineUserToken; }
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
        /// <param name="endPoint"></param>
        public void Start(IPEndPoint endPoint)
        {
            this.listenSocket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            if (endPoint.AddressFamily == AddressFamily.InterNetworkV6)
            {
                this.listenSocket.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)27, false);
                this.listenSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, endPoint.Port));
            }
            else
            {
                this.listenSocket.Bind(endPoint);
            }
            this.listenSocket.Listen(100);
            this.StartAccept(null);
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
            //OnClientNumberChange?.Invoke(-readWritePool.busypool.Count, null);
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

            string UID = GetIDByEndPoint(e.AcceptSocket.RemoteEndPoint as IPEndPoint);   //根据IP获取用户的UID

            if (string.IsNullOrEmpty(UID))
                return;
            if (readWritePool.BusyPoolContains(UID))    //判断现在的用户是否已经连接，避免同一用户开两个连接
                return;

            SocketAsyncEventArgsWithId readEventArgsWithId = this.readWritePool.Pop(UID);

            AsyncUserToken userToken = new AsyncUserToken
            {
                UID = UID,
                Socket = e.AcceptSocket,
                ConnectTime = DateTime.Now,
                FreshTime = DateTime.Now,
                Remote = e.AcceptSocket.RemoteEndPoint as IPEndPoint
            };

            readEventArgsWithId.ReceiveSAEA.UserToken = userToken;
            readEventArgsWithId.SendSAEA.UserToken = userToken;

            //激活接收
            if (!e.AcceptSocket.ReceiveAsync(readEventArgsWithId.ReceiveSAEA))
            {
                ProcessReceive(readEventArgsWithId.ReceiveSAEA);
            }

            Interlocked.Increment(ref this.numConnections);
            OnClientNumberChange?.Invoke(1, userToken);
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
            if (!(e.BytesTransferred > 0 && e.SocketError == SocketError.Success))
            {
                CloseClientSocket(((MySocketAsyncEventArgs)e).UID);
                return;
            }

            if (GetPackageLength == null)
                throw new ArgumentException("The function GetPackageLength can not be null!");

            if (e.LastOperation != SocketAsyncOperation.Receive)
                return;

            AsyncUserToken token = (AsyncUserToken)e.UserToken;
            token.FreshTime = DateTime.Now;
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

                OnMsgReceived?.Invoke(token, rev);

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

            AsyncUserToken token = (AsyncUserToken)e.UserToken;

            if (e.BytesTransferred > 0)
            {
                OnSended(token, e.SocketError);
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

            byte[] sendbuffer = GetSendMessage(msg);

            Send(uid, sendbuffer);
        }

        /// <summary>
        /// 发送信息
        /// </summary>
        /// <param name="uid">要发送的用户的uid</param>
        /// <param name="msg">消息体</param>
        public void Send(string uid, byte[] sendbuffer)
        {
            if (string.IsNullOrEmpty(uid) || sendbuffer.Length == 0)
                return;

            SocketAsyncEventArgsWithId socketWithId = readWritePool.FindByUID(uid);

            if (socketWithId == null) { OnSended(null, SocketError.NotSocket); return; }

            MySocketAsyncEventArgs e = socketWithId.SendSAEA;

            AsyncUserToken token = (AsyncUserToken)e.UserToken;
            token.FreshTime = DateTime.Now;

            if (e.SocketError != SocketError.Success)
            {
                OnSended(token, e.SocketError);
                this.CloseClientSocket(e.UID);
                return;
            }

            int i = 0;

            try
            {
                e.SetBuffer(sendbuffer, 0, sendbuffer.Length);

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
                    Send(uid, sendbuffer);
                }
                else
                {
                    OnSended(token, SocketError.SocketError);
                }
            }
        }
        #endregion

        #region 关闭连接
        public void CloseClientSocket(string uid)
        {
            if (string.IsNullOrEmpty(uid))
                return;

            SocketAsyncEventArgsWithId saeaw = readWritePool.FindByUID(uid);
            if (saeaw == null)
                return;

            //先将连接从活跃池中取出，再进行断开，因为断开会触发接收消息
            this.readWritePool.Push(saeaw);

            AsyncUserToken token = saeaw.ReceiveSAEA.UserToken as AsyncUserToken;
            token.Socket.Shutdown(SocketShutdown.Both);

            this.semaphoreAcceptedClients.Release();
            Interlocked.Decrement(ref this.numConnections);
            OnClientNumberChange?.Invoke(-1, token);
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
