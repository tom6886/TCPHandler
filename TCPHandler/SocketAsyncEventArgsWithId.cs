using System;

namespace TCPHandler
{
    internal sealed class SocketAsyncEventArgsWithId : IDisposable
    {
        private string uid = "-1";

        private bool state = false;

        private MySocketAsyncEventArgs receivesaea;

        private MySocketAsyncEventArgs sendsaea;

        internal SocketAsyncEventArgsWithId()
        {
            ReceiveSAEA = new MySocketAsyncEventArgs("Receive");
            SendSAEA = new MySocketAsyncEventArgs("Send");
        }

        /// <summary>
        /// 用户标识，跟MySocketAsyncEventArgs的UID是一样的，
        /// 在对SocketAsycnEventArgsWithId的UID属性赋值的时候也对MySocketAsyncEventArgs的UID属性赋值
        /// </summary>
        internal string UID
        {
            get { return uid; }
            set
            {
                uid = value;
                ReceiveSAEA.UID = value;
                SendSAEA.UID = value;
            }
        }

        /// <summary>
        /// 表示连接的可用与否，一旦连接被实例化放入连接池后State即变为True
        /// </summary>
        internal bool State
        {
            get { return state; }
            set { state = value; }
        }

        internal MySocketAsyncEventArgs ReceiveSAEA
        {
            set { receivesaea = value; }
            get { return receivesaea; }
        }

        internal MySocketAsyncEventArgs SendSAEA
        {
            set { sendsaea = value; }
            get { return sendsaea; }
        }

        public void Dispose()
        {
            ReceiveSAEA.Dispose();
            SendSAEA.Dispose();
        }
    }
}
