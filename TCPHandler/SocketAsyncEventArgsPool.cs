using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace TCPHandler
{
    internal sealed class SocketAsyncEventArgsPool : IDisposable
    {
        /// <summary>
        /// 连接栈，用来存放空闲的连接的，使用时pop出来，使用完后push进去。
        /// </summary>
        internal ConcurrentStack<SocketAsyncEventArgsWithId> pool;

        /// <summary>
        /// busypool是一个字典类型的，用来存放正在使用的连接的，key是用户标识，
        /// 设计的目的是为了统计在线用户数目和查找相应用户的连接，当然这是很重要的，为什么设计成字典类型的，
        /// 是因为我们查找时遍历字典的关键字就行了而不用遍历每一项的UID，这样效率会有所提高。
        /// </summary>
        internal ConcurrentDictionary<string, SocketAsyncEventArgsWithId> busypool;

        /// <summary>
        /// 这是一个存放用户标识的数组，起一个辅助的功能
        /// </summary>
        private string[] keys;

        /// <summary>
        /// 返回连接池中可用的连接数
        /// </summary>
        internal Int32 Count { get { return this.pool.Count; } }

        /// <summary>
        /// 返回在线用户的标识列表
        /// </summary>
        internal string[] OnlineUID
        {
            get
            {
                busypool.Keys.CopyTo(keys, 0);
                return keys;
            }
        }

        internal SocketAsyncEventArgsPool(Int32 capacity)
        {
            keys = new string[capacity];
            Stack<SocketAsyncEventArgsWithId> _pool = new Stack<SocketAsyncEventArgsWithId>(capacity);
            this.pool = new ConcurrentStack<SocketAsyncEventArgsWithId>(_pool);
            Dictionary<string, SocketAsyncEventArgsWithId> _busypool = new Dictionary<string, SocketAsyncEventArgsWithId>(capacity);
            this.busypool = new ConcurrentDictionary<string, SocketAsyncEventArgsWithId>(_busypool);
        }

        /// <summary>
        /// 用于获取一个可用连接给用户
        /// </summary>
        /// <param name="uid"></param>
        /// <returns></returns>
        internal SocketAsyncEventArgsWithId Pop(string uid)
        {
            if (uid == string.Empty || uid == "")
                return null;

            this.pool.TryPop(out SocketAsyncEventArgsWithId si);
            si.UID = uid;
            si.State = true;    //mark the state of pool is not the initial step
            busypool.TryAdd(uid, si);
            return si;
        }

        /// <summary>
        /// 把一个使用完的连接放回连接池
        /// </summary>
        /// <param name="item">使用完的连接</param>
        internal void Push(SocketAsyncEventArgsWithId item)
        {
            if (item == null)
                throw new ArgumentNullException("SocketAsyncEventArgsWithId对象为空");
            if (item.State == true)
            {
                if (busypool.Keys.Count != 0)
                {
                    if (busypool.Keys.Contains(item.UID))
                        busypool.TryRemove(item.UID, out item);
                    else
                        throw new ArgumentException("SocketAsyncEventWithId不在忙碌队列中");
                }
                else
                    throw new ArgumentException("忙碌队列为空");
            }
            item.UID = "-1";
            item.State = false;
            this.pool.Push(item);
        }

        /// <summary>
        /// 查找在线用户连接，返回这个连接
        /// </summary>
        /// <param name="uid"></param>
        /// <returns></returns>
        internal SocketAsyncEventArgsWithId FindByUID(string uid)
        {
            if (string.IsNullOrEmpty(uid) || !busypool.ContainsKey(uid))
                return null;

            return busypool[uid];
        }

        /// <summary>
        /// 判断某个用户的连接是否在线
        /// </summary>
        /// <param name="uid"></param>
        /// <returns></returns>
        internal bool BusyPoolContains(string uid)
        {
            return busypool.ContainsKey(uid);
        }

        #region IDisposable Members

        public void Dispose()
        {
            pool.Clear();
            busypool.Clear();
            pool = null;
            busypool = null;
        }

        #endregion
    }
}
