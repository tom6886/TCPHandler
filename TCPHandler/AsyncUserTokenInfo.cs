using System;
using System.Net;

namespace TCPHandler
{
    public class AsyncUserTokenInfo
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
        /// 连接时间  
        /// </summary>  
        public DateTime ConnectTime { get; set; }

        /// <summary>
        /// 最近一次通讯时间
        /// </summary>
        public DateTime FreshTime { get; set; }
    }
}
