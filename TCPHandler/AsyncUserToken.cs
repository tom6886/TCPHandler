﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace TCPHandler
{
    public class AsyncUserToken
    {
        /// <summary>
        /// 用户标识
        /// </summary>
        public string UID { get; set; }

        /// <summary>  
        /// 远程地址  
        /// </summary>  
        public IPEndPoint Remote { get; set; }

        /// <summary>  
        /// 通信SOKET  
        /// </summary>  
        public Socket Socket { get; set; }

        /// <summary>  
        /// 连接时间  
        /// </summary>  
        public DateTime ConnectTime { get; set; }

        /// <summary>
        /// 最近一次通讯时间
        /// </summary>
        public DateTime FreshTime { get; set; }

        /// <summary>  
        /// 数据缓存区  
        /// </summary>  
        public List<byte> Buffer { get; set; }


        public AsyncUserToken()
        {
            this.Buffer = new List<byte>();
        }
    }
}
