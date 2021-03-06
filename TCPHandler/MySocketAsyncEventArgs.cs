﻿using System.Net.Sockets;

namespace TCPHandler
{
    internal sealed class MySocketAsyncEventArgs : SocketAsyncEventArgs
    {
        /// <summary>
        /// 用户标识符，用来标识这个连接是那个用户的
        /// </summary>
        internal string UID;

        /// <summary>
        /// 标识该连接是用来发送信息还是监听接收信息的。
        /// param:Receive/Send，MySocketAsyncEventArgs类只带有一个参数的构造函数，说明类在实例化时就被说明是用来完成接收还是发送任务的
        /// </summary>
        private string Property;

        /// <summary>
        /// 状态标识符，使用时标识这个连接的状态，0：未使用/1：使用中，默认为0
        /// </summary>
        internal int State;

        internal MySocketAsyncEventArgs(string property)
        {
            this.State = 0;
            this.Property = property;
        }
    }
}
